# Developer Assessment: Full School Constraint-Based Timetable Solver

## Overview

This is a **technical assessment** for .NET developer candidates joining RDPL's timetable product work.

**Primary objective:** build a **full-school constraint-based timetable solver** using **Google OR-Tools (CP-SAT)** that reads the **real school dataset** we provide (teachers, classes/sections, subjects, bell schedule, assignments) and generates valid weekly timetables for **all 41 active sections**.

You are **not** building the complete product (UI, database, authentication, natural-language rules). Your deliverable is a **working constraint engine + data loader + solver output** for the full school.

---

## What We Want You to Build

```text
Full school dataset (Excel + JSON + markdown)
              ↓
        Data loader / normalizer
              ↓
   Constraint model (CpModel — variables + hard/soft rules)
              ↓
      Google OR-Tools CP-SAT solver
              ↓
  Timetables for 41 sections + teacher views + conflict report
```

You must demonstrate:

1. **Load** real teacher, section, subject, and assignment data from the provided files
2. **Model** the full scheduling problem as CP decision variables and constraints
3. **Solve** for all 41 sections together (shared teachers create cross-section conflicts)
4. **Report** valid timetables, solver status, and data/s scheduling conflicts clearly

---

## Time Limit

| Format | Duration |
| --- | --- |
| Take-home assignment | **5–7 working days** |
| On-site / extended live session | **2 days** |

Document partial progress honestly. A solver that handles all schedulable sections with clear conflict reporting is better than an incomplete UI.

---

## Technology Requirements

### Required

| Component | Requirement |
| --- | --- |
| Language | **C#** |
| Host | **ASP.NET Core Web API** *or* **Console application** |
| Constraint engine | **Google OR-Tools CP-SAT** (`Google.OrTools` NuGet) |
| Data input | Load from provided dataset files (see below) |

```bash
dotnet add package Google.OrTools
dotnet add package ClosedXML   # or similar — if reading Excel
```

### Not required

- Next.js / frontend
- PostgreSQL / database persistence
- Authentication, RLS, audit logs
- Natural-language constraint parser
- PDF/Excel export, drag-and-drop editing, background job queue

### Not allowed

- External timetable **SaaS APIs**
- Hard-coded timetables without a real CP model
- Silently skipping `UNASSIGNED-TT` rows or data gaps

---

## Problem Scope — Full School

| Metric | Value |
| --- | ---: |
| Active sections | **41** |
| Periods per section / week | 52 |
| **Total teaching slots** | **2,132** |
| Teaching assignment rows | 587 |
| Teachers with assignments | 44 |
| Placeholder (`UNASSIGNED-TT`) rows | 92 |

### Working week

| Days | Teaching periods / day | Subtotal |
| --- | ---: | ---: |
| Monday – Thursday | 8 | 32 |
| Friday – Saturday | 10 | 20 |
| **Per section** | | **52** |

Every section must receive exactly **52 teaching periods per week** across its curriculum subjects and activities.

---

## Provided Dataset (You Will Receive All of These)

| File | Contents |
| --- | --- |
| `candidate-data/sections.json` | All 41 sections with grade and curriculum key |
| `candidate-data/bell-schedule.json` | Bell schedule and valid teaching slots |
| `candidate-data/scheduling-rules.json` | Hard, soft, and optional constraints (**aligned to `Timetable-Engine-Rules.md`**) |
| `candidate-data/README.md` | How to combine the files into solver input |
| `Timetable-Engine-Rules.md` | **Authoritative production rule reference** (all Rule IDs) |
| `Timetable-Engine-Rules-Coverage.md` | Which rules are built-in / text / data / deferred |
| `CLASS_WISE_SUBJECTS.md` | Subject/activity requirements per grade (52 ppw) |
| `TEACHER_CLASS_ASSIGNMENTS.md` | Teacher roster and class-level assignments |
| `TEACHER_CLASS_ALLOCATION.xlsx` | **Section-level assignments** (587 rows — primary assignment source) |
| `TIMETABLE_GENERATION_DATA_REQUIREMENTS.md` | Rooms, data gaps, DB context, full constraint list |

**Start with `candidate-data/README.md`** — it explains how the files connect.

---

## Data Loading Requirements

Your application must:

1. **Parse** section list from `sections.json`
2. **Parse** curriculum (subjects, periods/week, max/day) from `CLASS_WISE_SUBJECTS.md` or normalized JSON you derive from it
3. **Parse** section-level teacher assignments from `TEACHER_CLASS_ALLOCATION.xlsx`
4. **Parse** bell schedule from `bell-schedule.json`
5. **Apply** constraints from `scheduling-rules.json` (mapped to `Timetable-Engine-Rules.md` Rule IDs)

