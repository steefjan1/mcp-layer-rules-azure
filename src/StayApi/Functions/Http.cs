using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker.Http;

namespace StayApi.Functions;

internal static class Http
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static async Task<HttpResponseData> JsonAsync<T>(HttpRequestData req, HttpStatusCode status, T body)
    {
        var res = req.CreateResponse(status);
        res.Headers.Add("Content-Type", "application/json; charset=utf-8");
        await res.WriteStringAsync(JsonSerializer.Serialize(body, Json));
        return res;
    }

    public static Task<HttpResponseData> ProblemAsync(HttpRequestData req, HttpStatusCode status, string title, string detail)
        => JsonAsync(req, status, new ProblemDetails($"https://httpstatuses.io/{(int)status}", title, detail, (int)status));

    public static async Task<T?> ReadAsync<T>(HttpRequestData req)
    {
        try { return await JsonSerializer.DeserializeAsync<T>(req.Body, Json); }
        catch (JsonException) { return default; }
    }

    public static string? Header(HttpRequestData req, string name)
        => req.Headers.TryGetValues(name, out var v) ? v.FirstOrDefault() : null;
}
