using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Rdpl.Timetable.Api.Contracts;
using Rdpl.Timetable.Api.Options;
using Rdpl.Timetable.Data;

namespace Rdpl.Timetable.Api.Services;

public enum JobState
{
    Running,
    Completed,
    Failed,
}

/// <summary>One generation run. <see cref="Error"/> is a safe, user-facing message (never a stack trace).</summary>
public sealed class TimetableJob
{
    public required string Id { get; init; }

    public required DateTimeOffset CreatedUtc { get; init; }

    public required string Profile { get; init; }

    public JobState State { get; internal set; } = JobState.Running;

    public DateTimeOffset? CompletedUtc { get; internal set; }

    public string? Outcome { get; internal set; }

    public string? Error { get; internal set; }

    public TimetableRun? Run { get; internal set; }
}

/// <summary>
/// Runs the CP-SAT pipeline in the background (a full-school solve takes minutes, longer than an HTTP request
/// should be held open). Only ONE solve runs at a time: CP-SAT already uses every configured worker thread.
/// </summary>
public sealed class TimetableJobService : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConcurrentDictionary<string, TimetableJob> _jobs = new(StringComparer.Ordinal);
    private readonly IOptionsMonitor<TimetableApiOptions> _options;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<TimetableJobService> _logger;
    private readonly CancellationToken _stopping;
    private volatile TimetableJob? _latestCompleted;
    private volatile TimetableRun? _latestDataValidation;

    public TimetableJobService(IOptionsMonitor<TimetableApiOptions> options, IWebHostEnvironment environment, ILogger<TimetableJobService> logger, IHostApplicationLifetime lifetime)
    {
        _options = options;
        _environment = environment;
        _logger = logger;
        _stopping = lifetime.ApplicationStopping;
    }

    public TimetableJob? LatestCompleted => _latestCompleted;

    public TimetableRun? LatestDataValidation => _latestDataValidation;

    public TimetableJob? Get(string id) => _jobs.TryGetValue(id, out var job) ? job : null;

    public bool IsBusy => _gate.CurrentCount == 0;

    /// <summary>Loads, normalizes and runs the pre-solve analysis only (fast; no solver).</summary>
    public TimetableRun ValidateData(RunSettings settings)
    {
        var dataset = DatasetLoader.Load(BuildPaths());
        var run = TimetableEngine.Run(dataset, settings.Rules, settings.Solver, solve: false, _stopping);
        _latestDataValidation = run;
        return run;
    }

    /// <summary>Starts a background solve, or returns null when one is already running.</summary>
    public TimetableJob? TryStart(RunSettings settings)
    {
        if (!_gate.Wait(0))
        {
            return null;
        }

        var job = new TimetableJob
        {
            Id = Guid.NewGuid().ToString("N"),
            CreatedUtc = DateTimeOffset.UtcNow,
            Profile = settings.Rules.Profile,
        };
        _jobs[job.Id] = job;
        Trim();

        _ = Task.Run(() => Execute(job, settings), CancellationToken.None);
        return job;
    }

    private void Execute(TimetableJob job, RunSettings settings)
    {
        try
        {
            _logger.LogInformation("Job {JobId}: generating with profile {Profile}, limit {Limit}s.", job.Id, settings.Rules.Profile, settings.Solver.TimeLimitSeconds);
            var dataset = DatasetLoader.Load(BuildPaths());
            var run = TimetableEngine.Run(dataset, settings.Rules, settings.Solver, solve: true, _stopping);
            job.Run = run;
            job.Outcome = run.Report.Outcome;
            job.State = JobState.Completed;
            _latestCompleted = job;
            _logger.LogInformation("Job {JobId}: {Outcome} (solver {Status}).", job.Id, run.Report.Outcome, run.Report.Solver.Status);
        }
        catch (DataInputException ex)
        {
            job.State = JobState.Failed;
            job.Error = ex.Message; // DataInputException messages are written to be user-safe.
            _logger.LogWarning("Job {JobId}: input error: {Message}", job.Id, ex.Message);
        }
        catch (OperationCanceledException)
        {
            job.State = JobState.Failed;
            job.Error = "The server is shutting down; the job was cancelled.";
        }
        catch (Exception ex)
        {
            job.State = JobState.Failed;
            job.Error = "Unexpected server error while generating the timetable. See server logs for job " + job.Id + ".";
            _logger.LogError(ex, "Job {JobId}: unexpected failure.", job.Id);
        }
        finally
        {
            job.CompletedUtc = DateTimeOffset.UtcNow;
            _gate.Release();
        }
    }

    private DatasetPaths BuildPaths()
    {
        var o = _options.CurrentValue;
        var allowedRoot = Path.GetFullPath(Path.IsPathRooted(o.AllowedRoot) ? o.AllowedRoot : Path.Combine(_environment.ContentRootPath, o.AllowedRoot));
        return new DatasetPaths
        {
            AllowedRoot = allowedRoot,
            DataRoot = o.DataRoot,
            AssignmentsWorkbook = o.AssignmentsWorkbook,
            AssignmentsSheet = o.AssignmentsSheet,
            ConfigRoot = o.ConfigRoot,
        };
    }

    private void Trim()
    {
        var max = Math.Max(1, _options.CurrentValue.MaxRetainedJobs);
        foreach (var old in _jobs.Values.Where(j => j.State != JobState.Running).OrderByDescending(j => j.CreatedUtc).Skip(max).ToList())
        {
            _jobs.TryRemove(old.Id, out _);
        }
    }

    public void Dispose() => _gate.Dispose();
}
