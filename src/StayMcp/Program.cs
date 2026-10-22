using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StayMcp.Services;

var builder = FunctionsApplication.CreateBuilder(args);

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

// The MCP layer owns exactly two things: how it talks to the API (its own identity)
// and how it checks what the API handed back (the quote signature). No business rules.
builder.Services.AddSingleton<ApiTokenProvider>();
builder.Services.AddHttpClient<StayApiClient>();
builder.Services.AddSingleton<QuoteVerifier>();

builder.Build().Run();
