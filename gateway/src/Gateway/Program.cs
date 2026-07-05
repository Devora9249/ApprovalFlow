using System.Text.Json;
using System.Threading.RateLimiting;
using ApprovalFlow.Contracts;
using Dapr.Client;
using Microsoft.AspNetCore.RateLimiting;
using Serilog;
using Serilog.Context;

const string IngestionServiceAppId = "ingestion-service";

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

app.Run();
