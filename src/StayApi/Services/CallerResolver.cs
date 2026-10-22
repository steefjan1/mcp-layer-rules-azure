using System.Text;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker.Http;

namespace StayApi.Services;

public sealed record Caller(string Name, IReadOnlySet<string> Roles, string Source)
{
    public bool Has(string role) => Roles.Contains(role);
    public static Caller Anonymous => new("anonymous", new HashSet<string>(), "none");
}

/// <summary>
/// Reads who is calling. In Azure the Functions built-in authentication (Entra ID)
/// validates the bearer token and hands the claims to the app in X-MS-CLIENT-PRINCIPAL.
/// The app never sees the token itself and never validates signatures; the platform did.
///
/// AUTH_MODE=local replaces that with an X-Local-Roles header so `func start` works
/// without an identity. Never ship that mode.
/// </summary>
public sealed class CallerResolver
{
    private readonly bool _local = string.Equals(Environment.GetEnvironmentVariable("AUTH_MODE"), "local", StringComparison.OrdinalIgnoreCase);

    public Caller Resolve(HttpRequestData req)
    {
        if (_local)
        {
            var roles = req.Headers.TryGetValues("X-Local-Roles", out var v)
                ? v.SelectMany(s => s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                : [];
            return new Caller("local-dev", roles.ToHashSet(StringComparer.Ordinal), "X-Local-Roles");
        }

        if (!req.Headers.TryGetValues("X-MS-CLIENT-PRINCIPAL", out var values))
            return Caller.Anonymous;

        var json = Encoding.UTF8.GetString(Convert.FromBase64String(values.First()));
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var name = "unknown";
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (root.TryGetProperty("claims", out var claims))
        {
            foreach (var c in claims.EnumerateArray())
            {
                var typ = c.GetProperty("typ").GetString() ?? "";
                var val = c.GetProperty("val").GetString() ?? "";
                if (typ == "roles") set.Add(val);
                // A managed identity token carries the service principal's object id in oid.
                if (typ is "appid" or "azp" or "http://schemas.microsoft.com/identity/claims/objectidentifier") name = val;
                if (typ is "name" or "preferred_username") name = val;
            }
        }
        return new Caller(name, set, "X-MS-CLIENT-PRINCIPAL");
    }
}
