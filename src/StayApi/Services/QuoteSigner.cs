using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace StayApi.Services;

/// <summary>
/// Signs quotes with ECDSA P-256 and publishes the public key. This is the
/// "provenance" half of the comment thread: the booking endpoint only accepts a
/// quote it issued, with the price and dates it issued, inside the window it issued.
///
/// The private key comes from QUOTE_SIGNING_KEY (base64 PKCS#8). When the setting is
/// missing the app generates a key at start-up; fine for local runs, useless across
/// instances, so the bicep writes one into app settings from Key Vault.
/// </summary>
public sealed class QuoteSigner
{
    private readonly ECDsa _key;
    public string KeyId { get; }
    public TimeSpan Ttl { get; }

    private static readonly JsonSerializerOptions Canonical = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public QuoteSigner(ILogger<QuoteSigner> logger)
    {
        KeyId = Environment.GetEnvironmentVariable("QUOTE_KEY_ID") ?? "unnamed-key";
        Ttl = TimeSpan.FromMinutes(int.TryParse(Environment.GetEnvironmentVariable("QUOTE_TTL_MINUTES"), out var m) ? m : 15);

        var pkcs8 = Environment.GetEnvironmentVariable("QUOTE_SIGNING_KEY");
        _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        if (!string.IsNullOrWhiteSpace(pkcs8))
        {
            _key.ImportPkcs8PrivateKey(Convert.FromBase64String(pkcs8), out _);
        }
        else
        {
            logger.LogWarning("QUOTE_SIGNING_KEY not set; generated an ephemeral P-256 key. Quotes will not verify across instances or restarts.");
        }
    }

    /// <summary>
    /// Canonical bytes: fixed property order, no whitespace, total rendered with two decimals
    /// so "370" and "370.0" cannot produce different signatures. StayMcp.QuoteVerifier builds
    /// the identical shape; change one and change the other.
    /// </summary>
    public static byte[] CanonicalBytes(Quote quote)
    {
        var ordered = new
        {
            quoteId = quote.QuoteId,
            listingId = quote.ListingId,
            checkIn = quote.CheckIn,
            checkOut = quote.CheckOut,
            guests = quote.Guests,
            nights = quote.Nights,
            total = quote.Total.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
            currency = quote.Currency,
            expiresAt = quote.ExpiresAt
        };
        return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(ordered, Canonical));
    }

    public SignedQuote Sign(Quote quote)
    {
        var sig = _key.SignData(CanonicalBytes(quote), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return new SignedQuote(quote, KeyId, Convert.ToBase64String(sig));
    }

    public bool Verify(SignedQuote signed, out string reason)
    {
        if (signed.KeyId != KeyId) { reason = $"unknown keyId '{signed.KeyId}'"; return false; }
        if (!DateTimeOffset.TryParse(signed.Quote.ExpiresAt, out var exp)) { reason = "expiresAt unreadable"; return false; }
        if (exp < DateTimeOffset.UtcNow) { reason = $"quote expired at {exp:O}"; return false; }

        byte[] sig;
        try { sig = Convert.FromBase64String(signed.Signature); }
        catch { reason = "signature is not base64"; return false; }

        if (!_key.VerifyData(CanonicalBytes(signed.Quote), sig, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
        {
            reason = "signature does not match quote body";
            return false;
        }
        reason = "ok";
        return true;
    }

    /// <summary>JWK for the public key, served at /api/.well-known/stay-keys.</summary>
    public object PublicJwk()
    {
        var p = _key.ExportParameters(false);
        return new
        {
            keys = new[]
            {
                new
                {
                    kty = "EC",
                    crv = "P-256",
                    kid = KeyId,
                    use = "sig",
                    alg = "ES256",
                    x = Base64Url(p.Q.X!),
                    y = Base64Url(p.Q.Y!)
                }
            }
        };
    }

    private static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
