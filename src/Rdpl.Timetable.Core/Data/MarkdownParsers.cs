using System.Globalization;
using System.Text.RegularExpressions;
using Rdpl.Timetable.Models;

namespace Rdpl.Timetable.Data;

/// <summary>Parses CLASS_WISE_SUBJECTS.md into per-class curricula (ppd / ppw / type).</summary>
public static partial class CurriculumMarkdownParser
{
    [GeneratedRegex(@"^###\s+(Class\s+\d{1,2})\b", RegexOptions.CultureInvariant)]
    private static partial Regex ClassHeading();

    [GeneratedRegex(@"^\|\s*Total periods\s*/\s*week\s*\|\s*\**(\d{1,3})\**\s*\|", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex TotalRow();

    [GeneratedRegex(@"^\|\s*(Subject|Activity Group)\s*\|\s*([^|]{1,120}?)\s*\|\s*(\d{1,2})\s*\|\s*(\d{1,3})\s*\|\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex ItemRow();

    public static IReadOnlyDictionary<string, ClassCurriculum> Parse(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        var result = new Dictionary<string, ClassCurriculum>(StringComparer.OrdinalIgnoreCase);
        string? currentKey = null;
        int? declaredTotal = null;
        var items = new List<CurriculumItem>();
        var lines = markdown.Split('\n');

        void Flush()
        {
            if (currentKey is null)
            {
                return;
            }

            if (result.ContainsKey(currentKey))
            {
                throw new DataInputException($"CLASS_WISE_SUBJECTS.md: duplicate section for {currentKey}.");
            }

            if (items.GroupBy(i => SubjectNames.Normalize(i.Name)).Any(g => g.Count() > 1))
            {
                throw new DataInputException($"CLASS_WISE_SUBJECTS.md: {currentKey} lists the same subject twice.");
            }

            result[currentKey] = new ClassCurriculum(currentKey, declaredTotal, items.ToList());
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r').Trim();
            var heading = ClassHeading().Match(line);
            if (heading.Success)
            {
                Flush();
                currentKey = Regex.Replace(heading.Groups[1].Value, @"\s+", " ");
                declaredTotal = null;
                items.Clear();
                continue;
            }

            if (line.StartsWith("## ", StringComparison.Ordinal) || line.StartsWith("# ", StringComparison.Ordinal))
            {
                Flush();
                currentKey = null;
                items.Clear();
                continue;
            }

            if (currentKey is null)
            {
                continue;
            }

            var total = TotalRow().Match(line);
            if (total.Success)
            {
                declaredTotal = int.Parse(total.Groups[1].Value, CultureInfo.InvariantCulture);
                continue;
            }

            var row = ItemRow().Match(line);
            if (row.Success)
            {
                var type = row.Groups[1].Value == "Subject" ? CurriculumItemType.Subject : CurriculumItemType.ActivityGroup;
                var ppd = int.Parse(row.Groups[3].Value, CultureInfo.InvariantCulture);
                var ppw = int.Parse(row.Groups[4].Value, CultureInfo.InvariantCulture);
                items.Add(new CurriculumItem(currentKey, row.Groups[2].Value.Trim(), type, ppd, ppw, i + 1));
            }
        }

        Flush();
        if (result.Count == 0)
        {
            throw new DataInputException("CLASS_WISE_SUBJECTS.md: no '### Class N' curriculum sections found.");
        }

        return result;
    }
}

/// <summary>
/// Parses TEACHER_CLASS_ASSIGNMENTS.md: the teacher roster (§2) and the class-level assignment tables (§3–§4).
/// This file is CLASS level only; the section-level source of truth is TEACHER_CLASS_ALLOCATION.xlsx.
/// </summary>
public static partial class ClassAssignmentMarkdownParser
{
    [GeneratedRegex(@"^###\s+`([^`]{1,40})`\s+[—–-]\s+(.{1,100})$", RegexOptions.CultureInvariant)]
    private static partial Regex TeacherHeading();

    [GeneratedRegex(@"^\|\s*(Class\s+\d{1,2})\s*\|\s*(Subject|Activity)\s*\|\s*([^|]{1,120}?)\s*\|(?:\s*(\d{1,4})\s*\|)?\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex AssignmentRow();

    [GeneratedRegex(@"^\|\s*\d{1,3}\s*\|\s*`([^`]{1,40})`\s*\|\s*([^|]{1,100}?)\s*\|", RegexOptions.CultureInvariant)]
    private static partial Regex RosterRow();

    [GeneratedRegex(@"^\|\s*`([^`]{1,40})`\s*\|\s*([^|]{1,100}?)\s*\|\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex StaffRow();

    public sealed record Result(
        IReadOnlyList<TeacherInfo> Roster,
        IReadOnlyList<ClassLevelAssignment> Assignments,
        IReadOnlyList<TeacherInfo> StaffWithoutAssignment);

    public static Result Parse(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        var roster = new List<TeacherInfo>();
        var assignments = new List<ClassLevelAssignment>();
        var unassignedStaff = new List<TeacherInfo>();
        var section = 0;
        string? code = null;
        string? name = null;
        var lines = markdown.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r').Trim();
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                var m = Regex.Match(line, @"^##\s+(\d+)\.");
                section = m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : section;
                code = null;
                continue;
            }

            var heading = TeacherHeading().Match(line);
            if (heading.Success)
            {
                code = heading.Groups[1].Value.Trim();
                name = heading.Groups[2].Value.Trim();
                continue;
            }

            if (section == 2)
            {
                var r = RosterRow().Match(line);
                if (r.Success)
                {
                    roster.Add(new TeacherInfo(r.Groups[1].Value.Trim(), r.Groups[2].Value.Trim()));
                }

                continue;
            }

            if (section == 5)
            {
                var s = StaffRow().Match(line);
                if (s.Success)
                {
                    unassignedStaff.Add(new TeacherInfo(s.Groups[1].Value.Trim(), s.Groups[2].Value.Trim()));
                }

                continue;
            }

            if ((section == 3 || section == 4) && code is not null)
            {
                var a = AssignmentRow().Match(line);
                if (a.Success)
                {
                    int? ppw = a.Groups[4].Success ? int.Parse(a.Groups[4].Value, CultureInfo.InvariantCulture) : null;
                    assignments.Add(new ClassLevelAssignment(code, name ?? code, Regex.Replace(a.Groups[1].Value, @"\s+", " "), a.Groups[2].Value, a.Groups[3].Value.Trim(), ppw, i + 1));
                }
            }
        }

        if (assignments.Count == 0)
        {
            throw new DataInputException("TEACHER_CLASS_ASSIGNMENTS.md: no class-level assignment tables found.");
        }

        return new Result(roster, assignments, unassignedStaff);
    }
}
