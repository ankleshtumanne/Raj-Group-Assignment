using System.Text.RegularExpressions;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.Options;
using Rdpl.Timetable.Api.Contracts;
using Rdpl.Timetable.Api.Options;
using Rdpl.Timetable.Api.Security;
using Rdpl.Timetable.Api.Services;
using Rdpl.Timetable.Data;
using Rdpl.Timetable.Reporting;
#if ENABLE_KEY_VAULT
using Azure.Identity;
#endif

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------- configuration & secrets
// Order (later wins): appsettings.json → appsettings.{Env}.json → user-secrets (Development only) → environment variables
// → command line → Azure Key Vault (when compiled with -p:EnableKeyVault=true and KeyVault:Uri is set).
#if ENABLE_KEY_VAULT
if (builder.Configuration["KeyVault:Uri"] is { Length: > 0 } vaultUri)
{
    // Managed identity in Azure, developer credentials locally. Secret "Api--ApiKey" maps to configuration key "Api:ApiKey".
    builder.Configuration.AddAzureKeyVault(new Uri(vaultUri), new DefaultAzureCredential());
}
#endif

builder.Services.Configure<TimetableApiOptions>(builder.Configuration.GetSection(TimetableApiOptions.SectionName));
builder.Services.Configure<ApiSecurityOptions>(builder.Configuration.GetSection(ApiSecurityOptions.SectionName));

var security = builder.Configuration.GetSection(ApiSecurityOptions.SectionName).Get<ApiSecurityOptions>() ?? new ApiSecurityOptions();
if (string.IsNullOrWhiteSpace(security.ApiKey) || security.ApiKey.Length < ApiSecurityOptions.MinimumKeyLength)
{
    // Fail closed: never start an unauthenticated API.
    throw new InvalidOperationException(
        $"Api:ApiKey is not configured (minimum {ApiSecurityOptions.MinimumKeyLength} characters). Set the environment variable Api__ApiKey, " +
        "use 'dotnet user-secrets set \"Api:ApiKey\" \"<key>\"' in Development, or store secret 'Api--ApiKey' in Azure Key Vault.");
}

// ---------------------------------------------------------------- hardening
builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.AddServerHeader = false;
    kestrel.Limits.MaxRequestBodySize = security.MaxRequestBodyBytes;
    kestrel.Limits.MaxRequestHeadersTotalSize = 16 * 1024;
});

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<SafeExceptionHandler>();

builder.Services.AddAuthentication(ApiKeyAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationHandler.SchemeName, _ => { });
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = Math.Max(1, security.PermitPerMinute), Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

builder.Services.AddSingleton<TimetableJobService>();
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = TimetableReport.JsonOptions.PropertyNamingPolicy;
    o.SerializerOptions.DefaultIgnoreCondition = TimetableReport.JsonOptions.DefaultIgnoreCondition;
    o.SerializerOptions.MaxDepth = 32;
    foreach (var converter in TimetableReport.JsonOptions.Converters)
    {
        o.SerializerOptions.Converters.Add(converter);
    }
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
var app = builder.Build();

app.UseExceptionHandler();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseHttpsRedirection();
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers.XContentTypeOptions = "nosniff";
    headers.XFrameOptions = "DENY";
    headers["Referrer-Policy"] = "no-referrer";
    headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
    headers.CacheControl = "no-store";
    await next();
});
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// ---------------------------------------------------------------- endpoints
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

var api = app.MapGroup("/api/timetable");

api.MapPost("/load-data", (GenerateRequest? request, TimetableJobService service, IOptionsMonitor<TimetableApiOptions> options) =>
{
    var (settings, errors) = GenerateRequestValidator.Validate(request, options.CurrentValue);
    if (settings is null)
    {
        return Results.ValidationProblem(errors);
    }

    var run = service.ValidateData(settings);
    var r = run.Report;
    return Results.Ok(new
    {
        r.Outcome,
        r.DataSource,
        r.RulesApplied,
        r.Summary,
        r.DataConflicts,
        r.SchedulingConflicts,
        r.Assumptions,
    });
});

api.MapPost("/generate", (GenerateRequest? request, TimetableJobService service, IOptionsMonitor<TimetableApiOptions> options) =>
{
    var (settings, errors) = GenerateRequestValidator.Validate(request, options.CurrentValue);
    if (settings is null)
    {
        return Results.ValidationProblem(errors);
    }

    var job = service.TryStart(settings);
    return job is null
        ? Results.Problem("A timetable generation is already running. Poll its job or retry later.", statusCode: StatusCodes.Status409Conflict)
        : Results.Accepted($"/api/timetable/jobs/{job.Id}", JobView(job));
});

api.MapGet("/jobs/{id}", (string id, TimetableJobService service) =>
{
    if (!IsSafeId(id))
    {
        return Results.Problem("Invalid job id.", statusCode: StatusCodes.Status400BadRequest);
    }

    var job = service.Get(id);
    return job is null ? Results.Problem("Job not found.", statusCode: StatusCodes.Status404NotFound) : Results.Ok(JobView(job));
});

