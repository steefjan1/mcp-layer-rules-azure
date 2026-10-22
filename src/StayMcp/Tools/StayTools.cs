using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.Mcp;
using Microsoft.Extensions.Logging;
using StayMcp.Services;

namespace StayMcp.Tools;

/// <summary>
/// Four tools, one per task (rule 2), not one per endpoint. The API has five routes;
/// GET /bookings/{id} is deliberately not a tool: the agent gets the booking back from
/// book_stay and has no task that needs it separately (rule 3).
///
/// Rule 4, labelling reads and writes: the Functions MCP trigger attribute takes a name and
/// a description, nothing else, so there is no readOnlyHint or destructiveHint to set here.
/// measure/list_tools.py checks what tools/list actually returns. The confirmation the
/// infographic says to keep is enforced below (confirm=true) and, more importantly, by
/// the API's role checks, which no annotation can switch off.
///
/// Function names end in "Tool" so they can never collide with an HTTP function name;
/// the host indexes both trigger types into one namespace.
/// </summary>
public sealed class StayTools(StayApiClient api, QuoteVerifier verifier, ILogger<StayTools> log)
{
    private static readonly JsonSerializerOptions Pretty = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    [Function("FindStaysTool")]
    public async Task<string> FindStays(
        [McpToolTrigger("find_stays",
            "Read-only. Finds short-stay listings for a city and party size. Use this first when the user asks for a place to stay; it returns listing ids that quote_stay needs. Does not change anything.")]
        ToolInvocationContext context,
        [McpToolProperty("city", "City to search, for example Amsterdam, Utrecht, Lisbon or Berlin. Pass an empty string for all cities.", isRequired: true)] string city,
        [McpToolProperty("guests", "Number of guests. Listings that take fewer guests are excluded. Pass 1 if unknown.", isRequired: true)] int guests,
        CancellationToken ct)
    {
        var r = await api.GetAsync($"/listings?city={Uri.EscapeDataString(city ?? "")}&guests={Math.Max(1, guests)}", ct);
        return Render(r, "find_stays");
    }

    [Function("QuoteStayTool")]
    public async Task<string> QuoteStay(
        [McpToolTrigger("quote_stay",
            "Read-only. Prices a stay for one listing and returns a signed quote with a total and an expiry. Call this before book_stay; book_stay only accepts the exact signed quote this tool returned. Does not reserve anything.")]
        ToolInvocationContext context,
        [McpToolProperty("listingId", "Listing id from find_stays, for example ams-02.", isRequired: true)] string listingId,
        [McpToolProperty("checkIn", "Check-in date, ISO format yyyy-MM-dd.", isRequired: true)] string checkIn,
        [McpToolProperty("checkOut", "Check-out date, ISO format yyyy-MM-dd, after checkIn.", isRequired: true)] string checkOut,
        [McpToolProperty("guests", "Number of guests.", isRequired: true)] int guests,
        CancellationToken ct)
    {
        // The Functions MCP extension parses a date-looking argument before binding it, so a string
        // parameter receives DateTimeOffset.ToString() ("12/05/2026 00:00:00 +00:00"), not "2026-12-05".
        // The door normalises; the API keeps its strict yyyy-MM-dd contract.
        var r = await api.PostAsync("/quotes", new { listingId, checkIn = IsoDate(checkIn), checkOut = IsoDate(checkOut), guests }, null, ct);
        return Render(r, "quote_stay");
    }

