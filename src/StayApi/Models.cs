using System.Text.Json.Serialization;

namespace StayApi;

public record Listing(
    string Id,
    string City,
    string Name,
    int MaxGuests,
    decimal NightlyRate,
    string Currency);

/// <summary>
/// A quote is the thing the agent acts on. The API signs it, so the booking
/// endpoint can prove the price and dates it sees are the ones it issued.
/// </summary>
public record Quote(
    [property: JsonPropertyName("quoteId")] string QuoteId,
    [property: JsonPropertyName("listingId")] string ListingId,
    [property: JsonPropertyName("checkIn")] string CheckIn,
    [property: JsonPropertyName("checkOut")] string CheckOut,
    [property: JsonPropertyName("guests")] int Guests,
    [property: JsonPropertyName("nights")] int Nights,
    [property: JsonPropertyName("total")] decimal Total,
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("expiresAt")] string ExpiresAt);

public record SignedQuote(
    [property: JsonPropertyName("quote")] Quote Quote,
    [property: JsonPropertyName("keyId")] string KeyId,
    [property: JsonPropertyName("signature")] string Signature);

public record QuoteRequest(
    [property: JsonPropertyName("listingId")] string ListingId,
    [property: JsonPropertyName("checkIn")] string CheckIn,
    [property: JsonPropertyName("checkOut")] string CheckOut,
    [property: JsonPropertyName("guests")] int Guests);

public record BookingRequest(
    [property: JsonPropertyName("signedQuote")] SignedQuote SignedQuote,
    [property: JsonPropertyName("guestName")] string GuestName);

public record Booking(
    [property: JsonPropertyName("bookingId")] string BookingId,
    [property: JsonPropertyName("quoteId")] string QuoteId,
    [property: JsonPropertyName("listingId")] string ListingId,
    [property: JsonPropertyName("checkIn")] string CheckIn,
    [property: JsonPropertyName("checkOut")] string CheckOut,
    [property: JsonPropertyName("guestName")] string GuestName,
    [property: JsonPropertyName("total")] decimal Total,
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("createdBy")] string CreatedBy,
    [property: JsonPropertyName("createdAt")] string CreatedAt);

public record ProblemDetails(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("detail")] string Detail,
    [property: JsonPropertyName("status")] int Status);
