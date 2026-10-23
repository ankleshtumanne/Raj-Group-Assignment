using Rdpl.Timetable.Models;

namespace Rdpl.Timetable.Data;

/// <summary>Where the inputs live. All paths must resolve inside <see cref="AllowedRoot"/>.</summary>
public sealed record DatasetPaths
{
    /// <summary>Everything the app reads or writes must be inside this folder (defence against path traversal).</summary>
    public required string AllowedRoot { get; init; }

    /// <summary>Folder with the client package (markdown files at its root, JSON in datasets/ or candidate-data/).</summary>
    public required string DataRoot { get; init; }

    /// <summary>TEACHER_CLASS_ALLOCATION.xlsx (real) or the DUMMY workbook.</summary>
    public string? AssignmentsWorkbook { get; init; }

    public string AssignmentsSheet { get; init; } = AssignmentWorkbookLoader.DefaultSheetName;

    /// <summary>Folder with subject-aliases.json, teacher-availability.json, scheduling-overrides.json.</summary>
    public required string ConfigRoot { get; init; }
}

public sealed record LoadedDataset(
    IReadOnlyList<Section> Sections,
    BellSchedule Bell,
    int? DeclaredWeeklyCapacity,
    IReadOnlyDictionary<string, ClassCurriculum> Curricula,
    ClassAssignmentMarkdownParser.Result? ClassLevel,
    IReadOnlyList<AssignmentRow> AssignmentRows,
    bool AssignmentsAreDummy,
    string? AssignmentsSource,
    IReadOnlyList<TeacherAvailability> Availability,
    IReadOnlyList<DayRestriction> DayRestrictions,
    IReadOnlyList<FixedSlotRule> FixedSlots,
    IReadOnlyList<SubjectAlias> Aliases,
    IReadOnlyDictionary<string, IReadOnlyList<string>> RuleTiers,
    IReadOnlyList<DataConflict> Conflicts,
    IReadOnlyList<string> FilesRead);

/// <summary>Loads every input file, validating each one, and records file-level problems as data conflicts.</summary>
public static class DatasetLoader
{
    private static readonly string[] JsonFolders = { "datasets", "candidate-data", "." };