    [Function("BookStayTool")]
    public async Task<string> BookStay(
        [McpToolTrigger("book_stay",
            "Writes. Books a stay from a signed quote returned by quote_stay and charges the quoted total. Irreversible without cancel_stay. Show the user the total and dates and get an explicit yes before calling with confirm=true. Safe to retry: the same quote never books twice.")]
        ToolInvocationContext context,
        [McpToolProperty("signedQuote", "The complete signedQuote JSON exactly as returned by quote_stay (quote, keyId, signature), as a string. Do not edit any field.", isRequired: true)] string signedQuoteJson,
        [McpToolProperty("guestName", "Name of the guest the booking is for.", isRequired: true)] string guestName,
        [McpToolProperty("confirm", "Must be true. Set it only after the user explicitly agreed to the quoted total and dates.", isRequired: true)] bool confirm,
        CancellationToken ct)
    {
        if (!confirm)
            return Refuse("book_stay", "confirm was not true. Show the user the total, dates and listing from the quote, ask for a yes, then call again with confirm=true.");

        JsonElement signedQuote;
        try { signedQuote = JsonDocument.Parse(signedQuoteJson).RootElement.Clone(); }
        catch (JsonException) { return Refuse("book_stay", "signedQuote is not valid JSON. Pass the quote_stay result unchanged."); }

        // Provenance, agent side: refuse before the write call if the quote is not the one the API issued.
        var (ok, why) = await verifier.VerifyAsync(signedQuote, ct);
        if (!ok)
        {
            log.LogWarning("book_stay refused: {Reason}", why);
            return Refuse("book_stay", $"quote did not verify ({why}). Call quote_stay again and pass its result unchanged.");
        }

        // Idempotency key derived from the quote id: a retry of the same quote is the same booking.
        var quoteId = signedQuote.GetProperty("quote").GetProperty("quoteId").GetString()!;
        var r = await api.PostAsync("/bookings", new { signedQuote, guestName }, $"mcp-{quoteId}", ct);
        if (r.Ok && r.Replayed)
            return "This quote was already booked; here is the existing booking (no second charge was made):\n" + Pretty_(r.Body);
        return Render(r, "book_stay");
    }

    [Function("CancelStayTool")]
    public async Task<string> CancelStay(
        [McpToolTrigger("cancel_stay",
            "Destructive. Cancels a confirmed booking by booking id. Cannot be undone. Get an explicit yes from the user, naming the booking, before calling with confirm=true.")]
        ToolInvocationContext context,
        [McpToolProperty("bookingId", "Booking id returned by book_stay, for example b-3f2a9c1d7e0b.", isRequired: true)] string bookingId,
        [McpToolProperty("confirm", "Must be true. Set it only after the user explicitly asked to cancel this booking.", isRequired: true)] bool confirm,
        CancellationToken ct)
    {
        if (!confirm)
            return Refuse("cancel_stay", "confirm was not true. Ask the user to confirm cancelling this booking, then call again with confirm=true.");

        var r = await api.DeleteAsync($"/bookings/{Uri.EscapeDataString(bookingId)}", ct);
        return Render(r, "cancel_stay");
    }

    // Error rendering for the model door: a 403 from the API becomes prose the model can act on,
    // not a stack trace. The sentinel words (forbidden, not verified) are what telemetry queries look for.
    private static string Render(ApiResult r, string tool)
    {
        if (r.Ok) return Pretty_(r.Body);
        return r.Status switch
        {
            401 => $"{tool} forbidden: the MCP server's own identity was not accepted by the API (401). This is a deployment problem, not something to retry.",
            403 => $"{tool} forbidden: the MCP server's identity lacks the role this action needs. Tell the user this action is not available through the assistant. Detail: {Detail(r.Body)}",
            404 => $"{tool} found nothing for that id. Detail: {Detail(r.Body)}",
            422 => $"{tool} rejected the input: {Detail(r.Body)}",
            _ => $"{tool} failed with HTTP {r.Status}: {Detail(r.Body)}"
        };
    }

    private static string Refuse(string tool, string reason) => $"{tool} not executed: {reason}";

    private static string IsoDate(string value)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", inv, System.Globalization.DateTimeStyles.None, out var d)) return value;
        if (DateTimeOffset.TryParse(value, inv, System.Globalization.DateTimeStyles.AssumeUniversal, out var dto)) return dto.ToString("yyyy-MM-dd", inv);
        return value; // let the API reject it with a clear message
    }

    private static string Detail(string body)
    {
        try { return JsonDocument.Parse(body).RootElement.GetProperty("detail").GetString() ?? body; }
        catch { return body; }
    }

    private static string Pretty_(string body)
    {
        try { return JsonSerializer.Serialize(JsonDocument.Parse(body).RootElement, Pretty); }
        catch { return body; }
    }
}
