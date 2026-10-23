# RDPL Full-School Timetable Solver (C# · Google OR-Tools CP-SAT)

A constraint-based weekly timetable generator for the RDPL assessment. It loads the client dataset (JSON +
markdown + Excel), builds **one joint CP-SAT model for all sections** (shared teachers couple them), solves it,
**re-checks the result with an independent validator**, and writes a JSON report with section timetables,
teacher timetables, `dataConflicts` and `schedulingConflicts`. Console host and ASP.NET Core Web API host.

> **⚠ DUMMY ASSIGNMENT DATA.** `TEACHER_CLASS_ALLOCATION.xlsx` (the primary, section-level assignment source,
> 587 rows) and `Timetable-Engine-Rules-Coverage.md` were **not supplied**. Until they are, the solver uses
> `data/dummy/TEACHER_CLASS_ALLOCATION.DUMMY.xlsx`, synthesized from the **class-level**
> `TEACHER_CLASS_ASSIGNMENTS.md` (rules in [§5.3](#53-dummy-workbook-how-it-is-built)). Every run that uses it says so in
> the console, in `dataSource.kind = "DUMMY_ASSIGNMENTS"`, in `dataConflicts[DUMMY_DATA_IN_USE]` and in
> assumption A1. **No result below is a client timetable.** Drop the real workbook in and re-run; no code changes
> are needed ([§5.2](#52-replacing-the-dummy-with-the-real-workbook)).

---

## 1. Results (verified in this repository)

Measured on a 2-core sandbox, `--workers 2`, real client JSON/markdown + DUMMY assignments.

| Run | Outcome | Solver | Sections | Cells | Independent validation |
| --- | --- | --- | --- | --- | --- |
| `--profile strict` | **INFEASIBLE** — proven before solving | not run (pre-solve proof); diagnostic solve FEASIBLE | 0 / 30 | – | n/a |
| `--profile pragmatic --time-limit 300` | **TIMETABLE_GENERATED** | FEASIBLE, objective 27,092 | **30 / 30** | **1,560 / 1,560** (906 lessons + 654 reserved) | **0 hard violations**; objective equals independently recomputed penalty |

Why strict is infeasible: the rules taken literally contradict the curriculum. The pre-solve analysis proves it
with 82 specific findings (`output/samples/timetable-strict.json`):

* **76 × `BLOCK_PARITY` (H5 + B1 + H8).** A block subject (periodsPerDay ≥ 2) placed only as full slot pairs can only
  reach an *even* weekly count. 76 section-items have an odd count, e.g. Mathematics 9, English Language 5, Accounts 11.
* **4 × `L1_DAY_CAPACITY` (L1).** Class 11/12: English Language 5/wk + English Literature 7/wk at ≤ 2/day need
  ≥ 3 + 4 = 7 distinct days, and the week has 6.
* **2 × `SINGLES_UNPAIRABLE` (H5).** In Grade 6, G.K./Moral is the only single-period subject, so its half-pairs cannot be filled
  without a lone block single.

The production JS engine escapes these through force-fill ("block subjects can be placed as singles", L1
relaxed in pass A2). Profile **`pragmatic`** does the same thing *optimally*, as explicit penalties:

* H5 lone singles cost 300 each and are minimised. The result has **80**, which is the counting minimum
  (76 odd items × 1, plus 2 per Grade 6 section).
* L1 is soft **only** for Class 11/12, at 500 per extra day. The result has **4**, also the minimum (1 per section).
* Everything else stays hard (H1, H3, H4, H7, H8, G1, G2, L2, L3, T2, DATA-1, B1, B3b).

Remaining soft penalty: PR-MATH 580 and the DB Games/Library-per-day rules 2 × 256. The latter conflict with B1 for
2-period Games/Library blocks; see [§6.3](#63-soft-constraints-tier-3).

## 2. Quick start

Prerequisites: **.NET 8 SDK**, internet access for the first `dotnet restore` (NuGet: `Google.OrTools`, xUnit).

```bash
dotnet restore
dotnet build -c Release

# Data check only (no solver): loads, normalizes, reports every data gap and pre-solve finding.
dotnet run -c Release --project src/Rdpl.Timetable.Cli -- validate-data

# Strict (every Tier-1/Tier-2 rule hard): returns INFEASIBLE with explanations + a diagnostic solve.
dotnet run -c Release --project src/Rdpl.Timetable.Cli -- generate --profile strict

# Pragmatic (H5 soft everywhere, L1 soft for Class 11/12 only): full-school timetable.
dotnet run -c Release --project src/Rdpl.Timetable.Cli -- generate --profile pragmatic --time-limit 300

dotnet test -c Release            # 60+ tests; the full-school integration test takes ~3 minutes
```

Run from the repository root (paths are resolved against the current directory). Reports are written to
`output/timetable-<profile>.json`. Exit codes: `0` generated and validated, `2` infeasible / no timetable,
`3` solution failed validation, `4` input data error, `64` usage error.

**Workers.** CP-SAT runs a portfolio of workers; use about one per CPU core (`--workers 8` on an 8-core machine). On the
2-core verification sandbox, `--workers 2` gave much better results than 8 oversubscribed workers.

### CLI options

| Option | Default | Meaning |
| --- | --- | --- |
| `--profile strict\|pragmatic` | `strict` | Rule profile ([§7](#7-rule-profiles)) |
| `--time-limit <s>` | 120 | CP-SAT limit, 1–1800 s (warm start included) |
| `--workers <n>` | 8 | CP-SAT workers, 1–64 |
| `--seed <n>` | 42 | Random seed (runs are reproducible for a fixed seed and worker count) |
| `--assignments <xlsx\|none>` | DUMMY workbook | Section-level assignment workbook |
| `--assignments-sheet <name>` | `Section Assignments` | Sheet name (falls back to the first sheet with the right columns) |
| `--data-root <dir>` | `data/client` | Client package folder (markdown at root, JSON in `datasets/` or `candidate-data/`) |
| `--zero-workload Exclude\|TreatAsAssigned` | `Exclude` | H10 handling of `workload_per_week = 0` rows |
| `--unstaffed ReserveSlot\|Exclude` | `ReserveSlot` | What to do with curriculum items that have no schedulable teacher |
| `--no-warm-start`, `--no-soft`, `--no-diagnostic`, `--no-aliases`, `--include-pre-primary` | | Switches |

Precedence: command line > environment variables (`TIMETABLE_TIME_LIMIT=300`, `TIMETABLE_PROFILE=pragmatic`, …) >
`config/timetable.settings.json` > defaults.

### Web API

```bash
# The API refuses to start without a key (≥ 32 chars). Never put it in appsettings.json.
dotnet user-secrets set "Api:ApiKey" "$(openssl rand -hex 32)" --project src/Rdpl.Timetable.Api   # Development
#   or:  export Api__ApiKey=...                     (any environment)
#   or:  Azure Key Vault secret "Api--ApiKey"       (build with -p:EnableKeyVault=true, set KeyVault__Uri)
dotnet run -c Release --project src/Rdpl.Timetable.Api --urls http://localhost:5080
```

| Method | Endpoint | Purpose |
| --- | --- | --- |
| `GET` | `/health` | Liveness (anonymous) |
| `POST` | `/api/timetable/load-data` | Load + validate + pre-solve analysis; returns summary and conflicts (synchronous, < 1 s) |
| `POST` | `/api/timetable/generate` | Starts a solve in the background → `202 Accepted` + job; `409` if one is already running |
| `GET` | `/api/timetable/jobs/{id}` | Job state, solver status, summary |
| `GET` | `/api/timetable/result` | Full report of the latest finished generation (same JSON as the CLI) |
| `GET` | `/api/timetable/sections` · `/sections/{id}` | All section timetables · one (`class-10-lily` or `Grade 10 - Lily`) |
| `GET` | `/api/timetable/teachers` | Teacher-wise timetables |
| `GET` | `/api/timetable/conflicts` | `dataConflicts`, `schedulingConflicts`, diagnostic violations |

Body for `load-data` / `generate` (all optional, validated and bounded):
`{ "profile": "pragmatic", "timeLimitSeconds": 300, "workers": 8, "randomSeed": 42, "zeroWorkloadPolicy": "Exclude", "unstaffedPolicy": "ReserveSlot", "softObjective": true }`.
Send the key as header `X-Api-Key`. Examples are in `src/Rdpl.Timetable.Api/Rdpl.Timetable.Api.http`.
`generate` is asynchronous because a full-school solve takes minutes.

## 3. Project structure

```text
RdplTimetable.sln
src/Rdpl.Timetable.Core/          class library — all logic, no host concerns
  Data/            loaders: JSON (sections, bell, rules, config), markdown (curriculum, roster), .xlsx reader/writer,
                   assignment workbook loader, DUMMY generator, SafeFileAccess (path/size/symlink guards)
  Models/          Section, CurriculumItem, AssignmentRow, BellSchedule/DaySchedule (slot pairs, breaks), LessonItem,
                   SchedulingProblem, DataConflict, SchedulingConflict
  Normalization/   ProblemBuilder: curriculum × sections × formal teacher (T2) → LessonItems; reports every gap
  Configuration/   RuleOptions (profiles, policies, weights), SolverOptions (bounded)
  Constraints/     TimetableModelBuilder: the CpModel (variables + every rule)            ← AC-1
  Solver/          TimetableSolver (CP-SAT run, extraction), WarmStartBuilder (solution hint)
  Validation/      PreSolveAnalyzer (proofs of infeasibility), TimetableValidator (independent re-check)
  Reporting/       ReportBuilder, report DTOs, RuleCatalog (rule → implementation map in every report)
  TimetableEngine.cs   pipeline: normalize → pre-solve → solve → validate → (diagnostic) → report
src/Rdpl.Timetable.Cli/           console host (commands generate / validate-data / generate-dummy-assignments)
src/Rdpl.Timetable.Api/           ASP.NET Core minimal API host (API key, rate limit, background jobs)
tests/Rdpl.Timetable.Tests/       xUnit: per-rule CP-SAT scenarios, validator, loaders, security, full school
data/client/                      the client package exactly as received (read-only)
data/dummy/                       TEACHER_CLASS_ALLOCATION.DUMMY.xlsx (clearly labelled stand-in)
config/                           subject-aliases.json, teacher-availability.json, scheduling-overrides.json, settings
output/samples/                   sample reports from the verified runs
docs/VERIFICATION.md              how this repository was built and verified (sandbox without NuGet)
```

NuGet packages: `Google.OrTools` (CP-SAT, required by the brief); `xunit`, `xunit.runner.visualstudio`,
`Microsoft.NET.Test.Sdk` (tests); optional `Azure.Extensions.AspNetCore.Configuration.Secrets` + `Azure.Identity`
(only when built with `-p:EnableKeyVault=true`). **Excel is read with the BCL only** (`System.IO.Compression` +
`System.Xml`), so no ClosedXML. That means one less dependency, and the reader is hardened against zip bombs and XXE.

## 4. Pipeline

```text
sections.json ─┐                 ┌─ dataConflicts[] (every excluded / reinterpreted row, never silent)
bell-schedule ─┤                 │
CLASS_WISE_*.md├─► DatasetLoader ─► ProblemBuilder ─► PreSolveAnalyzer ─► TimetableSolver ─► TimetableValidator ─► ReportBuilder ─► JSON
TEACHER_*.md   ┤   (validated)      (LessonItems)      (proofs)            (warm start +      (independent of      (+ RuleCatalog,
*.xlsx ────────┤                                                           joint CP-SAT)      CP-SAT variables)     assumptions)
config/*.json ─┘                                   └─ if strict is infeasible: diagnostic solve (relaxed, never "success")
```

## 5. Data mapping

### 5.1 Source → model

| Source | Model | Notes |
| --- | --- | --- |
| `datasets/sections.json` | `Section` (Id, DisplayName, Grade, SectionName, CurriculumKey, Capacity) | 41 sections; ids validated (`^[a-z0-9-]+$`), duplicates rejected |
| `datasets/bell-schedule.json` | `BellSchedule` → `DaySchedule` (periods, breaks, **slot pairs**, morning periods) | Mon–Thu 8 + Fri–Sat 10 = 52; breaks never become variables (H4); pairs (1-2)(3-4)(5-6)(7-8)(9-10) per B2 |
| `CLASS_WISE_SUBJECTS.md` | `ClassCurriculum` → `CurriculumItem` (name, Subject/ActivityGroup, ppd, ppw) | Applied to every section of the grade; all 12 classes = 52 ✓ |
| `TEACHER_CLASS_ALLOCATION.xlsx` / DUMMY | `AssignmentRow` (section, subject, teacher code/name, workload, status, origin) | Primary assignment source (T1/T2) |
| `TEACHER_CLASS_ASSIGNMENTS.md` | roster (`TeacherInfo`), class-level assignments | Roster cross-check; input of the DUMMY generator |
| `datasets/scheduling-rules.json` | rule tiers (`RuleCatalog` in the report) | Constraints are implemented in code; the file drives the coverage table |
| `config/teacher-availability.json` | `TeacherAvailability` (H2) | Client table is empty → file empty |
| `config/scheduling-overrides.json` | `DayRestriction` (H6), `FixedSlotRule` (H9) | Client provides none → empty; hooks tested |
| `config/subject-aliases.json` | `SubjectAlias` | **Assumption file**: `History & Civics` → `History`, `English` → `English Language` + `English Literature`, … every use reported |

Normalization result: one **`LessonItem` per (section, curriculum item)** with demand from the curriculum (H8), the
teacher from the formal assignment row (T2), and the allowed cells after H2/H6/H9/G1. Kinds:

* `Subject` — has a formal teacher; books the teacher (H1).
* `Activity` — activity group; instructors listed, **not booked** (T6/AC8).
* `ReservedUnstaffed` — curriculum subject with no schedulable teacher (UNASSIGNED-TT, zero workload, or no row). Its
  periods are reserved with **no lesson and no teacher** (DATA-1), so the week still has 52 filled cells
  (H3/H8) and the slots are ready once a teacher is assigned. Use `--unstaffed Exclude` to drop them instead.

### 5.2 Replacing the DUMMY with the real workbook

1. Copy the client file to `data/client/TEACHER_CLASS_ALLOCATION.xlsx`.
2. Run `generate --assignments data/client/TEACHER_CLASS_ALLOCATION.xlsx` (or change `AssignmentsWorkbook` in
   `config/timetable.settings.json` / `Timetable:AssignmentsWorkbook` for the API).
3. Read `dataConflicts`. The loader matches headers flexibly (`AssignmentWorkbookLoader.HeaderAliases`: e.g. *Teacher
   Code / Employee Code / Emp Code*, *Periods / Week / workload_per_week*). If the real sheet uses yet another
   header, add it there; nothing else changes. If the sheet is not called *Section Assignments*, the first sheet with
   the required columns is used and `SHEET_NAME_MAPPED` is reported.
4. Check `summary.assignmentRows` = 587, `unassignedPlaceholderRows` = 92 and `zeroWorkloadRows` = 142 against the brief.

### 5.3 DUMMY workbook: how it is built

`generate-dummy-assignments` writes it from the class-level markdown. It invents no teachers and no subjects:

1. Each class-level row (teacher, class, subject) becomes rows for that class's sections (`sections.json`).
2. Several teachers on the same class + subject → sections are spread round-robin (Lily → Lavender → Lotus → Marigold,
   like engine rule T3).
3. The class workload is split evenly over the teacher's sections; 0 stays 0.
4. `UNASSIGNED-TT` rows are repeated for every section of the class (the markdown has no section detail).

Every row has `Data Origin = "DUMMY - synthesized … NOT client data"`, and the first sheet is a warning.
The result is 539 rows / 96 placeholders / 120 zero-workload, **not** the brief's 587 / 92 / 142. That is expected
for a reconstruction.

## 6. The CP-SAT model (`Constraints/TimetableModelBuilder.cs`)

**Sets.** Items *i* (section × curriculum item, teacher fixed), days *d*, teaching periods *p* of day *d*, slot pairs *k*.

**Variables** (Boolean). Interval variables were rejected: lessons are unit-length cells, and every rule is about
cells, pairs and days.

| Variable | Meaning | Created when |
| --- | --- | --- |
| `x[i,d,p]` | item *i* is taught on day *d*, period *p* | only for allowed cells → H2, H4, H6, H9, G1 are **structural** |
| `y[i,d,k]` | block item *i* occupies the whole slot pair *k* (B1/B2) | block items, both cells allowed |
| `z[i,d,p]` | block item *i* appears as a **single** in *p* | only in profile `pragmatic` / diagnostic (each carries a penalty) |
| `u[i,d]` | item *i* is used on day *d* | for L1 / G2 groups |

Linking for block items: `x[i,d,p] = Σ y[i,d,k ∋ p] + z[i,d,p]`. With no *z* (strict), a block item exists only as full pairs.

**Why one joint model.** Teachers are shared across sections, and the H1 AtMostOne constraints over all sections are
what couples them. Solving section by section can paint itself into a corner. The joint model cannot, and it can prove infeasibility.

### 6.1 Rule → constraint map (AC-8; `Timetable-Engine-Rules.md` IDs)

| Rule | Status | CP-SAT encoding |
| --- | --- | --- |
| **H1** no teacher double-booking | Hard | `AddAtMostOne(x[i,d,p] for all items of teacher t)` per (t, d, p), across sections |
| **H2** teacher off-days / unavailable | Hard (no data) | no `x` for blocked cells (`config/teacher-availability.json`) |
| **H3** one lesson per section slot | Hard | `AddExactlyOne` per (section, d, p) when the section demand is 52 (AtMostOne otherwise) |
| **H4** no breaks | Hard, structural | variables exist only for teaching periods |
| **H5** block never a lone single | Hard (strict) / soft (pragmatic) | no `z` (strict) · `z` penalised 300 (pragmatic); `za + zb ≤ 1` per pair keeps the count exact; parity cut `Σz ≥ 1` for odd ppw |
| **H6** day restrictions | Hard (no data) | no `x` outside allowed days |
| **H7** daily max | Hard | `Σ_p x[i,d,p] ≤ ppd` |
| **H8** weekly totals | Hard | `Σ_{d,p} x[i,d,p] = ppw` (from the curriculum) |
| **H9** activity fixed slots | Hard (no data) | activity `x` only in fixed cells (AC1 exact, AC2 whole day) |
| **H10** zero workload | Hard | ppw ≤ 0 items get no variables; zero-workload rows excluded and reported |
| **G1** Games/Library not in first pair | Hard, structural | no `x` in periods 1–2 for any Games/Library item (incl. "Games / Library") |
| **G2** Class 11/12 Games & Library not same day | Hard | `Σ u[i,d] ≤ 1` over the Games-family items per day |
| **L1** same first word not same day | Hard (soft only for Class 11/12 in pragmatic) | `Σ u[i,d] ≤ 1` per first-word group per day |
| **L2** no break crossing | Hard | `x[last-before-break] + x[first-after-break] ≤ 1` |
| **L3** max 3 consecutive | Hard | window of 4 within a break-free segment `≤ 3` (implied by H7 when ppd ≤ 3) |
| **T2** formal teacher only | Hard, structural | the teacher is data on the item; the solver never chooses teachers |
| **DATA-1 / T4** no UNASSIGNED-TT lessons | Hard, structural | placeholder rows never become `Subject` items; reported in `dataConflicts` |
| **B1/B2** consecutive non-break pairs | Hard | `y` only for fixed pairs from the bell schedule |
| **B3** single pairing | Not modelled | placement heuristic of the JS engine; H3 makes CP-SAT fill pairs directly |
| **B3b** strict ppd/ppw caps | Hard | H7/H8 are caps/equalities on every path; there is no force-fill |
| **B4** no same-first-word singles in one pair | Hard (implied by L1) | the validator checks it explicitly |
| **T6 / AC8** activity instructors not booked | Applied | activity items are excluded from H1 |
| **P4** pre-primary excluded | Applied | 11 sections excluded and reported (no curriculum exists for them either) |
| **R1–R2** rooms | Skipped, documented | no labs, `room_type_id` null, 9 rooms for 41 sections; production passes `rooms: []` too (R5) |
| **M1–M6** merged sections | Not implemented | no merge rules in the dataset |
| **SE/SA/ST/AT** substitution | Out of scope | deferred epic |
| **FF** force-fill | Replaced | exact solve + explicit relaxations + diagnostic solve |

The same table is emitted in every report under `ruleCoverage` (built from the active options).

### 6.2 Infeasibility explanation (AC-7)

CP-SAT only says `INFEASIBLE`; it does not explain why. So the explanation is produced by separate logic:

1. **`PreSolveAnalyzer`.** Counting arguments that are *necessary conditions*, so each finding is a proof:
   item capacity (H7/H8), block parity (H5/B1/H8), unpairable singles (H5), L1 day capacity, G2 day capacity, and
   teacher overload (H1: *"Teacher X: 36 periods required … but max capacity is 30"*). Teachers within 2 slots of
   capacity get a non-blocking `TEACHER_NEAR_CAPACITY`. If any proof fires, the strict solve is skipped.
2. **CP-SAT** may still prove infeasibility through rule interactions → `SOLVER_INFEASIBLE`.
3. **Diagnostic solve.** H1/L1/G2/H5 become heavily penalised soft rules, and the result lists *where* they break.
   It is never reported as success. When its status is not OPTIMAL, the report says the counts are an upper bound.

### 6.3 Soft constraints (Tier 3)

| Rule | Weight | Encoding |
| --- | --- | --- |
| PR-MATH | 10 per period | any Mathematics period not in periods 1–4 of Mon–Thu |
| DB max one Games / day | 8 per excess period | exact excess `max(0, Σ − 1)` |
| DB max one Library / day | 8 per excess period | same |
| DB max two consecutive Games | 8 per 3-window | exact excess over every 3-period window |

Note the conflict: a 2-period "Games / Library" block placed as a pair (B1) always counts 2 Games and 2 Library
periods that day. `scheduling-rules.json` lists these DB rules as soft, so they stay soft here. Excess variables
use `AddMaxEquality`, so `objectiveValue` equals the true penalty even when the search stops early. The validator
recomputes it independently (`validation.objectiveMatchesIndependentCheck`).

### 6.4 Search: warm start

A cold full-school solve finds valid but poor solutions (objective 83,408 after 180 s in an earlier run). The warm
start (`Solver/WarmStartBuilder.cs`) solves sections **one at a time**, using the same model builder:

* the most teacher-constrained section goes first;
* after each section, the cells its teachers occupy are blocked for later sections.

The combined timetable is passed to the joint model as a **solution hint** (`AddHint`, `repair_hint`). The joint
model still decides, and the warm start uses at most 50 % of the time limit. In the verified run it built a complete
valid timetable in 66 s, and the joint solve returned objective 27,092. If the joint search returned nothing,
the complete warm-start timetable would be used (`solver.solutionSource = "WARM_START"`). It is validated like any other result.
Disable it with `--no-warm-start`.

## 7. Rule profiles

| | `strict` (default) | `pragmatic` |
| --- | --- | --- |
| H5 lone block singles | forbidden | allowed, 300 each, minimised |
| L1 for Class 11/12 | hard | soft, 500 per extra item-day |
| every other Tier-1/Tier-2 rule | hard | hard |
| result on this dataset | INFEASIBLE (proven, explained) | complete, validated timetable |

The default is `strict` because relaxing a hard rule is the client's decision. `pragmatic` is opt-in, and its
relaxations are listed in `rulesApplied`, `ruleCoverage`, `assumptions` and `validation.relaxedRuleViolations`
(every lone single and every L1 overlap, by section).

## 8. Data gaps (all reported in `dataConflicts`, never silently dropped)

| Type | Meaning | Handling |
| --- | --- | --- |
| `UNASSIGNED_TEACHER` | row on `UNASSIGNED-TT` | never scheduled (DATA-1); the item is reserved/unstaffed |
| `ZERO_WORKLOAD` | `workload_per_week = 0` | excluded (H10); `--zero-workload TreatAsAssigned` for what-if |
| `MISSING_ASSIGNMENT` | curriculum subject without a schedulable teacher | reserved (or dropped with `--unstaffed Exclude`) |
| `ORPHAN_ASSIGNMENT` | assignment for a subject not in that class's curriculum (e.g. Dance, Music) | not schedulable |
| `WORKLOAD_MISMATCH` | `workload_per_week` ≠ curriculum ppw | curriculum wins (H8) |
| `SUBJECT_ALIAS_APPLIED` / `ALIAS_SUPERSEDED_BY_PLACEHOLDER` | name mapping used / refused because the client explicitly marked the item UNASSIGNED | info / warning |
| `PLACEHOLDER_SUPERSEDED`, `AMBIGUOUS_TEACHER` | real teacher and placeholder, or two real teachers, for one section-subject | real teacher used / first by workload, then code |
| `SECTION_EXCLUDED_PRE_PRIMARY`, `SECTION_EXCLUDED_NO_CURRICULUM`, `CURRICULUM_TOTAL_MISMATCH`, `SECTION_NOT_FOUND`, `INVALID_ROW` | scope and row validity | excluded + reported |
| `DUMMY_DATA_IN_USE`, `MISSING_INPUT_FILE`, `TEACHER_AVAILABILITY_EMPTY`, `ROOM_RULES_SKIPPED`, `ACTIVITY_INSTRUCTOR_NOT_BOOKED` | file-level facts | info |

## 9. Output

The shape follows the brief's *Expected Output*: `success`, `solver{engine,status,wallTimeMs,timeLimitSeconds,objectiveValue}`,
`summary{sectionsTotal,sectionsScheduled,totalSlotsScheduled,teachersInvolved,unassignedPlaceholderRows,zeroWorkloadRows,…}`,
`dataConflicts[]`, `sectionTimetables{section{DAY[{period,subject,teacherCode,teacherName}]}}`,
`teacherTimetables{teacher{DAY[{period,section,subject}]}}`, and `validation{hardConstraintsSatisfied,softConstraintPenalty,violatedSoftConstraints}`.

It adds: `outcome`, `dataSource`, `rulesApplied`, `schedulingConflicts`, `diagnostic`, `ruleCoverage` and
`assumptions`. Section lessons also carry `slot`, `start`, `end`, `type` (`SUBJECT`/`ACTIVITY`/`RESERVED_UNSTAFFED`),
`instructors` and `note`. `totalSlotsScheduled` counts real lessons only; `reservedUnstaffedSlots` and
`totalCellsFilled` (= 52 × sections) are reported separately, so reserved cells are never passed off as lessons.

Samples: `output/samples/timetable-pragmatic.json` and `output/samples/timetable-strict.json`.

## 10. Security

* **Input validation.** Every file is size-capped (JSON/markdown 5 MB, xlsx 25 MB) and strictly parsed; ids, days,
  periods and enum values are validated; strings are length-bounded. Malformed rows become `dataConflicts`, and
  unusable files raise `DataInputException` with a safe message that never echoes file content.
* **Safe file handling.** All paths are resolved inside an allowed root (no traversal); symlinks are refused;
  writes are atomic (temp + move); the dummy file name must contain `DUMMY`. The .xlsx reader caps entry
  count, size and compression ratio (zip bombs) and prohibits DTDs (XXE). The API never accepts paths from clients.
* **Secrets.** There are none in the repository. The API key comes from environment variables, user-secrets
  (Development), or Azure Key Vault (`-p:EnableKeyVault=true`, `KeyVault__Uri`, `DefaultAzureCredential` /
  managed identity). The API **fails closed**: it will not start without a key of at least 32 characters.
* **API.** `X-Api-Key` header, compared as SHA-256 digests in constant time. Every endpoint except `/health`
  requires it (fallback authorization policy). Per-IP fixed-window rate limiting (60/min); a 16 KB request body
  limit; bounded solver parameters (time limit ≤ 600 s, workers ≤ 16); one solve at a time (`409` otherwise).
  Responses carry HSTS (non-Development), `nosniff`, `X-Frame-Options: DENY`, CSP `default-src 'none'`,
  `no-store`, and no `Server` header. Errors are ProblemDetails without stack traces (input errors → 422).
* **Logging.** No secrets and no file contents are logged; jobs are logged by id.

## 11. Assumptions and missing requirements

Missing inputs: `TEACHER_CLASS_ALLOCATION.xlsx` (DUMMY used, A1) and `Timetable-Engine-Rules-Coverage.md` (tiers
taken from `scheduling-rules.json` + `Timetable-Engine-Rules.md`; reported as `MISSING_INPUT_FILE`).

| # | Assumption (each is also printed in the report) |
| --- | --- |
| A1 | DUMMY section-level assignments synthesized from class-level data ([§5.3](#53-dummy-workbook-how-it-is-built)) |
| A2 | Weekly demand = curriculum ppw (H8); `workload_per_week` only identifies zero-workload rows and mismatches |
| A3 | Zero-workload rows are excluded (H10) |
| A4 | Unstaffed curriculum items keep reserved, teacherless slots |
| A5 | Activity groups are scheduled per section without teacher booking (T6/AC8); option rows (Karate, Robotics…) are shown as instructors |
| A6 | `teacher_availability` is empty → everyone is available; no day restrictions / fixed slots exist |
| A7 | Pre-primary excluded (P4; no curriculum). Realistic scope: **30 sections × 52 = 1,560**, not 2,132 |
| A8 | Slot pairs per B2 on the real bell schedule; G1's first pair = periods 1–2 |
| A9 | L1 first word = first whitespace token, case-insensitive ("Hindi" / "Hindi Vyakaran" share a word) |
| A10 | G2 treats Games, Library and "Games / Library" as one family |
| A11 | PR-MATH penalises Fri/Sat Mathematics too (morning = periods 1–4 Mon–Thu only) |
| A12 | Teacher timetables list booked subject lessons only |
| A13 | Subject aliases (`config/subject-aliases.json`) are assumptions; every use is reported |
| A14/A15 | Profile `pragmatic`: H5 soft, L1 soft for Class 11/12 ([§7](#7-rule-profiles)) |

Open questions for the client: confirm A1 with the real workbook; whether H5 and L1 may be relaxed as in
`pragmatic`; zero-workload rows of sole subject teachers (Mathematics 9–12, Chemistry, Biology, Computer 7–12, Business
Studies 11–12); whether a combined "English" assignment covers both English Language and English Literature; and Pre-Primary
curriculum.

## 12. Limitations and future work

* Results use DUMMY assignments; numbers will change with the real workbook.
* Pragmatic optimality is not proven (FEASIBLE). Counting bounds show the H5/L1 parts are at their minimum; the
  remaining gap is in PR-MATH/Games. More cores or time help.
* Cancellation is checked between phases. A running CP-SAT search stops at its time limit, not on Ctrl+C.
* Rooms (R1/R2), merged sections (M1–M6) and multi-profile bell merging beyond Mon–Thu/Fri–Sat are not implemented
  (no data). The H2/H6/H9 hooks are implemented and tested but have no client data.
* The API keeps jobs in memory (one instance). For several instances, store reports in a database or blob storage.

## 13. Acceptance criteria

| # | Criterion | Status |
| --- | --- | --- |
| AC-1 | CP-SAT with a separate constraint module | ✅ `Constraints/TimetableModelBuilder.cs`, `Solver/` |
| AC-2 | Loads Excel + JSON + markdown | ✅ (Excel = DUMMY until the client file arrives; the loader is tested with alternative headers) |
| AC-3 | All schedulable sections | ✅ 30/30 in-scope sections (pragmatic). The 11 pre-primary sections are excluded and documented (P4, no curriculum) |
| AC-4 | H1, H3, H4, H7, H8, G1, G2, L1 in CP-SAT | ✅ all hard (L1 soft only for Class 11/12 in opt-in `pragmatic`, because it is provably impossible there) |
| AC-5 | H5, B1 for ppd ≥ 2 | ✅ hard in `strict` (proves infeasibility); B1 hard and H5 minimised in `pragmatic` |
| AC-6 | No double-booking; UNASSIGNED-TT reported | ✅ validator + JSON-level test; every placeholder row is in `dataConflicts` |
| AC-7 | INFEASIBLE with explanation | ✅ pre-solve proofs + diagnostic solve |
| AC-8 | README maps the model to Rule IDs | ✅ [§6.1](#61-rule--constraint-map-ac-8-timetable-engine-rulesmd-ids) + `ruleCoverage` in every report |
| AC-9 | Builds and runs from README; status + wall time reported | ✅ (verified offline, see `docs/VERIFICATION.md`) |
| Bonus | L2, L3, H9, PR-MATH soft objective | ✅ · rooms / merged sections ✗ (no data) |

## 14. Walkthrough (for the interview)

1. `validate-data`: show the dataset audit (`dataConflicts` counts) and the 82 strict proofs.
2. `TimetableModelBuilder`: `x/y/z/u` variables; H1 couples the sections; G1/H4 are structural.
3. Strict run: INFEASIBLE with reasons, and why (odd block counts, 7 days of English in a 6-day week).
4. Pragmatic run: what is relaxed, by how much (80 singles = minimum, 4 L1 overlaps = minimum), and how
   validation is independent of the solver.
5. Swapping in the real workbook, with no code change.