// Full report of the latest finished generation (same JSON as the console writes).
api.MapGet("/result", (TimetableJobService service) =>
    service.LatestCompleted?.Run?.Report is { } report ? Results.Json(report, TimetableReport.JsonOptions) : NoResult());

api.MapGet("/sections", (TimetableJobService service) =>
    service.LatestCompleted?.Run?.Report is { } report
        ? Results.Ok(new { report.Success, report.Outcome, dataSource = report.DataSource.Kind, sectionTimetables = report.SectionTimetables })
        : NoResult());

api.MapGet("/sections/{id}", (string id, TimetableJobService service) =>
{
    if (!IsSafeId(id))
    {
        return Results.Problem("Invalid section id.", statusCode: StatusCodes.Status400BadRequest);
    }

    var run = service.LatestCompleted?.Run;
    if (run?.Report is not { } report)
    {
        return NoResult();
    }

    // Accept the sections.json id ("class-10-lily") or the display name ("Grade 10 - Lily").
    var section = run.Normalization.Problem.Sections.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase))
                  ?? run.Normalization.Problem.Sections.FirstOrDefault(s => string.Equals(s.DisplayName, id, StringComparison.OrdinalIgnoreCase));
    if (section is null)
    {
        var excluded = run.Normalization.ExcludedSections.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
        return excluded is null
            ? Results.Problem("Section not found.", statusCode: StatusCodes.Status404NotFound)
            : Results.Problem($"{excluded.DisplayName} is excluded from scheduling; see dataConflicts.", statusCode: StatusCodes.Status404NotFound);
    }

    return report.SectionTimetables is not null && report.SectionTimetables.TryGetValue(section.DisplayName, out var week)
        ? Results.Ok(new { section = section.DisplayName, section.Id, report.Outcome, timetable = week })
        : Results.Problem($"No valid timetable was generated (outcome {report.Outcome}).", statusCode: StatusCodes.Status404NotFound);
});

api.MapGet("/teachers", (TimetableJobService service) =>
    service.LatestCompleted?.Run?.Report is { } report
        ? Results.Ok(new { report.Success, report.Outcome, dataSource = report.DataSource.Kind, teacherTimetables = report.TeacherTimetables })
        : NoResult());

api.MapGet("/conflicts", (TimetableJobService service) =>
{
    var report = service.LatestCompleted?.Run?.Report ?? service.LatestDataValidation?.Report;
    return report is null
        ? Results.Problem("Nothing has been loaded yet. Call POST /api/timetable/load-data or /generate first.", statusCode: StatusCodes.Status404NotFound)
        : Results.Ok(new { report.Outcome, report.Summary.DataConflictCounts, report.DataConflicts, report.SchedulingConflicts, diagnostic = report.Diagnostic?.RelaxedHardRuleViolations });
});

app.Run();

static IResult NoResult() =>
    Results.Problem("No timetable has been generated yet. Call POST /api/timetable/generate and poll the job.", statusCode: StatusCodes.Status404NotFound);

static bool IsSafeId(string id) => id.Length is > 0 and <= 64 && Regex.IsMatch(id, "^[A-Za-z0-9][A-Za-z0-9 _-]*$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));

static object JobView(TimetableJob job) => new
{
    job.Id,
    State = job.State.ToString().ToUpperInvariant(),
    job.Profile,
    job.CreatedUtc,
    job.CompletedUtc,
    job.Outcome,
    job.Error,
    Solver = job.Run?.Report.Solver,
    Summary = job.Run?.Report.Summary,
    Links = new { result = "/api/timetable/result", sections = "/api/timetable/sections", teachers = "/api/timetable/teachers", conflicts = "/api/timetable/conflicts" },
};

/// <summary>Maps input errors to 422 and hides every other exception behind a generic 500 (no stack traces to clients).</summary>
internal sealed class SafeExceptionHandler : IExceptionHandler
{
    private readonly ILogger<SafeExceptionHandler> _logger;
    private readonly IProblemDetailsService _problemDetails;

    public SafeExceptionHandler(ILogger<SafeExceptionHandler> logger, IProblemDetailsService problemDetails)
    {
        _logger = logger;
        _problemDetails = problemDetails;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title, detail) = exception switch
        {
            DataInputException e => (StatusCodes.Status422UnprocessableEntity, "Input data error", e.Message),
            BadHttpRequestException => (StatusCodes.Status400BadRequest, "Bad request", "The request could not be read."),
            _ => (StatusCodes.Status500InternalServerError, "Unexpected error", "An unexpected error occurred. It has been logged."),
        };

        if (status == StatusCodes.Status500InternalServerError)
        {
            _logger.LogError(exception, "Unhandled exception for {Path}.", httpContext.Request.Path);
        }

        httpContext.Response.StatusCode = status;
        return await _problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = { Status = status, Title = title, Detail = detail },
            Exception = exception,
        });
    }
}

/// <summary>Exposed for WebApplicationFactory-based tests.</summary>
public partial class Program
{
}
