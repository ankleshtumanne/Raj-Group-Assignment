namespace Rdpl.Timetable.Api.Options;

/// <summary>
/// Where the API reads its inputs. Non-secret; lives in appsettings.json and can be overridden with
/// environment variables (e.g. <c>Timetable__AssignmentsWorkbook=data/client/TEACHER_CLASS_ALLOCATION.xlsx</c>).
/// Clients can NEVER supply paths — every path comes from server configuration and must stay inside <see cref="AllowedRoot"/>.
/// </summary>
public sealed class TimetableApiOptions
{
    public const string SectionName = "Timetable";

    /// <summary>Folder that every read must stay inside. Relative values are resolved against the content root.</summary>
    public string AllowedRoot { get; set; } = "../..";

    public string DataRoot { get; set; } = "data/client";

    /// <summary>Section-level assignment workbook. Points at the clearly-labelled DUMMY workbook until the client file arrives.</summary>
    public string? AssignmentsWorkbook { get; set; } = "data/dummy/TEACHER_CLASS_ALLOCATION.DUMMY.xlsx";

    public string AssignmentsSheet { get; set; } = "Section Assignments";

    public string ConfigRoot { get; set; } = "config";

    public int DefaultTimeLimitSeconds { get; set; } = 120;

    /// <summary>Upper bound a caller may request (protects the server from unbounded CPU use).</summary>
    public int MaxTimeLimitSeconds { get; set; } = 600;

    public int DefaultWorkers { get; set; } = 8;

    public int MaxWorkers { get; set; } = 16;

    public int RandomSeed { get; set; } = 42;

    /// <summary>How many finished jobs are kept in memory.</summary>
    public int MaxRetainedJobs { get; set; } = 10;
}

/// <summary>
/// Security settings. <see cref="ApiKey"/> is a SECRET: never put it in appsettings.json.
/// Supply it through an environment variable (<c>Api__ApiKey</c>), user-secrets in Development
/// (<c>dotnet user-secrets set "Api:ApiKey" "…"</c>) or Azure Key Vault (secret name <c>Api--ApiKey</c>).
/// </summary>
public sealed class ApiSecurityOptions
{
    public const string SectionName = "Api";

    public const int MinimumKeyLength = 32;

    public string? ApiKey { get; set; }

    /// <summary>Requests per minute per client IP (fixed window).</summary>
    public int PermitPerMinute { get; set; } = 60;

    /// <summary>Maximum request body size in bytes (requests carry only small JSON option objects).</summary>
    public long MaxRequestBodyBytes { get; set; } = 16 * 1024;
}
