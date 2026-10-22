using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StayApi.Services;

var builder = FunctionsApplication.CreateBuilder(args);

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

// The kitchen. Everything that decides something lives here, not in the MCP layer.
builder.Services.AddSingleton<ListingCatalog>();
builder.Services.AddSingleton<QuoteSigner>();
builder.Services.AddSingleton<BookingStore>();
builder.Services.AddSingleton<AuditLog>();
builder.Services.AddSingleton<CallerResolver>();

builder.Build().Run();