    public static LoadedDataset Load(DatasetPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var root = Path.GetFullPath(paths.AllowedRoot);
        var dataRoot = SafeFileAccess.ResolveInsideRoot(root, paths.DataRoot);
        var configRoot = SafeFileAccess.ResolveInsideRoot(root, paths.ConfigRoot);
        if (!Directory.Exists(dataRoot))
        {
            throw new DataInputException($"Data folder '{paths.DataRoot}' does not exist.");
        }

        var conflicts = new List<DataConflict>();
        var files = new List<string>();

        string Require(string fileName, IEnumerable<string> folders)
        {
            foreach (var folder in folders)
            {
                var candidate = SafeFileAccess.ResolveInsideRoot(dataRoot, Path.Combine(folder, fileName));
                if (File.Exists(candidate))
                {
                    files.Add(Path.GetRelativePath(root, candidate));
                    return candidate;
                }
            }

            throw new DataInputException($"Required input file '{fileName}' not found under '{paths.DataRoot}'.");
        }

        var sections = JsonInputLoader.LoadSections(Require("sections.json", JsonFolders));
        var (bell, declaredCapacity) = JsonInputLoader.LoadBellSchedule(Require("bell-schedule.json", JsonFolders));
        if (declaredCapacity is not null && declaredCapacity != bell.WeeklyTeachingSlots)
        {
            conflicts.Add(new DataConflict("BELL_CAPACITY_MISMATCH", ConflictSeverity.Warning,
                $"bell-schedule.json declares {declaredCapacity} weekly slots but its periods add up to {bell.WeeklyTeachingSlots}.", Source: "bell-schedule.json"));
        }

        var ruleTiers = JsonInputLoader.LoadRuleTiers(Require("scheduling-rules.json", JsonFolders));
        var curricula = CurriculumMarkdownParser.Parse(SafeFileAccess.ReadAllText(Require("CLASS_WISE_SUBJECTS.md", new[] { ".", "datasets" }), SafeFileAccess.MaxMarkdownBytes));

        ClassAssignmentMarkdownParser.Result? classLevel = null;
        try
        {
            classLevel = ClassAssignmentMarkdownParser.Parse(SafeFileAccess.ReadAllText(Require("TEACHER_CLASS_ASSIGNMENTS.md", new[] { ".", "datasets" }), SafeFileAccess.MaxMarkdownBytes));
        }
        catch (DataInputException ex)
        {
            conflicts.Add(new DataConflict(DataConflictTypes.MissingInputFile, ConflictSeverity.Warning, $"Teacher roster unavailable: {ex.Message}", Source: "TEACHER_CLASS_ASSIGNMENTS.md"));
        }

        foreach (var missing in new[] { "Timetable-Engine-Rules-Coverage.md" })
        {
            if (!File.Exists(Path.Combine(dataRoot, missing)))
            {
                conflicts.Add(new DataConflict(DataConflictTypes.MissingInputFile, ConflictSeverity.Info,
                    $"{missing} was referenced by the brief but not supplied; rule tiers are taken from scheduling-rules.json and Timetable-Engine-Rules.md instead.", Source: missing));
            }
        }

        IReadOnlyList<AssignmentRow> rows = Array.Empty<AssignmentRow>();
        var isDummy = false;
        string? workbookSource = null;
        if (string.IsNullOrWhiteSpace(paths.AssignmentsWorkbook))
        {
            conflicts.Add(new DataConflict(DataConflictTypes.MissingInputFile, ConflictSeverity.Error,
                "No assignments workbook configured: every curriculum subject will be reported as MISSING_ASSIGNMENT.", Source: "TEACHER_CLASS_ALLOCATION.xlsx"));
        }
        else
        {
            var workbookPath = SafeFileAccess.ResolveInsideRoot(root, paths.AssignmentsWorkbook);
            if (!File.Exists(workbookPath))
            {
                conflicts.Add(new DataConflict(DataConflictTypes.MissingInputFile, ConflictSeverity.Error,
                    $"Assignments workbook '{Path.GetFileName(workbookPath)}' not found: every curriculum subject will be reported as MISSING_ASSIGNMENT.", Source: paths.AssignmentsWorkbook));
            }
            else
            {
                var workbook = AssignmentWorkbookLoader.Load(workbookPath, paths.AssignmentsSheet);
                rows = workbook.Rows;
                isDummy = workbook.IsDummy;
                workbookSource = Path.GetRelativePath(root, workbookPath);
                files.Add(workbookSource);
                conflicts.AddRange(workbook.Conflicts);
                if (isDummy)
                {
                    conflicts.Add(new DataConflict(DataConflictTypes.DummyData, ConflictSeverity.Warning,
                        "The assignments workbook is DUMMY data synthesized from the class-level TEACHER_CLASS_ASSIGNMENTS.md because TEACHER_CLASS_ALLOCATION.xlsx was not supplied. Results are NOT a client timetable.",
                        Source: workbookSource));
                }
            }
        }

        IReadOnlyList<TeacherAvailability> availability = Array.Empty<TeacherAvailability>();
        var availabilityPath = Path.Combine(configRoot, "teacher-availability.json");
        if (File.Exists(availabilityPath))
        {
            availability = JsonInputLoader.LoadAvailability(availabilityPath);
            files.Add(Path.GetRelativePath(root, availabilityPath));
        }

        if (availability.Count == 0)
        {
            conflicts.Add(new DataConflict(DataConflictTypes.EmptyAvailability, ConflictSeverity.Info,
                "teacher_availability is empty in the client data; all teachers are assumed available in every teaching period (H2 has nothing to block).", RuleId: "H2"));
        }

        IReadOnlyList<DayRestriction> restrictions = Array.Empty<DayRestriction>();
        IReadOnlyList<FixedSlotRule> fixedSlots = Array.Empty<FixedSlotRule>();
        var overridesPath = Path.Combine(configRoot, "scheduling-overrides.json");
        if (File.Exists(overridesPath))
        {
            (restrictions, fixedSlots) = JsonInputLoader.LoadOverrides(overridesPath);
            files.Add(Path.GetRelativePath(root, overridesPath));
        }

        IReadOnlyList<SubjectAlias> aliases = Array.Empty<SubjectAlias>();
        var aliasPath = Path.Combine(configRoot, "subject-aliases.json");
        if (File.Exists(aliasPath))
        {
            aliases = JsonInputLoader.LoadAliases(aliasPath);
            files.Add(Path.GetRelativePath(root, aliasPath));
        }

        conflicts.Add(new DataConflict(DataConflictTypes.RoomRulesSkipped, ConflictSeverity.Info,
            "Room rules R1/R2 not modelled: only 9 generic classrooms exist, room_type_id is null and no Computer/Science Lab exists (TIMETABLE_GENERATION_DATA_REQUIREMENTS.md §6). Production also passes rooms: [] (R5/P9).", RuleId: "R1-R2"));

        return new LoadedDataset(sections, bell, declaredCapacity, curricula, classLevel, rows, isDummy, workbookSource,
            availability, restrictions, fixedSlots, aliases, ruleTiers, conflicts, files);
    }
}
