using ApprovalFlow.Contracts;
using Dapr.Client;
using Serilog;
using Serilog.Context;

Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddDaprClient();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "ingestion-service" }));

app.MapPost("/invoices", async (InvoiceSubmission submission, DaprClient daprClient, ILogger<Program> logger) =>
{
    var missingFields = GetMissingFields(submission);
    if (missingFields.Count > 0)
    {
        logger.LogWarning("Rejected invoice submission from {Submitter}: missing {MissingFields}",
            submission.Submitter, missingFields);
        return Results.BadRequest(new { error = "Missing required fields", fields = missingFields });
    }

    var invoiceId = Guid.NewGuid().ToString();

    using (LogContext.PushProperty("CorrelationId", invoiceId))
    {
        var submittedEvent = new InvoiceSubmittedEvent(invoiceId, invoiceId, DateTime.UtcNow, submission);
        await daprClient.PublishEventAsync("pubsub", "invoice.submitted", submittedEvent);

        logger.LogInformation("Invoice {InvoiceId} received from {Submitter} and published to invoice.submitted",
            invoiceId, submission.Submitter);
    }

    return Results.Ok(new { trackingId = invoiceId, status = "received" });
});

app.Run();

static List<string> GetMissingFields(InvoiceSubmission submission)
{
    var missing = new List<string>();
    if (string.IsNullOrWhiteSpace(submission.Submitter)) missing.Add(nameof(submission.Submitter));
    if (string.IsNullOrWhiteSpace(submission.Vendor)) missing.Add(nameof(submission.Vendor));
    if (string.IsNullOrWhiteSpace(submission.InvoiceNumber)) missing.Add(nameof(submission.InvoiceNumber));
    if (string.IsNullOrWhiteSpace(submission.Category)) missing.Add(nameof(submission.Category));
    if (submission.Total <= 0) missing.Add(nameof(submission.Total));
    if (submission.LineItems is null || submission.LineItems.Count == 0) missing.Add(nameof(submission.LineItems));
    return missing;
}
