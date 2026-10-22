using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace StayMcp.Services;

public sealed record ApiResult(int Status, string Body, bool Replayed)
{
    public bool Ok => Status is >= 200 and < 300;
}

/// <summary>Thin HTTP client for the Stay API. Attaches the server's own token and nothing else.</summary>
public sealed class StayApiClient
{
    private readonly HttpClient _http;
    private readonly ApiTokenProvider _tokens;
    private readonly string _base;

    public StayApiClient(HttpClient http, ApiTokenProvider tokens)
    {
        _http = http;
        _tokens = tokens;
        _base = (Environment.GetEnvironmentVariable("STAY_API_BASE_URL") ?? "http://localhost:7071/api").TrimEnd('/');
        _http.Timeout = TimeSpan.FromSeconds(20);
    }

    public Task<ApiResult> GetAsync(string path, CancellationToken ct)
        => SendAsync(HttpMethod.Get, path, null, null, ct);

    public Task<ApiResult> PostAsync(string path, object body, string? idempotencyKey, CancellationToken ct)
        => SendAsync(HttpMethod.Post, path, body, idempotencyKey, ct);

    public Task<ApiResult> DeleteAsync(string path, CancellationToken ct)
        => SendAsync(HttpMethod.Delete, path, null, null, ct);

    public async Task<JsonElement?> GetJwksAsync(CancellationToken ct)
    {
        var r = await GetAsync("/.well-known/stay-keys", ct);
        return r.Ok ? JsonDocument.Parse(r.Body).RootElement : null;
    }

    private async Task<ApiResult> SendAsync(HttpMethod method, string path, object? body, string? idempotencyKey, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(method, _base + path);

        if (_tokens.LocalMode)
            req.Headers.Add("X-Local-Roles", _tokens.LocalRoles);
        else
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await _tokens.GetTokenAsync(ct));

        if (idempotencyKey is not null) req.Headers.Add("Idempotency-Key", idempotencyKey);
        if (body is not null) req.Content = JsonContent.Create(body);

        using var res = await _http.SendAsync(req, ct);
        var text = await res.Content.ReadAsStringAsync(ct);
        var replayed = res.Headers.TryGetValues("Idempotent-Replayed", out var v) && v.Contains("true");
        return new ApiResult((int)res.StatusCode, text, replayed);
    }
}