### Teacher details

From assignments and roster files, load per teacher:

- Employee code and name
- Which section-subject pairs they teach
- Periods per week per assignment
- Shared assignments across sections (conflict source)

### Class / section details

From `sections.json`:

- Grade, section name, display name (e.g. `Grade 10 - Lily`)
- Curriculum key linking to subject requirements

### Subject details

From `CLASS_WISE_SUBJECTS.md`:

- Subject / activity name
- Periods per week and max periods per day
- Subject type (theory, activity, etc.)

Apply grade-level curriculum to **every section of that grade**.

---

## Constraint-Based Design (Core Requirement)

Separate your solution into:

```text
Data/           Load Excel, JSON, markdown → normalized model
Constraints/    Build CpModel (variables + rules)
Solver/         Run CpSolver, extract results
Reporting/      Class views, teacher views, conflicts, data gaps
```

### Tier 1 — Mandatory hard constraints (must enforce in CP-SAT)

Mapped to production engine Rule IDs in `Timetable-Engine-Rules.md`.

| ID | Engine | Rule |
| --- | --- | --- |
| H1 | H1 | No teacher double-booked across sections at same day/period |
| H2 | H2 | Teacher off-days / unavailable periods respected (assume all available if data empty) |
| H3 | H3 | One lesson per section per valid teaching slot |
| H4 | H4 | No lessons in break periods |
| H6 | H6 | Day restrictions honoured (`ONLY ON` specific days) |
| H7 | H7 | Daily max (`periodsPerDay`) not exceeded |
| H8 | H8 | Weekly curriculum totals met (52 ppw per section) |
| H9 | H9 | Activity `fixedSlots` respected when present in data |
| H10 | H10 | Zero-workload assignments excluded |
| G1 | G1 | Games and Library not in first slot pair (periods 1–2) |
| G2 | G2 | **Class 11 & 12:** Games and Library cannot both appear on the same day |
| L1 | L1 | **Same-first-word subjects** (e.g. English Language + English Literature) **cannot share a day** |
| L2 | L2 | Subject cannot span last slot before break AND first slot after break |
| L3 | L3 | Max 3 consecutive slots with the same subject (breaks interrupt count) |
| T2 | T2 | Only formally assigned teacher from `teaching_assignments` |
| DATA-1 | T4 | `UNASSIGNED-TT` rows must not produce scheduled lessons |

Full CP-SAT hints: `candidate-data/scheduling-rules.json` → `mandatoryHard`

### Tier 2 — Block & layout rules (required for production parity)

Subjects with `periodsPerDay >= 2` are **block subjects** in the production engine.

| ID | Engine | Rule |
| --- | --- | --- |
| H5 | H5 | Block subjects must not be placed as a lone single in a half-pair |
| B1 | B1 | Block subjects occupy two **consecutive** non-break slots |
| B3b | B3b | `periodsPerDay` and `periodsPerWeek` are strict caps — no over-placement |
| B4 | B4 | Two single-period subjects with same first word cannot share one slot-pair |

CP-SAT may model these via paired period variables instead of the JS engine's phased algorithm — document your approach in README.

Full definitions: `scheduling-rules.json` → `blockAndLayout`

### Tier 3 — Soft constraints (bonus — objective function)

| ID | Engine | Preference |
| --- | --- | --- |
| PR-MATH | text | Prefer Mathematics in morning (periods 1–4 Mon–Thu) |
| DB-RULE | school DB | At most one Games / Library per section per day |
| DB-RULE | school DB | Max two consecutive Games periods |

### Tier 4 — Optional (document if skipped)

| ID | Engine | Rule |
| --- | --- | --- |
| R1–R2 | R1–R2 | Room type mapping and no room double-booking (rooms empty in sample data) |
| M1–M6 | M1–M6 | Merged sections mirror master schedule |
| P4 | P4 | Pre-Primary sections excluded in production — document if you skip Nursery/KG |

### Out of scope (deferred per coverage map)

Substitution engine rules **SE1–SE8, SA1–SA8, ST1–ST2, AT1–AT7** — follow-up epic, not required.

Production note: activity instructors are **not teacher-booked** (T6, AC8) — whole-school activities run simultaneously. Document your handling.

---

## Handling Real-World Data Gaps

The dataset is **production-like, not perfectly clean**. Your solver must:

| Gap | Expected behaviour |
| --- | --- |
| 92 `UNASSIGNED-TT` rows | List in `dataConflicts[]`; do not schedule |
| 142 zero-workload rows | List and explain (exclude or fail with message) |
| Empty teacher availability | State assumption in README (e.g. fully available) |
| Missing lab rooms | Skip room-type hard rule or document assumption |

---

## Expected Output

