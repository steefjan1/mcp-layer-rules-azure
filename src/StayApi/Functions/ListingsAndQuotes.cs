using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using StayApi.Services;

namespace StayApi.Functions;

/// <summary>
/// Read side of the API. Reads need the Stays.Read role; the key endpoint is public
/// because anyone who wants to verify a quote needs the public key.
/// </summary>
public sealed class ListingsAndQuotes(ListingCatalog catalog, QuoteSigner signer, CallerResolver callers, AuditLog audit)
{
    public const string ReadRole = "Stays.Read";

    [Function("GetListings")]
    public async Task<HttpResponseData> GetListings(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "listings")] HttpRequestData req)
    {
        var caller = callers.Resolve(req);
        if (!caller.Has(ReadRole))
            return await Http.ProblemAsync(req, HttpStatusCode.Forbidden, "Role required", $"Caller '{caller.Name}' lacks role {ReadRole}.");

        var q = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var city = q["city"];
        var guests = int.TryParse(q["guests"], out var g) ? g : 1;

        return await Http.JsonAsync(req, HttpStatusCode.OK, catalog.Search(city, guests));
    }

    [Function("CreateQuote")]
    public async Task<HttpResponseData> CreateQuote(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "quotes")] HttpRequestData req)
    {
        var caller = callers.Resolve(req);
        if (!caller.Has(ReadRole))
            return await Http.ProblemAsync(req, HttpStatusCode.Forbidden, "Role required", $"Caller '{caller.Name}' lacks role {ReadRole}.");

        var body = await Http.ReadAsync<QuoteRequest>(req);
        if (body is null) return await Http.ProblemAsync(req, HttpStatusCode.BadRequest, "Bad request", "Body must be a QuoteRequest.");

        var listing = catalog.Get(body.ListingId);
        if (listing is null) return await Http.ProblemAsync(req, HttpStatusCode.NotFound, "Unknown listing", $"No listing '{body.ListingId}'.");
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        if (!DateOnly.TryParseExact(body.CheckIn ?? "", "yyyy-MM-dd", inv, System.Globalization.DateTimeStyles.None, out var ci)
            || !DateOnly.TryParseExact(body.CheckOut ?? "", "yyyy-MM-dd", inv, System.Globalization.DateTimeStyles.None, out var co)
            || co <= ci)
            return await Http.ProblemAsync(req, HttpStatusCode.BadRequest, "Bad dates",
                $"checkIn and checkOut must be yyyy-MM-dd and checkOut must follow checkIn (received checkIn='{body.CheckIn}', checkOut='{body.CheckOut}', listingId='{body.ListingId}').");
        if (body.Guests < 1 || body.Guests > listing.MaxGuests)
            return await Http.ProblemAsync(req, HttpStatusCode.UnprocessableEntity, "Too many guests", $"Listing takes at most {listing.MaxGuests} guests.");

        var nights = co.DayNumber - ci.DayNumber;
        var quote = new Quote(
            QuoteId: $"q-{Guid.NewGuid():N}"[..14],
            ListingId: listing.Id,
            CheckIn: ci.ToString("yyyy-MM-dd"),
            CheckOut: co.ToString("yyyy-MM-dd"),
            Guests: body.Guests,
            Nights: nights,
            Total: nights * listing.NightlyRate,
            Currency: listing.Currency,
            ExpiresAt: DateTimeOffset.UtcNow.Add(signer.Ttl).ToString("O"));

        var signed = signer.Sign(quote);
        await audit.WriteAsync(quote.QuoteId, "issued", caller, "CreateQuote", $"{quote.Total} {quote.Currency} for {listing.Id} {quote.CheckIn}..{quote.CheckOut}");
        return await Http.JsonAsync(req, HttpStatusCode.Created, signed);
    }

    [Function("GetSigningKeys")]
    public Task<HttpResponseData> GetSigningKeys(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = ".well-known/stay-keys")] HttpRequestData req)
        => Http.JsonAsync(req, HttpStatusCode.OK, signer.PublicJwk());
}
