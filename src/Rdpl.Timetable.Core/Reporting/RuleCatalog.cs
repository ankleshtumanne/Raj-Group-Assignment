using Rdpl.Timetable.Configuration;

namespace Rdpl.Timetable.Reporting;

/// <summary>Maps every rule ID from scheduling-rules.json / Timetable-Engine-Rules.md to how this solver handles it (AC-8).</summary>
public static class RuleCatalog
{
    public static IReadOnlyList<RuleCoverageDto> Build(RuleOptions rules)
    {
        var l1 = rules.L1SoftCurriculumKeys.Count == 0
            ? "Hard"
            : $"Hard, except SOFT (weight {rules.Weights.L1Relaxed}) for {string.Join(", ", rules.L1SoftCurriculumKeys)} [profile '{rules.Profile}']";
        var h5 = rules.BlockSinglesSoft
            ? $"SOFT (weight {rules.Weights.LoneBlockSingle} per lone single, minimised) [profile '{rules.Profile}']"
            : "Hard (strict: full pairs only)";

        return new List<RuleCoverageDto>
        {
            new("H1", "mandatoryHard", "Hard", "AddAtMostOne over all x of a teacher per (day, period), across all sections. Activity instructors excluded (T6)."),
            new("H2", "mandatoryHard", "Hard (no data)", "No x variable for a teacher's off-day/unavailable cells (config/teacher-availability.json). Client table is empty → all available."),
            new("H3", "mandatoryHard", "Hard", "AddExactlyOne per section per teaching (day, period) when the section's demand equals the 52 slots."),
            new("H4", "mandatoryHard", "Hard (structural)", "Variables are created only for teaching periods from bell-schedule.json; breaks never exist in the model."),
            new("H6", "mandatoryHard", "Hard (no data)", "No x variable outside dayRestrictions (config/scheduling-overrides.json). Client provides none."),
            new("H7", "mandatoryHard", "Hard", "Σ x[i, day, ·] ≤ periodsPerDay."),
            new("H8", "mandatoryHard", "Hard", "Σ x[i, ·, ·] = periodsPerWeek from CLASS_WISE_SUBJECTS.md (curriculum, not workload_per_week)."),
            new("H9", "mandatoryHard", "Hard (no data)", "Activity x restricted to fixedSlots (exact cell or whole day, AC1/AC2). Client provides none."),
            new("H10", "mandatoryHard", "Hard", "Curriculum items with ppw ≤ 0 get no variables; zero-workload assignment rows are excluded (ZeroWorkloadPolicy=" + rules.ZeroWorkloadPolicy + ") and reported."),
            new("G1", "mandatoryHard", "Hard (structural)", "Games/Library items (incl. 'Games / Library') get no variable in the first slot pair of each day."),
            new("G2", "mandatoryHard", "Hard", "Class 11/12: Σ dayUsed(Games-family item) ≤ 1 per day ('Games / Library' counts as one family item)."),
            new("L1", "mandatoryHard", l1, "Day-usage booleans u[i,d]; Σ u over a same-first-word group ≤ 1 per section per day."),
            new("L2", "mandatoryHard", "Hard", "x[last-before-break] + x[first-after-break] ≤ 1 per item."),
            new("L3", "mandatoryHard", "Hard", $"Window of {rules.MaxConsecutiveSameSubject + 1} consecutive periods (within a break-free segment) ≤ {rules.MaxConsecutiveSameSubject}; implied by H7 when ppd ≤ {rules.MaxConsecutiveSameSubject}."),
            new("T2", "mandatoryHard", "Hard (structural)", "Teacher is fixed per item from the formal assignment row; the solver never chooses teachers."),
            new("DATA-1/T4", "mandatoryHard", "Hard (structural)", "UNASSIGNED-TT rows never produce lessons; affected items become RESERVED_UNSTAFFED (UnstaffedPolicy=" + rules.UnstaffedPolicy + ") and are listed in dataConflicts."),
            new("H5", "blockAndLayout", h5, "x = y(pair) + z(single); z variables exist only when singles are allowed (pragmatic profile / diagnostic) and each carries a penalty."),
            new("B1/B2", "blockAndLayout", "Hard", "Pair variables y only for fixed consecutive non-break pairs (1-2, 3-4, 5-6, 7-8, 9-10)."),
            new("B3", "blockAndLayout", "Not modelled (heuristic)", "Single pairing is a JS-engine placement heuristic; CP-SAT satisfies H3 directly."),
            new("B3b", "blockAndLayout", "Hard", "H7/H8 are equalities/caps on every path; no force-fill exists."),
            new("B4", "blockAndLayout", "Hard (implied by L1)", "Same-first-word items cannot share a day, so they cannot share a slot pair. Validator checks it explicitly."),
            new("PR-MATH", "soft", rules.EnableSoftObjective ? "Soft" : "Off", $"Penalty {rules.Weights.MathOutsideMorning} per Mathematics period not in periods 1–4 of Mon–Thu."),
            new("DB max_one_games_per_day", "soft", rules.EnableSoftObjective ? "Soft" : "Off", $"Penalty {rules.Weights.GamesAboveOnePerDay} per Games period above 1/day. NOTE: conflicts with B1 for ppd=2 Games items (always ≥1)."),
            new("DB max_one_library_per_day", "soft", rules.EnableSoftObjective ? "Soft" : "Off", $"Penalty {rules.Weights.LibraryAboveOnePerDay} per Library period above 1/day."),
            new("DB max_two_consecutive_games", "soft", rules.EnableSoftObjective ? "Soft" : "Off", $"Penalty {rules.Weights.GamesConsecutiveAboveTwo} per 3-period window with 3 Games periods."),
            new("PR1/PR5", "soft", "Not applicable", "Placement-order heuristics of the JS engine; meaningless for an exact solver."),
            new("R1/R2", "optional", "Skipped (documented)", "No lab rooms, room_type_id null, 9 rooms for 41 sections; production also passes rooms: [] (R5)."),
            new("M1-M6", "optional", "Not implemented", "No merge rules supplied in the dataset."),
            new("P4", "optional", rules.ExcludePrePrimary ? "Applied" : "Off", "Pre-Nursery/Nursery/Junior KG/Senior KG excluded; they also have no curriculum in CLASS_WISE_SUBJECTS.md."),
            new("T6/AC8", "engine note", "Applied", "Activity groups are not teacher-booked."),
            new("SE/SA/ST/AT", "deferred", "Out of scope", "Substitution engine — follow-up epic."),
            new("FF (force-fill)", "deferred", "Replaced", "No force-fill: CP-SAT is exact. When strict rules are infeasible, a separate DIAGNOSTIC solve relaxes H1/L1/G2/H5 with large penalties to show where they break."),
        };
    }
}
