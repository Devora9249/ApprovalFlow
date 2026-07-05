using ApprovalFlow.Contracts;
using Dapr.Client;
using DecisionService;
using Serilog;
using Serilog.Context;

Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
    .WriteTo.File(
        "logs/decision-service-.log",
        rollingInterval: RollingInterval.Day,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level}] {CorrelationId} {Message}{NewLine}{Exception}")
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddDaprClient();

var autonomySettings = builder.Configuration.GetSection("Autonomy").Get<AutonomySettings>()
    ?? throw new InvalidOperationException("Missing required 'Autonomy' configuration section.");
builder.Services.AddSingleton(autonomySettings);
builder.Services.AddScoped<IInvoiceStateStore, DaprInvoiceStateStore>();
builder.Services.AddScoped<InvoiceProcessor>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCloudEvents();
app.MapSubscribeHandler();

app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "decision-service" }));

app.MapPost("/invoice-submitted", async (InvoiceSubmittedEvent evt, InvoiceProcessor processor, ILogger<Program> logger) =>
{
    using (LogContext.PushProperty("CorrelationId", evt.CorrelationId))
    {
        logger.LogInformation("Invoice {InvoiceId} received from invoice.submitted", evt.InvoiceId);
        await processor.ProcessAsync(evt);
    }
    return Results.Ok();
}).WithTopic("pubsub", "invoice.submitted");

app.MapGet("/invoices/{id}/status", async (string id, IInvoiceStateStore stateStore) =>
{
    var state = await stateStore.GetAsync(id);
    return state is null ? Results.NotFound() : Results.Ok(state);
});

app.Run();
