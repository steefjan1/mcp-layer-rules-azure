using Azure.Core;
using Azure.Identity;

namespace StayMcp.Services;

/// <summary>
/// Rule 1 in code. The MCP server calls the API with its OWN token, acquired by its
/// managed identity for the API's app registration. It never sees, stores or forwards
/// a user token. (The MCP tool trigger does not even hand the function a caller
/// identity, so there is nothing to forward by accident.)
///
/// The token is cached by the credential; GetToken is cheap after the first call.
/// </summary>
public sealed class ApiTokenProvider
{
    private readonly TokenCredential? _credential;
    private readonly string? _scope;
    public bool LocalMode { get; }
    public string LocalRoles { get; }

    public ApiTokenProvider()
    {
        LocalMode = string.Equals(Environment.GetEnvironmentVariable("AUTH_MODE"), "local", StringComparison.OrdinalIgnoreCase);
        LocalRoles = Environment.GetEnvironmentVariable("LOCAL_ROLES") ?? "";
        if (LocalMode) return;

        var appIdUri = Environment.GetEnvironmentVariable("STAY_API_APP_ID_URI")
            ?? throw new InvalidOperationException("STAY_API_APP_ID_URI is required when AUTH_MODE is not local.");
        _scope = appIdUri.TrimEnd('/') + "/.default";

        // User-assigned identity in Azure (AZURE_CLIENT_ID set by bicep); developer sign-in locally.
        var clientId = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID");
        _credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions { ManagedIdentityClientId = clientId });
    }

    public async Task<string> GetTokenAsync(CancellationToken ct)
    {
        var token = await _credential!.GetTokenAsync(new TokenRequestContext([_scope!]), ct);
        return token.Token;
    }
}
