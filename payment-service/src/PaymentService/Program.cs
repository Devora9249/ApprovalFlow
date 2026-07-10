using ApprovalFlow.Contracts;
using Dapr.Client;
using PaymentService;
using Serilog;
using Serilog.Context;

Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
    .WriteTo.File(
        "logs/payment-service-.log",
        rollingInterval: RollingInterval.Day,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level}] {CorrelationId} {Message}{NewLine}{Exception}")
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddDaprClient();

builder.Services.AddScoped<IPaymentStateStore, DaprPaymentStateStore>();
builder.Services.AddScoped<IEventPublisher, DaprEventPublisher>();
builder.Services.AddScoped<PaymentProcessor>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCloudEvents();
app.MapSubscribeHandler();

app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "payment-service" }));

app.MapPost("/invoice-approved", async (InvoiceState invoice, PaymentProcessor processor, ILogger<Program> logger) =>
{
    using (LogContext.PushProperty("CorrelationId", invoice.CorrelationId))
    {
        logger.LogInformation("Invoice {InvoiceId} received from invoice.approved", invoice.InvoiceId);
        await processor.ProcessAsync(invoice);
    }
    return Results.Ok();
}).WithTopic("pubsub", "invoice.approved");

app.Run();
