using Azure;
using Azure.Data.Tables;

namespace StayApi.Services;

/// <summary>
/// Bookings in Table storage, with an idempotency table keyed by the caller's
/// Idempotency-Key. A retry after a timeout returns the booking that already
/// exists instead of a second one. That is the "defined recovery path" from the
/// comment thread, implemented in the kitchen where it belongs.
/// </summary>
public sealed class BookingStore
{
    private readonly TableClient _bookings;
    private readonly TableClient _idem;

    public BookingStore()
    {
        var conn = Environment.GetEnvironmentVariable("STORE_CONNECTION");
        var svc = !string.IsNullOrWhiteSpace(conn)
            ? new TableServiceClient(conn)
            : new TableServiceClient(new Uri(Environment.GetEnvironmentVariable("STORE_TABLE_URI")!), new Azure.Identity.DefaultAzureCredential());
        _bookings = svc.GetTableClient("bookings");
        _idem = svc.GetTableClient("idempotency");
        _bookings.CreateIfNotExists();
        _idem.CreateIfNotExists();
    }

    public async Task<Booking?> FindByIdempotencyKeyAsync(string key)
    {
        try
        {
            var row = await _idem.GetEntityAsync<TableEntity>("idem", Safe(key));
            return await GetAsync(row.Value.GetString("BookingId"));
        }
        catch (RequestFailedException ex) when (ex.Status == 404) { return null; }
    }

    public async Task<Booking?> GetAsync(string bookingId)
    {
        try
        {
            var e = (await _bookings.GetEntityAsync<TableEntity>("booking", bookingId)).Value;
            return ToBooking(e);
        }
        catch (RequestFailedException ex) when (ex.Status == 404) { return null; }
    }

    /// <summary>Returns false when the idempotency key was claimed by a concurrent request.</summary>
    public async Task<bool> TryCreateAsync(string idempotencyKey, Booking b)
    {
        // Claim the key first. Insert fails with 409 if another request got there first.
        try
        {
            await _idem.AddEntityAsync(new TableEntity("idem", Safe(idempotencyKey)) { ["BookingId"] = b.BookingId });
        }
        catch (RequestFailedException ex) when (ex.Status == 409) { return false; }

        var e = new TableEntity("booking", b.BookingId)
        {
            ["QuoteId"] = b.QuoteId,
            ["ListingId"] = b.ListingId,
            ["CheckIn"] = b.CheckIn,
            ["CheckOut"] = b.CheckOut,
            ["GuestName"] = b.GuestName,
            ["Total"] = (double)b.Total,
            ["Currency"] = b.Currency,
            ["Status"] = b.Status,
            ["CreatedBy"] = b.CreatedBy,
            ["CreatedAt"] = b.CreatedAt
        };
        await _bookings.AddEntityAsync(e);
        return true;
    }

    public async Task<Booking?> CancelAsync(string bookingId, string by)
    {
        var existing = await GetAsync(bookingId);
        if (existing is null) return null;
        var e = new TableEntity("booking", bookingId) { ["Status"] = "cancelled", ["CancelledBy"] = by, ["CancelledAt"] = DateTimeOffset.UtcNow.ToString("O") };
        await _bookings.UpdateEntityAsync(e, ETag.All, TableUpdateMode.Merge);
        return existing with { Status = "cancelled" };
    }

    private static Booking ToBooking(TableEntity e) => new(
        e.RowKey,
        e.GetString("QuoteId"),
        e.GetString("ListingId"),
        e.GetString("CheckIn"),
        e.GetString("CheckOut"),
        e.GetString("GuestName"),
        (decimal)(e.GetDouble("Total") ?? 0),
        e.GetString("Currency"),
        e.GetString("Status"),
        e.GetString("CreatedBy"),
        e.GetString("CreatedAt"));

    // Table keys forbid / \ # ? and control chars.
    private static string Safe(string key) => string.Concat(key.Select(c => "/\\#?".Contains(c) || c < ' ' ? '_' : c));
}
