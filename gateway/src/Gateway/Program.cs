using System.Text.Json;
using System.Threading.RateLimiting;
using ApprovalFlow.Contracts;
using Dapr.Client;
using Microsoft.AspNetCore.RateLimiting;
using Serilog;
using Serilog.Context;

const string IngestionServiceAppId = "ingestion-service";
const string DecisionServiceAppId = "decision-service";
const string UiCorsPolicy = "ui";

Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
    .WriteTo.File(
        "logs/gateway-.log",
        rollingInterval: RollingInterval.Day,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level}] {CorrelationId} {Message}{NewLine}{Exception}")
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddDaprClient();

builder.Services.AddCors(options =>
{
    // Dev-only: the frontend is static HTML opened from a file:// URL or a plain static
    // server, not a known origin, so allow any origin. No cookies/credentials are used.
    options.AddPolicy(UiCorsPolicy, policy => policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Partitioned by client IP for now — swap to authenticated user id once auth exists.
    options.AddPolicy("per-client", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors(UiCorsPolicy);
app.UseRateLimiter();

app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "gateway" }));

app.MapPost("/invoices", async (InvoiceSubmission body, DaprClient daprClient, ILogger<Program> logger) =>
{
    logger.LogInformation("Gateway forwarding POST /invoices to {AppId}", IngestionServiceAppId);

    var invokeRequest = daprClient.CreateInvokeMethodRequest(
        HttpMethod.Post, IngestionServiceAppId, "invoices", Array.Empty<KeyValuePair<string, string>>(), body);

    var response = await daprClient.InvokeMethodWithResponseAsync(invokeRequest);
    var responseBody = await response.Content.ReadAsStringAsync();

    return Results.Content(responseBody, "application/json", statusCode: (int)response.StatusCode);
})
.RequireRateLimiting("per-client");

app.MapGet("/invoices/{id}/status", async (string id, DaprClient daprClient, ILogger<Program> logger) =>
{
    logger.LogInformation("Gateway forwarding GET /invoices/{InvoiceId}/status to {AppId}", id, DecisionServiceAppId);
    return await ProxyGetAsync(daprClient, DecisionServiceAppId, $"invoices/{id}/status");
})
.RequireRateLimiting("per-client");

app.MapGet("/invoices/pending", async (DaprClient daprClient, ILogger<Program> logger) =>
{
    logger.LogInformation("Gateway forwarding GET /invoices/pending to {AppId}", DecisionServiceAppId);
    return await ProxyGetAsync(daprClient, DecisionServiceAppId, "invoices/pending");
})
.RequireRateLimiting("per-client");

app.MapPost("/invoices/{id}/decision", async (string id, JsonElement body, DaprClient daprClient, ILogger<Program> logger) =>
{
    logger.LogInformation("Gateway forwarding POST /invoices/{InvoiceId}/decision to {AppId}", id, DecisionServiceAppId);

    var invokeRequest = daprClient.CreateInvokeMethodRequest(
        HttpMethod.Post, DecisionServiceAppId, $"invoices/{id}/decision", Array.Empty<KeyValuePair<string, string>>(), body);

    var response = await daprClient.InvokeMethodWithResponseAsync(invokeRequest);
    var responseBody = await response.Content.ReadAsStringAsync();

    return Results.Content(responseBody, "application/json", statusCode: (int)response.StatusCode);
})
.RequireRateLimiting("per-client");

app.MapGet("/dashboard/stats", async (DaprClient daprClient, ILogger<Program> logger) =>
{
    logger.LogInformation("Gateway forwarding GET /dashboard/stats to {AppId}", DecisionServiceAppId);
    return await ProxyGetAsync(daprClient, DecisionServiceAppId, "dashboard/stats");
})
.RequireRateLimiting("per-client");

app.Run();

static async Task<IResult> ProxyGetAsync(DaprClient daprClient, string appId, string methodName)
{
    var invokeRequest = daprClient.CreateInvokeMethodRequest(HttpMethod.Get, appId, methodName);
    var response = await daprClient.InvokeMethodWithResponseAsync(invokeRequest);
    var responseBody = await response.Content.ReadAsStringAsync();

    return Results.Content(responseBody, "application/json", statusCode: (int)response.StatusCode);
}
