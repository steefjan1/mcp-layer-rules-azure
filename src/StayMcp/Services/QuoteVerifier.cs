using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace StayMcp.Services;

/// <summary>
/// The agent-side provenance check from the comment thread: before the MCP server lets
/// a quote go to book_stay it verifies the API's signature with the key the API
/// publishes. Fails closed. The API verifies again on its side; this check exists so a
/// quote that was altered between quote_stay and book_stay (by the model, by a prompt
/// injection, by a buggy client) is refused before the write call is even made.
/// </summary>
public sealed class QuoteVerifier(StayApiClient api)
{
    private readonly Dictionary<string, ECDsa> _keys = new();
    private DateTimeOffset _fetchedAt = DateTimeOffset.MinValue;
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5); // a revoked key stops being accepted within five minutes

    private static readonly JsonSerializerOptions Canonical = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public async Task<(bool ok, string reason)> VerifyAsync(JsonElement signedQuote, CancellationToken ct)
    {
        if (!signedQuote.TryGetProperty("quote", out var quote) ||
            !signedQuote.TryGetProperty("keyId", out var keyIdEl) ||
            !signedQuote.TryGetProperty("signature", out var sigEl))
            return (false, "signedQuote must contain quote, keyId and signature");

        var keyId = keyIdEl.GetString() ?? "";
        var key = await GetKeyAsync(keyId, ct);
        if (key is null) return (false, $"no published key with kid '{keyId}'");

        if (quote.TryGetProperty("expiresAt", out var exp) &&
            DateTimeOffset.TryParse(exp.GetString(), out var expAt) && expAt < DateTimeOffset.UtcNow)
            return (false, $"quote expired at {expAt:O}; call quote_stay again");

        // Re-serialize the quote exactly like the API does: fixed property order, no whitespace.
        var ordered = new
        {
            quoteId = quote.GetProperty("quoteId").GetString(),
            listingId = quote.GetProperty("listingId").GetString(),
            checkIn = quote.GetProperty("checkIn").GetString(),
            checkOut = quote.GetProperty("checkOut").GetString(),
            guests = quote.GetProperty("guests").GetInt32(),
            nights = quote.GetProperty("nights").GetInt32(),
            total = quote.GetProperty("total").GetDecimal().ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
            currency = quote.GetProperty("currency").GetString(),
            expiresAt = quote.GetProperty("expiresAt").GetString()
        };
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(ordered, Canonical));

        byte[] sig;
        try { sig = Convert.FromBase64String(sigEl.GetString() ?? ""); }
        catch { return (false, "signature is not base64"); }

        return key.VerifyData(bytes, sig, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)
            ? (true, "ok")
            : (false, "signature does not match the quote; the quote was changed after it was issued");
    }

    private async Task<ECDsa?> GetKeyAsync(string keyId, CancellationToken ct)
    {
        if (_keys.TryGetValue(keyId, out var cached) && DateTimeOffset.UtcNow - _fetchedAt < Ttl) return cached;

        var jwks = await api.GetJwksAsync(ct);
        if (jwks is null) return null;

        _keys.Clear();
        foreach (var jwk in jwks.Value.GetProperty("keys").EnumerateArray())
        {
            var p = new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = FromB64Url(jwk.GetProperty("x").GetString()!), Y = FromB64Url(jwk.GetProperty("y").GetString()!) }
            };
            var ec = ECDsa.Create(p);
            _keys[jwk.GetProperty("kid").GetString()!] = ec;
        }
        _fetchedAt = DateTimeOffset.UtcNow;
        return _keys.GetValueOrDefault(keyId);
    }

    private static byte[] FromB64Url(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }
}
