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

var llmSettings = builder.Configuration.GetSection("Llm").Get<LlmSettings>()
    ?? throw new InvalidOperationException("Missing required 'Llm' configuration section.");
builder.Services.AddSingleton(llmSettings);

if (string.Equals(llmSettings.Provider, "stub", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddScoped<ILlmProvider, StubLlmProvider>();
}
else
{
    builder.Services.AddHttpClient<GeminiProvider>();
    builder.Services.AddScoped<ILlmProvider>(sp => sp.GetRequiredService<GeminiProvider>());
}

builder.Services.AddScoped<IInvoiceStateStore, DaprInvoiceStateStore>();
builder.Services.AddScoped<IEventPublisher, DaprEventPublisher>();
builder.Services.AddScoped<InvoiceProcessor>();
builder.Services.AddScoped<HumanDecisionProcessor>();

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

app.MapGet("/invoices/pending", async (IInvoiceStateStore stateStore) =>
{
    var pendingIds = await stateStore.GetPendingIdsAsync();
    var views = new List<PendingInvoiceView>();

    foreach (var id in pendingIds)
    {
        var state = await stateStore.GetAsync(id);
        // Defensive: the index and the per-invoice record are two separate state entries,
        // so skip anything that's out of sync (already decided, or index entry with no record).
        if (state is null || state.Status != "waiting_for_human")
            continue;

        views.Add(new PendingInvoiceView(
            state.InvoiceId, state.Vendor, state.Category, state.Total,
            state.AgentRecommendation, state.AgentReasoning, state.AgentConfidence,
            state.PolicyViolations, state.EscalatedAt));
    }

    return Results.Ok(views);
});

app.MapPost("/invoices/{id}/decision", async (
    string id, HumanDecisionRequest request, HumanDecisionProcessor processor) =>
{
    using (LogContext.PushProperty("CorrelationId", id))
    {
        var result = await processor.ApplyAsync(id, request);

        return result.Outcome switch
        {
            HumanDecisionOutcome.InvoiceNotFound => Results.NotFound(),
            HumanDecisionOutcome.NotAwaitingHumanDecision => Results.Conflict(new
            {
                error = $"Invoice {id} is not awaiting a human decision (status={result.State!.Status})"
            }),
            HumanDecisionOutcome.UnknownAction => Results.BadRequest(new
            {
                error = $"Unknown action '{request.Action}'. Expected approve, reject or request_more_info."
            }),
            _ => Results.Ok(result.State)
        };
    }
});

app.Run();
