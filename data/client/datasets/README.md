# Candidate Data Package — Full School Timetable

This folder contains the **real school dataset** for the timetable assessment. Load these files into your constraint model — do not invent teachers, subjects, or period counts.

---

## File Index

| File | Purpose |
| --- | --- |
| **`sections.json`** | All **41 active sections** with grade, section name, and curriculum key |
| **`bell-schedule.json`** | Bell schedule: teaching periods, breaks, Mon–Thu vs Fri–Sat |
| **`../Timetable-Engine-Rules.md`** | In this package: **`Timetable-Engine-Rules.md`** (folder root) — all production Rule IDs |
| **`../Timetable-Engine-Rules-Coverage.md`** | In this package: **`Timetable-Engine-Rules-Coverage.md`** (folder root) |
| **`scheduling-rules.json`** | Assessment subset mapped to engine Rule IDs — **use this for CP-SAT implementation** |
| **`CLASS_WISE_SUBJECTS.md`** | Subject/activity curriculum per grade (52 ppw per class) |
| **`TEACHER_CLASS_ASSIGNMENTS.md`** | Teacher roster and class-level assignments |
| **`TEACHER_CLASS_ALLOCATION.xlsx`** | **Primary source** for section-level teaching assignments (587 rows) |
| **`TIMETABLE_GENERATION_DATA_REQUIREMENTS.md`** | Full data dictionary, rooms, gaps, and DB context |

---

## Scale

| Metric | Value |
| --- | ---: |
| Active sections | 41 |
| Teaching slots per section / week | 52 |
| **Total slots to schedule** | **2,132** |
| Active teaching assignment rows | 587 |
| Teachers with assignments | 44 |
| Placeholder rows (`UNASSIGNED-TT`) | 92 |
| Grades with curriculum in `CLASS_WISE_SUBJECTS.md` | Class 1 – Class 12 |

---

## How to Build Your Input Model

### Step 1 — Sections

Load `sections.json`. Each section maps to a `curriculumKey` (e.g. `"Class 10"`).

### Step 2 — Subjects / curriculum per section

From `CLASS_WISE_SUBJECTS.md`, read the subject/activity list for each `curriculumKey`:

- `periodsPerWeek`, `periodsPerDay` (max per day)
- Subject vs activity type

Apply the **same curriculum** to every section sharing a grade (e.g. Grade 10 Lily and Lavender both use Class 10 curriculum).

Pre-Primary / KG sections: use assignment and curriculum sheets from `TEACHER_CLASS_ALLOCATION.xlsx` or document your assumption.

### Step 3 — Teachers and assignments

**Primary:** Load section-level rows from `TEACHER_CLASS_ALLOCATION.xlsx` (sheet: Section Assignments).

Each row should provide:

- Section
- Subject or activity
- Teacher (employee code + name)
- `workload_per_week` (periods)

**Secondary reference:** `TEACHER_CLASS_ASSIGNMENTS.md` for class-level summary and teacher roster.

Rules:

- Use only formal `teaching_assignments` — do not infer from `main_subject` fields.
- Rows with teacher `UNASSIGNED-TT` are **not schedulable** — report as conflicts.
- Rows with `workload_per_week = 0` must be reconciled or excluded with explanation.

### Step 4 — Bell schedule

Load `bell-schedule.json`:

- Mon–Thu: periods 1–8 teaching
- Fri–Sat: periods 1–10 teaching
- Break slots are **not assignable**

### Step 5 — Constraints

Load `scheduling-rules.json` and implement hard rules in your CP-SAT model.

---

## Data Relationship Diagram

```text
sections.json (41 sections)
    │
    ├── curriculumKey ──► CLASS_WISE_SUBJECTS.md (subjects + ppw)
    │
    └── section id ──► TEACHER_CLASS_ALLOCATION.xlsx (teacher per subject)
                              │
                              └── teacher ──► shared across sections (conflict constraint)

bell-schedule.json ──► valid (day, period) slots
scheduling-rules.json ──► hard / soft constraints
```

---

## Known Data Gaps (Handle Explicitly)

Your solver **must not silently ignore** these. Report them in output:

1. **92 UNASSIGNED-TT placeholders** — list affected section-subjects
2. **142 zero-workload rows** — list and explain handling
3. **Empty teacher availability** — state assumption (e.g. fully available)
4. **No Computer Lab / Science Lab rooms** — skip room-type rules or document
5. **41 sections vs 9 rooms** — room assignment optional for this assessment

See `TIMETABLE_GENERATION_DATA_REQUIREMENTS.md` section 11 for the full checklist.

---

## Suggested Normalized Model (Internal DTO)

```csharp
Section      { Id, DisplayName, Grade, CurriculumItems[] }
CurriculumItem { SubjectName, PeriodsPerWeek, MaxPeriodsPerDay, Type }
Assignment   { SectionId, SubjectName, TeacherId, PeriodsPerWeek }
Teacher      { Id, Code, Name, MaxPeriodsPerDay?, MaxPeriodsPerWeek? }
TimeSlot     { Day, Period, IsTeaching }
```

You may extend this. Document your schema in your README.

---

## Excel Workbook Sheets (Expected)

When you receive `TEACHER_CLASS_ALLOCATION.xlsx`, expect at minimum:

| Sheet | Content |
| --- | --- |
| Section Assignments | 587 section-level assignment rows |
| Teacher Roster | Employee codes and names |
| Class Subjects | Curriculum reference |
| Gaps | UNASSIGNED and zero-workload rows |

If sheet names differ, note the mapping in your README.
