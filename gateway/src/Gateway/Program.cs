using System.Text;
using System.Threading.RateLimiting;
using ApprovalFlow.Contracts;
using Dapr.Client;
using Gateway.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
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
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste just the token from POST /auth/token — Swagger adds the \"Bearer \" prefix for you."
    });

    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        { new OpenApiSecuritySchemeReference("Bearer", document), new List<string>() }
    });
});
builder.Services.AddDaprClient();

builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection("Auth"));

var jwtSecret = JwtSecretValidator.Validate(builder.Configuration["Jwt:Secret"]);
builder.Services.AddSingleton(sp => new TokenService(sp.GetRequiredService<IOptions<AuthOptions>>(), jwtSecret));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = TokenService.Issuer,
            ValidateAudience = true,
            ValidAudience = TokenService.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

// Secure by default: every endpoint requires an authenticated user unless it
// explicitly opts out with AllowAnonymous() (POST /auth/token, GET /health).
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

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
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "gateway" }))
    .AllowAnonymous()
    .WithName("GatewayHealth")
    .WithTags("Health")
    .WithSummary("Gateway liveness check")
    .Produces(StatusCodes.Status200OK);

app.MapPost("/auth/token", (LoginRequest body, TokenService tokenService, ILogger<Program> logger) =>
{
    var user = tokenService.ValidateCredentials(body.Username, body.Password);
    if (user is null)
    {
        logger.LogWarning("Login failed for {Username}", body.Username);
        return Results.Unauthorized();
    }

    logger.LogInformation("Login succeeded for {Username} with role {Role}", user.Username, user.Role);
    return Results.Ok(tokenService.IssueToken(user));
})
.AllowAnonymous()
.RequireRateLimiting("per-client")
.WithName("Login")
.WithTags("Auth")
.WithSummary("Exchange a username/password for a signed JWT")
.Accepts<LoginRequest>("application/json")
.Produces<TokenResponse>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status401Unauthorized);

app.MapPost("/invoices", async (InvoiceSubmission body, DaprClient daprClient, ILogger<Program> logger) =>
{
    logger.LogInformation("Gateway forwarding POST /invoices to {AppId}", IngestionServiceAppId);

    var invokeRequest = daprClient.CreateInvokeMethodRequest(
        HttpMethod.Post, IngestionServiceAppId, "invoices", Array.Empty<KeyValuePair<string, string>>(), body);

    var response = await daprClient.InvokeMethodWithResponseAsync(invokeRequest);
    var responseBody = await response.Content.ReadAsStringAsync();

    return Results.Content(responseBody, "application/json", statusCode: (int)response.StatusCode);
})
.RequireRateLimiting("per-client")
.RequireAuthorization(policy => policy.RequireRole("submitter", "admin"))
.WithName("SubmitInvoice")
.WithTags("Invoices")
.WithSummary("Submit a new invoice for approval")
.WithDescription("Forwards to IngestionService, which validates required fields, publishes invoice.submitted, and returns a trackingId immediately. Processing (Layers 1-3) happens asynchronously in DecisionService.")
.Accepts<InvoiceSubmission>("application/json")
.Produces(StatusCodes.Status200OK)
.Produces(StatusCodes.Status400BadRequest);

app.MapGet("/invoices/{id}/status", async (string id, DaprClient daprClient, ILogger<Program> logger) =>
{
    logger.LogInformation("Gateway forwarding GET /invoices/{InvoiceId}/status to {AppId}", id, DecisionServiceAppId);
    return await ProxyGetAsync(daprClient, DecisionServiceAppId, $"invoices/{id}/status");
})
.RequireRateLimiting("per-client")
.RequireAuthorization(policy => policy.RequireRole("submitter", "approver", "admin"))
.WithName("GetInvoiceStatus")
.WithTags("Invoices")
.WithSummary("Get the full current state of an invoice")
.Produces<InvoiceState>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound);

app.MapGet("/invoices/pending", async (DaprClient daprClient, ILogger<Program> logger) =>
{
    logger.LogInformation("Gateway forwarding GET /invoices/pending to {AppId}", DecisionServiceAppId);
    return await ProxyGetAsync(daprClient, DecisionServiceAppId, "invoices/pending");
})
.RequireRateLimiting("per-client")
.RequireAuthorization(policy => policy.RequireRole("approver", "admin"))
.WithName("GetPendingInvoices")
.WithTags("Invoices")
.WithSummary("List invoices awaiting a human decision (the approval queue)")
.Produces<List<PendingInvoiceView>>(StatusCodes.Status200OK);

app.MapPost("/invoices/{id}/decision", async (string id, HumanDecisionRequest body, DaprClient daprClient, ILogger<Program> logger) =>
{
    logger.LogInformation("Gateway forwarding POST /invoices/{InvoiceId}/decision to {AppId}", id, DecisionServiceAppId);

    var invokeRequest = daprClient.CreateInvokeMethodRequest(
        HttpMethod.Post, DecisionServiceAppId, $"invoices/{id}/decision", Array.Empty<KeyValuePair<string, string>>(), body);

    var response = await daprClient.InvokeMethodWithResponseAsync(invokeRequest);
    var responseBody = await response.Content.ReadAsStringAsync();

    return Results.Content(responseBody, "application/json", statusCode: (int)response.StatusCode);
})
.RequireRateLimiting("per-client")
.RequireAuthorization(policy => policy.RequireRole("approver", "admin"))
.WithName("SubmitHumanDecision")
.WithTags("Invoices")
.WithSummary("Approve, reject, or request more info on an invoice awaiting human review")
.WithDescription("action must be one of: approve, reject, request_more_info. Only valid when the invoice's status is waiting_for_human.")
.Accepts<HumanDecisionRequest>("application/json")
.Produces<InvoiceState>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound)
.Produces(StatusCodes.Status409Conflict)
.Produces(StatusCodes.Status400BadRequest);

app.MapGet("/dashboard/stats", async (DaprClient daprClient, ILogger<Program> logger) =>
{
    logger.LogInformation("Gateway forwarding GET /dashboard/stats to {AppId}", DecisionServiceAppId);
    return await ProxyGetAsync(daprClient, DecisionServiceAppId, "dashboard/stats");
})
.RequireRateLimiting("per-client")
.RequireAuthorization(policy => policy.RequireRole("admin", "approver"))
.WithName("GetDashboardStats")
.WithTags("Dashboard")
.WithSummary("Aggregate counts and amounts across all invoices")
.Produces<DashboardStats>(StatusCodes.Status200OK);

app.Run();

static async Task<IResult> ProxyGetAsync(DaprClient daprClient, string appId, string methodName)
{
    var invokeRequest = daprClient.CreateInvokeMethodRequest(HttpMethod.Get, appId, methodName);
    var response = await daprClient.InvokeMethodWithResponseAsync(invokeRequest);
    var responseBody = await response.Content.ReadAsStringAsync();

    return Results.Content(responseBody, "application/json", statusCode: (int)response.StatusCode);
}