```json
{
  "success": true,
  "solver": {
    "engine": "Google OR-Tools CP-SAT",
    "status": "FEASIBLE",
    "wallTimeMs": 185000,
    "timeLimitSeconds": 300,
    "objectiveValue": 142
  },
  "summary": {
    "sectionsTotal": 41,
    "sectionsScheduled": 41,
    "totalSlotsScheduled": 2132,
    "teachersInvolved": 44,
    "unassignedPlaceholderRows": 92,
    "zeroWorkloadRows": 142
  },
  "dataConflicts": [
    {
      "type": "UNASSIGNED_TEACHER",
      "section": "Grade 2 - Lily",
      "subject": "Mathematics",
      "message": "Assignment row uses UNASSIGNED-TT — cannot schedule until teacher assigned."
    }
  ],
  "sectionTimetables": {
    "Grade 10 - Lily": {
      "MONDAY": [
        { "period": 1, "subject": "Mathematics", "teacherCode": "RVS115", "teacherName": "Ashish Thakur" }
      ]
    }
  },
  "teacherTimetables": {
    "Ashish Thakur": {
      "MONDAY": [
        { "period": 1, "section": "Grade 8 - Lily", "subject": "Mathematics" },
        { "period": 3, "section": "Grade 10 - Lily", "subject": "Mathematics" }
      ]
    }
  },
  "validation": {
    "hardConstraintsSatisfied": true,
    "softConstraintPenalty": 142,
    "violatedSoftConstraints": []
  }
}
```

If infeasible:

```json
{
  "success": false,
  "solver": { "status": "INFEASIBLE" },
  "schedulingConflicts": [
    "Teacher RVS115 (Ashish Thakur): 36 periods required across Grade 8 and Grade 10 sections but max capacity is 30."
  ],
  "dataConflicts": []
}
```

Deliverables:

- JSON output for all schedulable sections
- Teacher-wise combined timetable
- `dataConflicts` report for bad input rows
- `schedulingConflicts` when the solver cannot find a feasible solution

---

## Suggested API

| Method | Endpoint | Purpose |
| --- | --- | --- |
| `POST` | `/api/timetable/load-data` | Load and validate dataset; return summary + conflicts |
| `POST` | `/api/timetable/generate` | Build model, run solver, return timetables |
| `GET` | `/api/timetable/sections` | All section timetables |
| `GET` | `/api/timetable/sections/{id}` | One section |
| `GET` | `/api/timetable/teachers` | All teacher timetables |
| `GET` | `/api/timetable/conflicts` | Data gaps + scheduling conflicts |

Console-only submission is acceptable if documented in README.

---

## Acceptance Criteria

| # | Criterion |
| --- | --- |
| AC-1 | Uses **Google OR-Tools CP-SAT** with a separate constraint model module |
| AC-2 | Loads data from provided files (Excel + JSON + curriculum markdown) |
| AC-3 | Generates timetables for **all schedulable sections** (target: 41; exclude only rows with documented data conflicts) |
| AC-4 | Tier 1 hard constraints **H1, H3, H4, H7, H8, G1, G2, L1** enforced in CP-SAT |
| AC-5 | Tier 2 block rules **H5, B1** enforced for `periodsPerDay >= 2` subjects |
| AC-6 | No teacher double-booking; `UNASSIGNED-TT` reported in `dataConflicts` |
| AC-7 | Solver returns `INFEASIBLE` with explanation when data/rules impossible |
| AC-8 | README maps constraint model to **`Timetable-Engine-Rules.md`** Rule IDs |
| AC-9 | Application builds and runs from README steps; solver status + wall time reported |

**Bonus:** L2, L3, H9 fixed slots, soft objective (PR-MATH), room rules, merged sections.

---

## What We Are Evaluating

| Area | Weight | Focus |
| --- | ---: | --- |
| Constraint modeling (OR-Tools) | High | Variables, hard rules in CpModel, cross-section teacher conflicts |
| Full data integration | High | Excel + JSON + curriculum correctly normalized |
| Correctness at scale | High | 41 sections, 2,132 slots, shared teachers |
| Data gap handling | Medium | UNASSIGNED-TT, zero workload — reported not ignored |
| C# quality & structure | Medium | Data / Constraints / Solver / Reporting separation |
| Communication | Medium | README and interview walkthrough |

---

## Submission

Submit a **ZIP** or **Git repository** with:

1. Source code (data loader + constraint model + solver)
2. `README.md` (setup, data mapping, constraint design, assumptions, limitations)
3. Sample **full-run output JSON** (or summary if file is large)
4. *(Optional)* Tests, script to run generation

Be prepared to **walk through your CpModel** and explain how you loaded the Excel assignments in the interview.


Good luck.
