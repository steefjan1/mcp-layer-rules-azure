using Azure.Data.Tables;
using Microsoft.Extensions.Logging;

namespace StayApi.Services;

/// <summary>
/// One row per stage: requested, authorized, denied, rejected, attempted, completed, replayed.
/// The comment asked for "what was requested, approved, attempted, and actually completed".
/// A table in the same storage account is enough to show the shape; a real system would
/// forward these to Log Analytics or an append-only store.
/// </summary>
public sealed class AuditLog
{
    private readonly TableClient _audit;
    private readonly ILogger<AuditLog> _log;

    public AuditLog(ILogger<AuditLog> log)
    {
        _log = log;
        var conn = Environment.GetEnvironmentVariable("STORE_CONNECTION");
        var svc = !string.IsNullOrWhiteSpace(conn)
            ? new TableServiceClient(conn)
            : new TableServiceClient(new Uri(Environment.GetEnvironmentVariable("STORE_TABLE_URI")!), new Azure.Identity.DefaultAzureCredential());
        _audit = svc.GetTableClient("audit");
        _audit.CreateIfNotExists();
    }

    public async Task WriteAsync(string correlationKey, string stage, Caller caller, string operation, string detail)
    {
        var now = DateTimeOffset.UtcNow;
        var e = new TableEntity(correlationKey, $"{now:yyyyMMddHHmmssfff}-{stage}")
        {
            ["Stage"] = stage,
            ["Operation"] = operation,
            ["Caller"] = caller.Name,
            ["CallerSource"] = caller.Source,
            ["Roles"] = string.Join(",", caller.Roles),
            ["Detail"] = detail,
            ["At"] = now.ToString("O")
        };
        await _audit.AddEntityAsync(e);
        _log.LogInformation("audit {Stage} {Operation} key={Key} caller={Caller} detail={Detail}", stage, operation, correlationKey, caller.Name, detail);
    }
}
