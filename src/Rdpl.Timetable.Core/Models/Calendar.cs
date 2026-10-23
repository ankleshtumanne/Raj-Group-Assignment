namespace Rdpl.Timetable.Models;

/// <summary>Working days as numbered in bell-schedule.json (1 = Monday … 6 = Saturday).</summary>
public enum SchoolDay
{
    Monday = 1,
    Tuesday = 2,
    Wednesday = 3,
    Thursday = 4,
    Friday = 5,
    Saturday = 6,
}

public static class SchoolDayExtensions
{
    /// <summary>Upper-case name used in the client files and the output JSON (e.g. "MONDAY").</summary>
    public static string ToKey(this SchoolDay day) => day.ToString().ToUpperInvariant();

    public static bool TryParse(string? text, out SchoolDay day)
    {
        day = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return Enum.TryParse(text.Trim(), ignoreCase: true, out day) && Enum.IsDefined(day);
    }
}

/// <summary>One teaching period of a day (breaks are never represented — rule H4).</summary>
public sealed record TeachingPeriod(int Period, string SlotLabel, TimeOnly Start, TimeOnly End);

/// <summary>
/// Teaching layout of one day. Slot pairs follow engine rule B2: consecutive non-break periods are
/// paired N/N+1 and the pair is never split by a break.
/// </summary>
public sealed class DaySchedule
{
    public DaySchedule(SchoolDay day, IReadOnlyList<TeachingPeriod> periods, IReadOnlySet<int> lastPeriodsBeforeBreak, IReadOnlyList<int> morningPeriods)
    {
        ArgumentNullException.ThrowIfNull(periods);
        ArgumentNullException.ThrowIfNull(lastPeriodsBeforeBreak);
        if (periods.Count == 0)
        {
            throw new ArgumentException($"{day} has no teaching periods (rule DM4/Q5).", nameof(periods));
        }

        Day = day;
        Periods = periods.OrderBy(p => p.Period).ToList();
        LastPeriodsBeforeBreak = lastPeriodsBeforeBreak;
        MorningPeriods = morningPeriods;
        Segments = BuildSegments(Periods, lastPeriodsBeforeBreak);
        SlotPairs = BuildPairs(Segments);
    }

    public SchoolDay Day { get; }

    public IReadOnlyList<TeachingPeriod> Periods { get; }

    /// <summary>Periods that are immediately followed by a break (used by rule L2).</summary>
    public IReadOnlySet<int> LastPeriodsBeforeBreak { get; }

    public IReadOnlyList<int> MorningPeriods { get; }

    /// <summary>Runs of consecutive teaching periods not interrupted by a break (used by L3).</summary>
    public IReadOnlyList<IReadOnlyList<int>> Segments { get; }

    /// <summary>Fixed slot pairs (B2). A trailing odd period in a segment is left unpaired.</summary>
    public IReadOnlyList<(int First, int Second)> SlotPairs { get; }

    public IEnumerable<int> PeriodNumbers => Periods.Select(p => p.Period);

    public bool HasPeriod(int period) => Periods.Any(p => p.Period == period);

    /// <summary>The pair containing the period, or null when the period is unpaired.</summary>
    public (int First, int Second)? PairOf(int period)
    {
        foreach (var pair in SlotPairs)
        {
            if (pair.First == period || pair.Second == period)
            {
                return pair;
            }
        }

        return null;
    }

    private static List<IReadOnlyList<int>> BuildSegments(IReadOnlyList<TeachingPeriod> periods, IReadOnlySet<int> breaksAfter)
    {
        var segments = new List<IReadOnlyList<int>>();
        var current = new List<int>();
        foreach (var p in periods)
        {
            current.Add(p.Period);
            if (breaksAfter.Contains(p.Period))
            {
                segments.Add(current);
                current = new List<int>();
            }
        }

        if (current.Count > 0)
        {
            segments.Add(current);
        }

        return segments;
    }

    private static List<(int, int)> BuildPairs(IReadOnlyList<IReadOnlyList<int>> segments)
    {
        var pairs = new List<(int, int)>();
        foreach (var segment in segments)
        {
            for (var i = 0; i + 1 < segment.Count; i += 2)
            {
                pairs.Add((segment[i], segment[i + 1]));
            }
        }

        return pairs;
    }
}

/// <summary>The weekly bell schedule (Mon–Thu profile + Fri–Sat profile merged into one week grid).</summary>
public sealed class BellSchedule
{
    public BellSchedule(string name, IReadOnlyList<DaySchedule> days)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(days);
        if (days.Count == 0)
        {
            throw new ArgumentException("Bell schedule has no working days (rule P1).", nameof(days));
        }

        Name = name;
        Days = days.OrderBy(d => d.Day).ToList();
    }

    public string Name { get; }

    public IReadOnlyList<DaySchedule> Days { get; }

    public int WeeklyTeachingSlots => Days.Sum(d => d.Periods.Count);

    public DaySchedule? GetDay(SchoolDay day) => Days.FirstOrDefault(d => d.Day == day);
}
