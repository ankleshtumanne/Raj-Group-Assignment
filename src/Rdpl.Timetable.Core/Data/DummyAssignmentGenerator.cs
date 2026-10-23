using Rdpl.Timetable.Models;

namespace Rdpl.Timetable.Data;

/// <summary>
/// Builds a clearly-labelled DUMMY stand-in for TEACHER_CLASS_ALLOCATION.xlsx while the real workbook is missing.
///
/// It invents NO teachers and NO subjects: every row is derived from a real class-level row of
/// TEACHER_CLASS_ASSIGNMENTS.md and expanded to the real sections of that class (sections.json), using these
/// documented synthesis rules:
///   1. One class-level row per (teacher, class, subject) → section-level rows for that class's sections.
///   2. When several real teachers hold the same (class, subject), sections are distributed round-robin
///      in P6 order (Lily → Lavender → Lotus → Marigold), mirroring production rule T3.
///   3. A teacher's class workload is split evenly over the sections they receive; 0 stays 0 (zero-workload rows kept).
///   4. UNASSIGNED-TT placeholder rows are emitted for EVERY section of the class (the md gives no section detail).
/// Every row carries Data Origin = "DUMMY …" and the workbook's first sheet is a README warning.
/// </summary>
public static class DummyAssignmentGenerator
{
    public const string OriginLabel = "DUMMY - synthesized from TEACHER_CLASS_ASSIGNMENTS.md (class-level); NOT client data";

    public static readonly string[] Header =
    {
        "Section", "Class", "Type", "Subject / Activity", "Teacher Code", "Teacher", "Periods / Week", "Status", "Data Origin",
    };

    public sealed record Summary(int Rows, int PlaceholderRows, int ZeroWorkloadRows, int RoundRobinGroups);

    public static Summary Generate(IReadOnlyList<Section> sections, ClassAssignmentMarkdownParser.Result classLevel, string outputFullPath, IReadOnlySet<string> placeholderCodes)
    {
        var rows = BuildRows(sections, classLevel, placeholderCodes, out var roundRobinGroups);
        var sheetRows = new List<IReadOnlyList<object?>> { Header };
        sheetRows.AddRange(rows.Select(r => (IReadOnlyList<object?>)new object?[]
        {
            r.SectionText, r.ClassText, r.ItemTypeText, r.SubjectName, r.TeacherCode, r.TeacherName,
            r.WorkloadPerWeek, r.StatusText, r.DataOrigin,
        }));

        var readme = new List<IReadOnlyList<object?>>
        {
            new object?[] { "*** DUMMY DATA — NOT THE CLIENT'S TEACHER_CLASS_ALLOCATION.xlsx ***" },
            new object?[] { "Generated because the real section-level workbook was not supplied with the assessment package." },
            new object?[] { "Source: TEACHER_CLASS_ASSIGNMENTS.md (class-level) expanded to sections.json sections." },
            new object?[] { "Rule 1: each class-level row becomes section-level rows for that class." },
            new object?[] { "Rule 2: several teachers for the same class+subject are spread round-robin over sections (Lily, Lavender, Lotus, Marigold) — like engine rule T3." },
            new object?[] { "Rule 3: class workload is split evenly over the teacher's sections; zero stays zero." },
            new object?[] { "Rule 4: UNASSIGNED-TT placeholders are repeated for every section of the class." },
            new object?[] { "Replace this file with the real TEACHER_CLASS_ALLOCATION.xlsx; no code change is needed." },
        };

        var roster = new List<IReadOnlyList<object?>> { new object?[] { "Teacher Code", "Teacher", "Data Origin" } };
        roster.AddRange(classLevel.Roster.Select(t => (IReadOnlyList<object?>)new object?[] { t.Code, t.Name, "TEACHER_CLASS_ASSIGNMENTS.md §2 (client, class-level)" }));

        var gaps = new List<IReadOnlyList<object?>> { new object?[] { "Section", "Subject / Activity", "Teacher Code", "Gap", "Data Origin" } };
        foreach (var r in rows.Where(r => placeholderCodes.Contains(r.TeacherCode) || r.WorkloadPerWeek == 0))
        {
            gaps.Add(new object?[] { r.SectionText, r.SubjectName, r.TeacherCode, placeholderCodes.Contains(r.TeacherCode) ? "UNASSIGNED" : "ZERO_WORKLOAD", OriginLabel });
        }

        XlsxWriter.Write(outputFullPath, new List<(string, IReadOnlyList<IReadOnlyList<object?>>)>
        {
            ("README - DUMMY", readme),
            (AssignmentWorkbookLoader.DefaultSheetName, sheetRows),
            ("Teacher Roster", roster),
            ("Gaps", gaps),
        });

        return new Summary(rows.Count, rows.Count(r => placeholderCodes.Contains(r.TeacherCode)), rows.Count(r => r.WorkloadPerWeek == 0), roundRobinGroups);
    }

    public static List<AssignmentRow> BuildRows(IReadOnlyList<Section> sections, ClassAssignmentMarkdownParser.Result classLevel, IReadOnlySet<string> placeholderCodes, out int roundRobinGroups)
    {
        var result = new List<AssignmentRow>();
        roundRobinGroups = 0;
        var rowNumber = 2;
        var byClass = sections.GroupBy(s => s.CurriculumKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderBy(s => s.SectionOrder).ThenBy(s => s.Id, StringComparer.Ordinal).ToList(), StringComparer.OrdinalIgnoreCase);

        var groups = classLevel.Assignments
            .GroupBy(a => (Class: a.ClassName, Subject: SubjectNames.Normalize(a.SubjectName), Placeholder: placeholderCodes.Contains(a.TeacherCode)))
            .OrderBy(g => g.Min(a => a.SourceLine));

        foreach (var group in groups)
        {
            if (!byClass.TryGetValue(group.Key.Class, out var classSections) || classSections.Count == 0)
            {
                continue;
            }

            var teachers = group.GroupBy(a => a.TeacherCode, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
            if (group.Key.Placeholder)
            {
                var placeholder = teachers[0];
                foreach (var section in classSections)
                {
                    result.Add(Row(section, placeholder, null, "NEEDS_TEACHER_ASSIGNMENT", rowNumber++));
                }

                continue;
            }

            if (teachers.Count > 1)
            {
                roundRobinGroups++;
            }

            var perTeacher = teachers.ToDictionary(t => t.TeacherCode, _ => new List<Section>(), StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < classSections.Count; i++)
            {
                perTeacher[teachers[i % teachers.Count].TeacherCode].Add(classSections[i]);
            }

            foreach (var teacher in teachers)
            {
                var assigned = perTeacher[teacher.TeacherCode];
                if (assigned.Count == 0)
                {
                    continue;
                }

                var total = teacher.PeriodsPerWeek ?? 0;
                for (var i = 0; i < assigned.Count; i++)
                {
                    var share = (total / assigned.Count) + (i < total % assigned.Count ? 1 : 0);
                    result.Add(Row(assigned[i], teacher, share, share == 0 ? "ZERO_WORKLOAD" : "ACTIVE", rowNumber++));
                }
            }
        }

        return result;
    }

    private static AssignmentRow Row(Section section, ClassLevelAssignment source, int? workload, string status, int rowNumber) =>
        new(
            "TEACHER_CLASS_ALLOCATION.DUMMY.xlsx",
            AssignmentWorkbookLoader.DefaultSheetName,
            rowNumber,
            section.DisplayName,
            source.ClassName,
            source.ItemType,
            source.SubjectName,
            source.TeacherCode,
            source.TeacherName,
            workload,
            status,
            OriginLabel);
}
