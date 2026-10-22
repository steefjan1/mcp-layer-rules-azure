using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using StayApi.Services;

namespace StayApi.Functions;

/// <summary>
/// Write side of the API. This is where authority is decided: role check, signature
/// check, idempotency and audit all live here. The MCP server upstream can label a
/// tool "destructive" and ask the user for a yes, but nothing it says changes what
/// this code accepts.
/// </summary>
public sealed class Bookings(BookingStore store, QuoteSigner signer, CallerResolver callers, AuditLog audit)
{
    public const string CreateRole = "Bookings.Create";
    public const string CancelRole = "Bookings.Cancel";

    [Function("CreateBooking")]
    public async Task<HttpResponseData> CreateBooking(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "bookings")] HttpRequestData req)
    {
        var caller = callers.Resolve(req);
        var idemKey = Http.Header(req, "Idempotency-Key");
        var body = await Http.ReadAsync<BookingRequest>(req);
        var corr = body?.SignedQuote?.Quote?.QuoteId ?? idemKey ?? "no-key";

        await audit.WriteAsync(corr, "requested", caller, "CreateBooking", $"idempotencyKey={idemKey ?? "(none)"}");

        // 1. Authority: who may do this. Enforced here, every call, regardless of the door.
        if (!caller.Has(CreateRole))
        {
            await audit.WriteAsync(corr, "denied", caller, "CreateBooking", $"missing role {CreateRole}");
            return await Http.ProblemAsync(req, HttpStatusCode.Forbidden, "Role required", $"Caller '{caller.Name}' lacks role {CreateRole}.");
        }

        if (body is null || body.SignedQuote is null || string.IsNullOrWhiteSpace(body.GuestName))
            return await Http.ProblemAsync(req, HttpStatusCode.BadRequest, "Bad request", "Body must contain signedQuote and guestName.");
        if (string.IsNullOrWhiteSpace(idemKey))
            return await Http.ProblemAsync(req, HttpStatusCode.BadRequest, "Idempotency-Key required", "Send an Idempotency-Key header so a retry cannot book twice.");

        // 2. Provenance: is the thing the agent is acting on genuine. The quote must carry
        //    our signature over exactly these fields and still be inside its window.
        if (!signer.Verify(body.SignedQuote, out var why))
        {
            await audit.WriteAsync(corr, "rejected", caller, "CreateBooking", $"quote not verified: {why}");
            return await Http.ProblemAsync(req, HttpStatusCode.UnprocessableEntity, "Quote not verified", why);
        }

        // 3. Recovery: a retry with the same key returns the booking that already exists.
        var existing = await store.FindByIdempotencyKeyAsync(idemKey);
        if (existing is not null)
        {
            await audit.WriteAsync(corr, "replayed", caller, "CreateBooking", existing.BookingId);
            var replay = await Http.JsonAsync(req, HttpStatusCode.OK, existing);
            replay.Headers.Add("Idempotent-Replayed", "true");
            return replay;
        }

        await audit.WriteAsync(corr, "authorized", caller, "CreateBooking", $"roles ok, quote ok, total={body.SignedQuote.Quote.Total}");

        var q = body.SignedQuote.Quote;
        var booking = new Booking(
            BookingId: $"b-{Guid.NewGuid():N}"[..14],
            QuoteId: q.QuoteId,
            ListingId: q.ListingId,
            CheckIn: q.CheckIn,
            CheckOut: q.CheckOut,
            GuestName: body.GuestName.Trim(),
            Total: q.Total,
            Currency: q.Currency,
            Status: "confirmed",
            CreatedBy: caller.Name,
            CreatedAt: DateTimeOffset.UtcNow.ToString("O"));

        await audit.WriteAsync(corr, "attempted", caller, "CreateBooking", booking.BookingId);

        if (!await store.TryCreateAsync(idemKey, booking))
        {
            // Lost a race on the same key. The other request owns it; return that one.
            var winner = await store.FindByIdempotencyKeyAsync(idemKey);
            await audit.WriteAsync(corr, "replayed", caller, "CreateBooking", $"race: {winner?.BookingId}");
            return await Http.JsonAsync(req, HttpStatusCode.OK, winner);
        }

        await audit.WriteAsync(corr, "completed", caller, "CreateBooking", booking.BookingId);
        return await Http.JsonAsync(req, HttpStatusCode.Created, booking);
    }

    [Function("GetBooking")]
    public async Task<HttpResponseData> GetBooking(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "bookings/{id}")] HttpRequestData req, string id)
    {
        var caller = callers.Resolve(req);
        if (!caller.Has(ListingsAndQuotes.ReadRole))
            return await Http.ProblemAsync(req, HttpStatusCode.Forbidden, "Role required", $"Caller '{caller.Name}' lacks role {ListingsAndQuotes.ReadRole}.");
        var b = await store.GetAsync(id);
        return b is null
            ? await Http.ProblemAsync(req, HttpStatusCode.NotFound, "Not found", $"No booking '{id}'.")
            : await Http.JsonAsync(req, HttpStatusCode.OK, b);
    }

    [Function("CancelBooking")]
    public async Task<HttpResponseData> CancelBooking(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "bookings/{id}")] HttpRequestData req, string id)
    {
        var caller = callers.Resolve(req);
        await audit.WriteAsync(id, "requested", caller, "CancelBooking", "");
        if (!caller.Has(CancelRole))
        {
            await audit.WriteAsync(id, "denied", caller, "CancelBooking", $"missing role {CancelRole}");
            return await Http.ProblemAsync(req, HttpStatusCode.Forbidden, "Role required", $"Caller '{caller.Name}' lacks role {CancelRole}.");
        }
        await audit.WriteAsync(id, "attempted", caller, "CancelBooking", "");
        var b = await store.CancelAsync(id, caller.Name);
        if (b is null)
            return await Http.ProblemAsync(req, HttpStatusCode.NotFound, "Not found", $"No booking '{id}'.");
        await audit.WriteAsync(id, "completed", caller, "CancelBooking", "cancelled");
        return await Http.JsonAsync(req, HttpStatusCode.OK, b);
    }
}
