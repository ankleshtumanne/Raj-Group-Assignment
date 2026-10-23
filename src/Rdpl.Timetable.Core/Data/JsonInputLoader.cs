using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Rdpl.Timetable.Models;

namespace Rdpl.Timetable.Data;

/// <summary>Loads and validates the JSON inputs (sections.json, bell-schedule.json and the config/*.json files).</summary>
public static partial class JsonInputLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        MaxDepth = 32,
        AllowTrailingCommas = false,
        ReadCommentHandling = JsonCommentHandling.Skip,
        NumberHandling = JsonNumberHandling.Strict,
    };

    // Section ids in sections.json are kebab-case slugs ("class-10-lily").
    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,63}$")]
    private static partial Regex SectionIdPattern();

    private static readonly Dictionary<string, SchoolDay[]> ProfileDays = new(StringComparer.OrdinalIgnoreCase)
    {
        ["mondayToThursday"] = new[] { SchoolDay.Monday, SchoolDay.Tuesday, SchoolDay.Wednesday, SchoolDay.Thursday },
        ["fridayAndSaturday"] = new[] { SchoolDay.Friday, SchoolDay.Saturday },
    };

    public static T Deserialize<T>(string fullPath)
    {
        var text = SafeFileAccess.ReadAllText(fullPath, SafeFileAccess.MaxJsonBytes);
        try
        {
            return JsonSerializer.Deserialize<T>(text, Options)
                   ?? throw new DataInputException($"{Path.GetFileName(fullPath)} is empty.");
        }
        catch (JsonException ex)
        {
            // Do not echo file content back; line/position is enough to fix it.
            throw new DataInputException($"{Path.GetFileName(fullPath)} is not valid JSON (line {ex.LineNumber}, position {ex.BytePositionInLine}).", ex);
        }
    }

    public static IReadOnlyList<Section> LoadSections(string fullPath)
    {
        var dto = Deserialize<SectionsFileDto>(fullPath);
        if (dto.Sections is null || dto.Sections.Count == 0)
        {
            throw new DataInputException("sections.json contains no sections (rule P2).");
        }

        if (dto.Sections.Count > 2000)
        {
            throw new DataInputException("sections.json has an implausible number of sections (> 2000).");
        }

        var result = new List<Section>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in dto.Sections)
        {
            var id = s.Id?.Trim() ?? string.Empty;
            if (!SectionIdPattern().IsMatch(id))
            {
                throw new DataInputException($"sections.json: invalid section id '{Truncate(id)}'.");
            }

            if (!seen.Add(id))
            {
                throw new DataInputException($"sections.json: duplicate section id '{id}'.");
            }

            var display = RequireText(s.DisplayName, $"section '{id}' displayName", 100);
            var grade = RequireText(s.Grade, $"section '{id}' grade", 50);
            var name = RequireText(s.Section, $"section '{id}' section", 50);
            var key = RequireText(s.CurriculumKey, $"section '{id}' curriculumKey", 50);
            if (s.Capacity is < 0 or > 1000)
            {
                throw new DataInputException($"sections.json: section '{id}' has an invalid capacity.");
            }

            result.Add(new Section(id, display, grade, name, key, s.Capacity ?? 0));
        }

        return result;
    }

    public static (BellSchedule Bell, int? DeclaredWeeklyCapacity) LoadBellSchedule(string fullPath)
    {
        var dto = Deserialize<BellFileDto>(fullPath);
        var name = string.IsNullOrWhiteSpace(dto.ScheduleName) ? "Bell schedule" : dto.ScheduleName.Trim();
        if (dto.WorkingDays is null || dto.WorkingDays.Count == 0)
        {
            throw new DataInputException("bell-schedule.json: workingDays is empty (rule P1/P7).");
        }

        var profiles = new Dictionary<string, BellProfileDto?>(StringComparer.OrdinalIgnoreCase)
        {
            ["mondayToThursday"] = dto.MondayToThursday,
            ["fridayAndSaturday"] = dto.FridayAndSaturday,
        };

        var days = new List<DaySchedule>();
        foreach (var dayText in dto.WorkingDays.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!SchoolDayExtensions.TryParse(dayText, out var day))
            {
                throw new DataInputException($"bell-schedule.json: unknown working day '{Truncate(dayText)}'.");
            }

            // Engine rule Q4/M2: each weekday is owned by exactly one profile.
            var owners = profiles.Where(p => ProfileDays[p.Key].Contains(day) && p.Value is not null).ToList();
            if (owners.Count != 1)
            {
                throw new DataInputException($"bell-schedule.json: {day.ToKey()} must belong to exactly one bell profile (found {owners.Count}).");
            }

            days.Add(BuildDay(day, owners[0].Value!, owners[0].Key));
        }

        return (new BellSchedule(name, days), dto.WeeklyTeachingCapacity);
    }

    private static DaySchedule BuildDay(SchoolDay day, BellProfileDto profile, string profileName)
    {
        if (profile.TeachingPeriods is null || profile.TeachingPeriods.Count == 0)
        {
            throw new DataInputException($"bell-schedule.json: profile '{profileName}' has no teaching periods (rule Q5).");
        }

        var periods = new List<TeachingPeriod>();
        foreach (var p in profile.TeachingPeriods)
        {
            if (p.Type is not null && !p.Type.Equals("Teaching", StringComparison.OrdinalIgnoreCase))
            {
                continue; // H4: anything that is not a teaching period is never schedulable.
            }

            if (p.Period is null or < 1 or > 20)
            {
                throw new DataInputException($"bell-schedule.json: profile '{profileName}' has an invalid period number.");
            }

            var start = ParseTime(p.Start, profileName);
            var end = ParseTime(p.End, profileName);
            if (end <= start)
            {
                throw new DataInputException($"bell-schedule.json: period {p.Period} in '{profileName}' ends before it starts.");
            }

            periods.Add(new TeachingPeriod(p.Period.Value, p.Slot?.Trim() ?? p.Period.Value.ToString(CultureInfo.InvariantCulture), start, end));
        }

        periods = periods.OrderBy(p => p.Start).ToList();
        if (periods.Select(p => p.Period).Distinct().Count() != periods.Count)
        {
            throw new DataInputException($"bell-schedule.json: duplicate period numbers in '{profileName}'.");
        }

        for (var i = 1; i < periods.Count; i++)
        {
            if (periods[i].Period <= periods[i - 1].Period || periods[i].Start < periods[i - 1].End)
            {
                throw new DataInputException($"bell-schedule.json: periods in '{profileName}' overlap or are out of order.");
            }
        }

        // A break separates period i and i+1 when a break starts at period i's end, or when there is a time gap.
        var breakStarts = (profile.BreakPeriods ?? new List<BreakDto>()).Select(b => ParseTime(b.Start, profileName)).ToHashSet();
        var lastBeforeBreak = new HashSet<int>();
        for (var i = 0; i < periods.Count - 1; i++)
        {
            if (breakStarts.Contains(periods[i].End) || periods[i + 1].Start > periods[i].End)
            {
                lastBeforeBreak.Add(periods[i].Period);
            }
        }

        var morning = profile.MorningPeriods?.Where(m => periods.Any(p => p.Period == m)).ToList() ?? new List<int>();
        return new DaySchedule(day, periods, lastBeforeBreak, morning);
    }

    public static IReadOnlyList<TeacherAvailability> LoadAvailability(string fullPath)
    {
        var dto = Deserialize<AvailabilityFileDto>(fullPath);
        var result = new List<TeacherAvailability>();
        foreach (var t in dto.Teachers ?? new List<AvailabilityDto>())
        {
            var code = RequireText(t.TeacherCode, "teacher-availability teacherCode", 40);
            var off = new HashSet<SchoolDay>();
            foreach (var d in t.OffDays ?? new List<string>())
            {
                if (!SchoolDayExtensions.TryParse(d, out var day))
                {
                    throw new DataInputException($"teacher-availability.json: invalid off-day '{Truncate(d)}' for {code} (rule DM1).");
                }

                off.Add(day);
            }

            if (off.Count >= 6)
            {
                throw new DataInputException($"teacher-availability.json: {code} cannot be off on all days (rule DM2).");
            }

            var cells = new HashSet<(SchoolDay, int)>();
            foreach (var u in t.Unavailable ?? new List<SlotDto>())
            {
                if (!SchoolDayExtensions.TryParse(u.Day, out var day) || u.Period is null or < 1)
                {
                    throw new DataInputException($"teacher-availability.json: invalid unavailable slot for {code}.");
                }

                cells.Add((day, u.Period.Value));
            }

            result.Add(new TeacherAvailability(code, off, cells));
        }

        if (result.Select(r => r.TeacherCode).Distinct(StringComparer.OrdinalIgnoreCase).Count() != result.Count)
        {
            throw new DataInputException("teacher-availability.json: one record per teacher is allowed (rule DM3).");
        }

        return result;
    }

    public static (IReadOnlyList<DayRestriction> DayRestrictions, IReadOnlyList<FixedSlotRule> FixedSlots) LoadOverrides(string fullPath)
    {
        var dto = Deserialize<OverridesFileDto>(fullPath);
        var restrictions = new List<DayRestriction>();
        foreach (var r in dto.DayRestrictions ?? new List<DayRestrictionDto>())
        {
            var days = new HashSet<SchoolDay>();
            foreach (var d in r.Days ?? new List<string>())
            {
                if (!SchoolDayExtensions.TryParse(d, out var day))
                {
                    throw new DataInputException($"scheduling-overrides.json: invalid day '{Truncate(d)}'.");
                }

                days.Add(day);
            }

            if (days.Count == 0)
            {
                throw new DataInputException("scheduling-overrides.json: a day restriction needs at least one day.");
            }

            restrictions.Add(new DayRestriction(RequireText(r.CurriculumKey, "curriculumKey", 50), RequireText(r.Item, "item", 120), days));
        }

        var fixedSlots = new List<FixedSlotRule>();
        foreach (var f in dto.FixedSlots ?? new List<FixedSlotDto>())
        {
            var slots = new List<(SchoolDay, int?)>();
            foreach (var s in f.Slots ?? new List<SlotDto>())
            {
                if (!SchoolDayExtensions.TryParse(s.Day, out var day) || s.Period is < 1)
                {
                    throw new DataInputException("scheduling-overrides.json: invalid fixed slot.");
                }

                slots.Add((day, s.Period));
            }

            if (slots.Count == 0)
            {
                throw new DataInputException("scheduling-overrides.json: a fixed-slot rule needs at least one slot (null periodId must be resolved first).");
            }

            fixedSlots.Add(new FixedSlotRule(RequireText(f.CurriculumKey, "curriculumKey", 50), RequireText(f.Item, "item", 120), slots));
        }

        return (restrictions, fixedSlots);
    }

    public static IReadOnlyList<SubjectAlias> LoadAliases(string fullPath)
    {
        var dto = Deserialize<AliasFileDto>(fullPath);
        var result = new List<SubjectAlias>();
        foreach (var a in dto.Aliases ?? new List<AliasDto>())
        {
            var targets = (a.CurriculumItems ?? new List<string>()).Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).ToList();
            if (targets.Count == 0)
            {
                throw new DataInputException("subject-aliases.json: alias without curriculumItems.");
            }

            result.Add(new SubjectAlias(RequireText(a.AssignmentSubject, "assignmentSubject", 120), targets, a.Reason?.Trim() ?? string.Empty));
        }

        return result;
    }

    /// <summary>Reads the rule ids per tier from scheduling-rules.json (used for the coverage table in the report).</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> LoadRuleTiers(string fullPath)
    {
        using var doc = JsonDocument.Parse(SafeFileAccess.ReadAllText(fullPath, SafeFileAccess.MaxJsonBytes), new JsonDocumentOptions { MaxDepth = 32 });
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var tier in new[] { "mandatoryHard", "blockAndLayout", "soft", "optional", "deferred" })
        {
            if (doc.RootElement.TryGetProperty(tier, out var array) && array.ValueKind == JsonValueKind.Array)
            {
                result[tier] = array.EnumerateArray()
                    .Select(e => e.TryGetProperty("id", out var id) ? id.GetString() ?? string.Empty : string.Empty)
                    .Where(s => s.Length > 0)
                    .ToList();
            }
        }

        return result;
    }

    private static TimeOnly ParseTime(string? text, string profileName)
    {
        if (!TimeOnly.TryParseExact(text?.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
        {
            throw new DataInputException($"bell-schedule.json: invalid time '{Truncate(text)}' in '{profileName}' (expected HH:mm).");
        }

        return t;
    }

    private static string RequireText(string? value, string what, int maxLength)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v))
        {
            throw new DataInputException($"Missing value: {what}.");
        }

        if (v.Length > maxLength)
        {
            throw new DataInputException($"Value too long: {what}.");
        }

        return v;
    }

    private static string Truncate(string? s) => s is null ? string.Empty : s.Length <= 40 ? s : s[..40] + "…";

    private sealed class SectionsFileDto
    {
        public List<SectionDto>? Sections { get; set; }
    }

    private sealed class SectionDto
    {
        public string? Id { get; set; }

        public string? DisplayName { get; set; }

        public string? Grade { get; set; }

        public string? Section { get; set; }

        public string? CurriculumKey { get; set; }

        public int? Capacity { get; set; }
    }

    private sealed class BellFileDto
    {
        public string? ScheduleName { get; set; }

        public List<string>? WorkingDays { get; set; }

        public BellProfileDto? MondayToThursday { get; set; }

        public BellProfileDto? FridayAndSaturday { get; set; }

        public int? WeeklyTeachingCapacity { get; set; }
    }

    private sealed class BellProfileDto
    {
        public List<PeriodDto>? TeachingPeriods { get; set; }

        public List<BreakDto>? BreakPeriods { get; set; }

        public List<int>? MorningPeriods { get; set; }
    }

    private sealed class PeriodDto
    {
        public int? Period { get; set; }

        public string? Slot { get; set; }

        public string? Start { get; set; }

        public string? End { get; set; }

        public string? Type { get; set; }
    }

    private sealed class BreakDto
    {
        public string? Slot { get; set; }

        public string? Start { get; set; }

        public string? End { get; set; }
    }

    private sealed class AvailabilityFileDto
    {
        public List<AvailabilityDto>? Teachers { get; set; }
    }

    private sealed class AvailabilityDto
    {
        public string? TeacherCode { get; set; }

        public List<string>? OffDays { get; set; }

        public List<SlotDto>? Unavailable { get; set; }
    }

    private sealed class SlotDto
    {
        public string? Day { get; set; }

        public int? Period { get; set; }
    }

    private sealed class OverridesFileDto
    {
        public List<DayRestrictionDto>? DayRestrictions { get; set; }

        public List<FixedSlotDto>? FixedSlots { get; set; }
    }

    private sealed class DayRestrictionDto
    {
        public string? CurriculumKey { get; set; }

        public string? Item { get; set; }

        public List<string>? Days { get; set; }
    }

    private sealed class FixedSlotDto
    {
        public string? CurriculumKey { get; set; }

        public string? Item { get; set; }

        public List<SlotDto>? Slots { get; set; }
    }

    private sealed class AliasFileDto
    {
        public List<AliasDto>? Aliases { get; set; }
    }

    private sealed class AliasDto
    {
        public string? AssignmentSubject { get; set; }

        public List<string>? CurriculumItems { get; set; }

        public string? Reason { get; set; }
    }
}
