# Timetable Generation — Data Requirements

| Field | Value |
| --- | --- |
| Primary source | `RIMS_DB_17_Backup_18_07.sql` |
| Reference source | `RIMS_DB_14_Backup_18_08.sql` (schema and bell schedule) |
| Database | PostgreSQL |
| School ID | `d1f65e04-2dc0-48ce-9b1a-fdbfd980205f` |
| Tenant ID | `10b62a9e-9124-4f3b-a530-73e2047196ec` |
| Academic year | **2026-27** (`0b68525b-427a-437d-b5ef-efc7497e6f33`) |
| Generated | 18 August 2026 |

---

## Contents

1. [Executive readiness](#1-executive-readiness-summary)
2. [Assignment status](#2-assignment-and-curriculum-status)
3. [Bell schedule capacity](#5-bell-schedule)
4. [Infrastructure gaps](#6-classroom-and-resource-details)
5. [Scheduling rules](#8-scheduling-constraints)
6. [Data model](#9-timetable-generation-data-model)
7. [Completion checklist](#11-required-completion-checklist)
8. [Related outputs](#10-related-documents-and-exports)

---

## 1. Executive readiness summary

The schema supports full timetable generation, but **operational data is incomplete**.

| Area | Status | Detail |
| --- | --- | --- |
| Teaching assignments | **Partial** | 495 active rows across 44 teachers; 92 placeholder rows on `UNASSIGNED-TT` |
| Class teachers | **Missing** | 0 sections with `is_class_teacher = true` |
| Section classrooms | **Missing** | No section linked to a home classroom |
| Teacher availability | **Empty** | `teacher_availability` has no rows |
| Generated timetables | **Empty** | `weekly_timetables` has no rows |
| Curriculum load | **Partial** | Classes 1–5 and 9–12 in `grade_subject_allocations`; Classes 6–8 supplemented from `grade_subjects` |
| Weekly period target | **Aligned** | Bell schedule provides **52** teaching slots/week; all classes normalized to 52 ppw in `CLASS_WISE_SUBJECTS.md` |
| Rooms | **Insufficient** | 41 sections vs 9 generic classrooms; no specialist labs |

**Verdict:** Assignments and curriculum must be reconciled, placeholders reassigned, and infrastructure data completed before production generation.

---

## 2. Assignment and curriculum status

### 2.1 Teaching assignments (DB 17)

| Metric | Value |
| --- | ---: |
| Total active assignment rows | 587 |
| Assigned to real teachers | 495 |
| Placeholder (`UNASSIGNED-TT`) | 92 |
| Distinct teachers with assignments | 44 |
| Active teaching employees (excl. placeholder) | 56 |
| Teaching staff without assignment | 12 |
| Zero-workload rows | 142 |

Formal allocations live in `public.teaching_assignments`. Do **not** infer assignments from `employees.main_subject` or `secondary_subjects`.

### 2.2 Curriculum load

Per-class subject and activity requirements (52 ppw, subjects-only normalization) are documented in **`CLASS_WISE_SUBJECTS.md`**.

Activity periods are fixed and must not be increased during load balancing.

### 2.3 Key gaps before generation

- Reassign all **92** `NEEDS_TEACHER_ASSIGNMENT` placeholder rows.
- Reconcile assignment `workload_per_week` with section curriculum demand.
- Populate class teachers, home classrooms, and teacher availability.
- Resolve scheduling rules that reference missing room types (`Computer Lab`, `Science Lab`).

---

## 4. Class and section details

### 4.1 Current school classes

| Academic level | Class | Sections | Section count | Capacity per section |
|---|---|---|---:|---:|
| Pre-Primary | Pre Nursery | Lily | 1 | 35 |
| Pre-Primary | Nursery | Lily, Lavender, Lotus, Marigold | 4 | 35 |
| Pre-Primary | Junior KG | Lily, Lavender, Lotus | 3 | 35 |
| Pre-Primary | Senior KG | Lily, Lavender, Lotus | 3 | 35 |
| Primary | Grade 1 | Lily, Lavender, Lotus, Marigold | 4 | 40 |
| Primary | Grade 2 | Lily, Lavender, Lotus, Marigold | 4 | 40 |
| Primary | Grade 3 | Lily, Lavender, Lotus, Marigold | 4 | 40 |
| Primary | Grade 4 | Lily, Lavender, Lotus | 3 | 40 |
| Primary | Grade 5 | Lily, Lavender, Lotus | 3 | 40 |
| Secondary | Grade 6 | Lily, Lavender | 2 | 40 |
| Secondary | Grade 7 | Lily | 1 | 50 |
| Secondary | Grade 8 | Lily | 1 | 50 |
| Secondary | Grade 9 | Lily, Lavender | 2 | 40 |
| Secondary | Grade 10 | Lily, Lavender | 2 | 40 |
| Sr. Secondary | Grade 11 | Lily, Lavender | 2 | 40 |
| Sr. Secondary | Grade 12 | Lily, Lavender | 2 | 40 |

Total active sections: **41**.

### 4.2 Class data gaps

- `grade_sections.class_teacher_id` is null for every listed section.
- `grade_sections.classroom_id` is null for every listed section.
- `stream_name` is not populated, including Grades 11 and 12.
- Student strength is not stored on the section; only planned capacity exists.
- No section-specific working-day configuration exists.

## 5. Bell schedule

### 5.1 Selected profile

- Period group: `Schedule-1`
- Period group ID: `8f87e6e0-cc37-4961-8381-61d9783a6afc`
- Profile status: active
- Description: Bell Schedule Profile
- Assigned to the school's sections through `assigned_section_ids`
- Day numbering is inferred as 1 = Monday through 6 = Saturday.

### 5.2 Monday to Thursday

| Order | Slot | Start | End | Type |
|---:|---|---|---|---|
| 1 | 1A | 07:45 | 08:25 | Teaching |
| 2 | 1B | 08:25 | 09:05 | Teaching |
| 3 | Short Break | 09:05 | 09:15 | Break |
| 4 | 2A | 09:15 | 09:55 | Teaching |
| 5 | 2B | 09:55 | 10:35 | Teaching |
| 6 | Lunch Break | 10:35 | 11:05 | Break |
| 7 | 3A | 11:05 | 11:40 | Teaching |
| 8 | 3B | 11:40 | 12:15 | Teaching |
| 9 | Short Break | 12:15 | 12:20 | Break |
| 10 | 4A | 12:20 | 12:55 | Teaching |
| 11 | 4B | 12:55 | 13:30 | Teaching |

Teaching periods per day: **8**.

### 5.3 Friday and Saturday

| Order | Slot | Start | End | Type |
|---:|---|---|---|---|
| 1 | 1A | 07:45 | 08:15 | Teaching |
| 2 | 1B | 08:15 | 08:45 | Teaching |
| 3 | Short Break | 08:45 | 08:55 | Break |
| 4 | 2A | 08:55 | 09:30 | Teaching |
| 5 | 2B | 09:30 | 10:00 | Teaching |
| 6 | Lunch Break | 10:00 | 10:30 | Break |
| 7 | 3A | 10:30 | 11:00 | Teaching |
| 8 | 3B | 11:00 | 11:30 | Teaching |
| 9 | 4A | 11:30 | 12:00 | Teaching |
| 10 | 4B | 12:00 | 12:30 | Teaching |
| 11 | 5A | 12:30 | 13:00 | Teaching |
| 12 | 5B | 13:00 | 13:30 | Teaching |

Teaching periods per day: **10**.

### 5.4 Weekly capacity

If all six days are working days:

- Monday–Thursday: 4 × 8 = 32 teaching periods
- Friday–Saturday: 2 × 10 = 20 teaching periods
- Total available teaching slots per section: **52 per week**

This capacity must be reconciled against
`grade_subject_allocations.periods_per_week` before generation.

## 6. Classroom and resource details

Nine active, non-deleted rooms are available for the current school:

| Code | Name | Building | Floor | Capacity |
|---|---|---|---:|---:|
| B01 | Classroom | Main Building | 2 | 45 |
| B02 | Classroom | Main Building | 2 | 45 |
| B03 | Classroom | Main Building | 2 | 45 |
| B04 | Classroom | Main Building | 2 | 45 |
| B05 | Classroom | Main Building | 2 | 45 |
| B06 | Classroom | Main Building | 2 | 45 |
| B07 | Classroom | Main Building | 2 | 45 |
| B08 | Classroom | Main Building | 2 | 45 |
| B09 | Classroom | Main Building | 2 | 45 |

Resource limitations:

- 41 sections exist but only 9 active rooms are available.
- no active specialist lab is available;
- `room_type_id` is null for all active rooms;
- projector, smart-board, and air-conditioning flags are false;
- no section is linked to a home classroom;
- rules requiring Computer Lab or Science Lab cannot currently be satisfied.

## 7. Subjects, curriculum load, and activities

### 7.1 Subject master

The current school contains 38 active subject records, including:

- languages: English Language I/II, English Literature I/II, Hindi, Sanskrit,
  French and Communication;
- mathematics/science: Mathematics I/II, Environmental Science, Science,
  Physics I/II, Chemistry I/II and Biology I/II;
- social science: Social Studies, History & Civics and Geography;
- commerce: Economics I/II, Commercial Studies, Accountancy, Business
  Studies, Commerce and Entrepreneurship;
- computer/technology: Computer Studies, Computer Applications, Computer
  Science and Robotics;
- other: Abacus, Physical Education I/II, Arts Education, G.K. & Moral, and
  SUPW & Community Service.

The subject master only defines subjects. Per-section weekly demand must come
from `grade_subject_allocations`.

### 7.2 Allocation rules

Each active `grade_subject_allocations` row defines:

- academic year;
- grade and section;
- either a subject or an activity group;
- maximum periods per day;
- required periods per week.

The row-level check permits exactly one of `subject_id` or
`activity_group_id`.

### 7.3 Activities

Active activities include Karate, Football, Painting, Football 1, Cricket,
Library, Gymnastics and Skating.

Active activity groups include:

- Arts Activity, containing Painting;
- Physical Activity, containing Cricket, Football 1 and Karate 1.

`activity_groups.fixed_slots` can force an activity group to particular days
and periods. A fixed-slot record with a null `periodId` is incomplete and must
be resolved before generation.

## 8. Scheduling constraints

### 8.1 Mandatory hard constraints

A valid generated timetable must enforce:

1. one lesson per section in a teaching slot;
2. no teacher double-booking;
3. no classroom double-booking;
4. teacher must match the formal `teaching_assignment`;
5. lesson count must satisfy the section's weekly subject/activity allocation;
6. lesson count per day must not exceed `periods_per_day`;
7. no lesson in a break period;
8. teacher must be available for the day and period;
9. classroom capacity must be sufficient;
10. room type/equipment requirements must be satisfied;
11. activity fixed slots must be respected;
12. inactive or soft-deleted records must be excluded;
13. tenant, school, and academic-year IDs must match across every join.

### 8.2 Rules present for the current school/current year

- never place Games in the first pair;
- never place Library in the first pair;
- at most one Games period per day;
- at most one Library period per day;
- maximum two consecutive Games periods;
- require Computer in a Computer Lab;
- require Science in a Science Lab;
- subjects sharing the same first word cannot be placed on the same day;
- prefer Mathematics in the morning.

Severity `2` is used for hard rules and severity `1` for preferences in the
stored examples.

### 8.3 Rule problems

- The current subject names are `Computer Studies`, `Computer Applications`,
  and `Computer Science`, while one rule targets `Computer`.
- A room rule targets `Science Lab`, but no active room has that type.
- A room rule targets `Computer Lab`, but no active room has that type.
- The “same first word” rule may prevent valid pairs such as English Language
  and English Literature on the same day.
- Rules from older academic years must not be mixed into the current run.
- A previously parsed rule for “Class 11 and Class 12” had partial parse status
  because class names did not match the configured names.

## 9. Timetable-generation data model

### 9.1 Required input tables

| Purpose | Primary table(s) |
|---|---|
| School and academic scope | `registered_schools`, `academic_years` |
| Academic hierarchy | `academic_levels`, `academic_grades`, `grade_sections` |
| Teachers and personal details | `employees`, `departments`, `designations`, `lookup_values` |
| Teacher-to-class/subject mapping | `teaching_assignments` |
| Subject master | `subjects` |
| Required weekly curriculum load | `grade_subject_allocations` |
| Bell schedule | `period_groups`, `period_group_periods`, `periods` |
| Teacher availability | `teacher_availability` |
| Rooms and labs | `classrooms`, `buildings`, `floors`, `lookup_values` |
| Activities and fixed slots | `activities`, `activity_categories`, `activity_groups` |
| Constraints | `timetable_rule_sets`, `timetable_text_rules`, `scheduling_rules` |
| Calendar exclusions | `academic_calendar_events`, `holidays`, `timetable_exceptions` |
| Generated version | `timetable_versions`, `weekly_timetables` |
| Audit/publishing | `timetable_change_logs`, `timetable_publish_history` |

### 9.2 Core relationship

```text
academic_years
  ├─ academic_grades ─ grade_sections
  ├─ grade_subject_allocations ─ subjects/activity_groups
  ├─ teaching_assignments ─ employees + grade_sections + subjects/activities
  ├─ teacher_availability ─ employees + period_groups + periods
  └─ timetable_versions
       └─ weekly_timetables
            ├─ teaching_assignments
            ├─ period_groups/periods
            └─ classrooms
```

---

## 10. Related documents and exports

| Output | Description |
| --- | --- |
| `CLASS_WISE_SUBJECTS.md` | Class-level curriculum: subjects, activities, ppd/ppw (52 ppw normalized) |
| `TEACHER_CLASS_ASSIGNMENTS.md` | Class-level teacher roster and assignments with weekly load |
| `TEACHER_CLASS_ALLOCATION.xlsx` | Excel workbook: section-level assignments, roster, class subjects, gaps |

Section-level assignment detail (587 rows) is maintained in the Excel workbook rather than duplicated in this document.

---

## 11. Required completion checklist

Complete all items before running a production generator:

- [ ] Reassign all `UNASSIGNED-TT` placeholder allocations to real teachers.
- [ ] Ensure every section subject/activity has a formal teaching assignment.
- [ ] Reconcile assignment workload with `grade_subject_allocations` demand.
- [ ] Confirm 52 ppw curriculum load per class (see `CLASS_WISE_SUBJECTS.md`).
- [ ] Assign a class teacher to every section.
- [ ] Assign a home classroom to every section or define roomless scheduling.
- [ ] Add enough active rooms for simultaneous sections.
- [ ] Add and classify Computer Lab, Science Lab and other specialist rooms.
- [ ] Populate teacher availability/unavailability.
- [ ] Define maximum teacher workload per day and per week.
- [ ] Resolve activity fixed slots containing a null period.
- [ ] Review hard and soft scheduling rules for the current year.
- [ ] Normalize rule subject names to actual subject-master names.
- [ ] Populate streams/electives for Classes 11 and 12.
- [ ] Create a draft `timetable_versions` record before saving generated rows.

---

## 12. Final conclusion

The database schema is timetable-ready. The **DB 17 backup** contains substantial teaching-assignment data (587 active rows) but remains blocked by placeholder allocations, missing class teachers, empty availability, insufficient rooms, and unreconciled workloads. Use the three companion documents and the Excel export to complete data entry before generation.
