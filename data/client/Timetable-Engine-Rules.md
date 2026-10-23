# Timetable Engine — Complete Rules Reference

> **Source:** Derived from the actual implementation in `server/src/services/timetable/` (`generationEngine.js`, `substitutionEngine.js`, `substitutionService.js`, `workloadService.js`), related models, and `timetableController.js`.  
> **Purpose:** A single authoritative list of every rule, constraint, and scheduling policy enforced by the timetable generation and substitution engines **today (hardcoded)**.  
> **Production target:** Admins will configure IF/ELSE rules with AND/OR logic **before** generation — see [`Timetable-Scheduling-Rules-Builder.md`](./Timetable-Scheduling-Rules-Builder.md).

---

## Table of Contents

1. [Algorithm Overview](#1-algorithm-overview)
2. [Pre-Generation & Input Rules](#2-pre-generation--input-rules)
3. [Teacher Assignment Rules](#3-teacher-assignment-rules)
4. [Hard Constraints (Never Violated in Normal Phases)](#4-hard-constraints-never-violated-in-normal-phases)
5. [Block & Pairing Rules](#5-block--pairing-rules)
6. [Subject Layout Rules](#6-subject-layout-rules)
7. [Games & Library Rules](#7-games--library-rules)
8. [Activity (Curricular) Rules](#8-activity-curricular-rules)
9. [Room Allocation Rules](#9-room-allocation-rules)
10. [Day & Quota Rules](#10-day--quota-rules)
11. [Shared-Teacher & Section Interleaving Rules](#11-shared-teacher--section-interleaving-rules)
12. [Scheduling Phase Order & What Each Phase Does](#12-scheduling-phase-order--what-each-phase-does)
13. [Priority & Tie-Breaking Rules (Soft Preferences)](#13-priority--tie-breaking-rules-soft-preferences)
14. [Force-Fill & Relaxation Rules (Last Resort)](#14-force-fill--relaxation-rules-last-resort)
15. [Merged Sections Rules](#15-merged-sections-rules)
16. [Post-Generation Validation Rules](#16-post-generation-validation-rules)
17. [Substitution Engine — Eligibility Rules](#17-substitution-engine--eligibility-rules)
18. [Substitution Engine — Priority & Scoring Rules](#18-substitution-engine--priority--scoring-rules)
19. [Substitution Engine — Assignment & Cap Rules](#19-substitution-engine--assignment--cap-rules)
20. [Substitution Engine — Auto-Trigger Rules (Leave Approval)](#20-substitution-engine--auto-trigger-rules-leave-approval)
21. [Configuration Defaults (SubstitutionConfig)](#21-configuration-defaults-substitutionconfig)
22. [Data Model Validation Rules](#22-data-model-validation-rules)
23. [Rules Mentioned in Comments but NOT Enforced in Code](#23-rules-mentioned-in-comments-but-not-enforced-in-code)
24. [Quick Reference — Rule Severity Matrix](#24-quick-reference--rule-severity-matrix)

---

## 1. Algorithm Overview

The generation engine uses a **Block-First, Slot-Interleaved, Section-Staggered Assignment** strategy.

| #   | Rule                | Description                                                                                                                                                    |
| --- | ------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| A1  | Block-first         | Subjects with `periodsPerDay ≥ 2` are always placed as **consecutive non-break slot pairs** (a "block").                                                       |
| A2  | Pair singles        | Subjects with `periodsPerDay = 1` are **paired together** into a 2-slot block when possible (e.g., Games + Library in slots 1A + 1B).                          |
| A3  | Slot-interleaved    | For each day, the engine processes **one slot-pair at a time across ALL sections** (round-robin), preventing early sections from monopolizing shared teachers. |
| A4  | Section-staggered   | Section processing order is rotated per day and shuffled per pair to spread load fairly.                                                                       |
| A5  | Controlled variance | Each regeneration produces a **different timetable** by shuffling day order and using random tie-breaking — constraints stay the same, placement order varies. |

---

## 2. Pre-Generation & Input Rules

### 2.1 Merged week bell layout (dynamic multi-profile weeks)

When a school follows **more than one Bell Schedule in the same week** (classic case: Mon–Thu weekday + Fri–Sat weekend with Zero Period), the engine builds one shared week grid:

| Step | Rule |
| ---- | ---- |
| M1 | Collect every active `PeriodGroup` that applies to the section (school-wide if `assignedSectionIds` is empty). |
| M2 | Assign each weekday to **exactly one** profile (non-overlapping day sets preferred; `IsDefault` wins ties). |
| M3 | **Column count** = width of the **largest** day sequence in that week; any labels that exist only on other days are appended (so Zero Period columns still appear). |
| M4 | For each day × column: **enable** when that day’s profile has a matching short label (`1A`, `0B`, `SHORT BREAK`, …); otherwise **disable** (`NotOnDay` / OFF). |
| M5 | Generation only places into **enabled** cells for that day and stamps `WeeklyTimetable.periodGroupId` from the day owner. |
| M6 | Class timetable view renders the same merged columns so layout stays dynamic for any school. |

Implementation: `TimetableEngine/MergedWeekBellLayout.cs`.

---

Rules enforced **before** the engine runs (in `timetableController.js`).

| #   | Rule                   | Detail                                                                                                                    |
| --- | ---------------------- | ------------------------------------------------------------------------------------------------------------------------- |
| P1  | Bell profile required  | Generation fails with 400 if **no bell profiles** exist for the school.                                                   |
| P2  | Sections required      | Generation fails with 400 if **no target sections** are found.                                                            |
| P3  | Allocations required   | Generation fails with 400 if **no ClassAllocation records** exist for target sections.                                    |
| P4  | Pre-Primary excluded   | When auto-selecting sections, these classes are **excluded**: `Pre-Nursery`, `Nursery`, `Junior KG`, `Senior KG`.         |
| P5  | Class sort order       | Classes are sorted **numerically** by class number (`Class 1` … `Class 12`).                                              |
| P6  | Section sort order     | Sections within a class are sorted: **Lily → Lavender → Lotus → Marigold** (unknown names go last).                       |
| P7  | Default active days    | If not provided: `Monday, Tuesday, Wednesday, Thursday, Friday, Saturday`.                                                |
| P8  | Default academic year  | If not provided: `2026-2027`.                                                                                             |
| P9  | Rooms not wired        | Controller passes `rooms: []` — **room allocation logic exists in the engine but no rooms are currently loaded from DB**. |
| P10 | Pinned slots not wired | Controller passes `pinnedSlots: []` — **pre-pinned slot support exists but is not used from the API yet**.                |

---

## 3. Teacher Assignment Rules

How teachers are resolved for each subject before scheduling begins.

| #   | Rule                         | Detail                                                                                                                                                                                  |
| --- | ---------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| T1  | Source of truth              | Teachers are resolved from **`StaffDirectory.allocations`**, not from `ClassAllocation.teacherId` alone (though explicit allocation is preferred — see T2).                             |
| T2  | Explicit teacher preferred   | If `ClassAllocation.subjects[].teacherId` is set and resolves to a known staff member, that teacher is used **exactly as assigned by admin**.                                           |
| T3  | Round-robin fallback         | If no explicit teacher: pick from the teacher pool for `(classId, subjectName)` using **section index modulo pool size** — different sections of the same class get different teachers. |
| T4  | Unassigned logging           | If no teacher can be resolved, the subject is logged in `diagnostics.unallocatedSubjects` and scheduled with `teacherId: null`.                                                         |
| T5  | Activity teachers            | Activity instructors are resolved: **option.teacherId first**, else from **`CurriculumActivity.instructorIds`**.                                                                        |
| T6  | Activity teachers not booked | Activity instructors are **NOT added to `teacherBooked`** — whole-school activities run simultaneously across sections with the same instructors.                                       |

---

## 4. Hard Constraints (Never Violated in Normal Phases)

These are checked via `isTeacherFree`, `isSlotFilled`, and placement filters in Phases 0–2.5.

| #   | Rule                         | Function / Check                     | Detail                                                                                                               |
| --- | ---------------------------- | ------------------------------------ | -------------------------------------------------------------------------------------------------------------------- |
| H1  | No teacher double-booking    | `teacherBooked` + `isTeacherFree`    | A teacher cannot teach two regular subjects in the **same day + slot** across any sections.                          |
| H2  | Teacher off-day              | `teacherOffDays` + `isTeacherFree`   | If a teacher's `FacultyAvailability.offDays` includes the day, they are **unavailable all day**.                     |
| H3  | Slot uniqueness              | `isSlotFilled`                       | A section's `(day, slotId)` can hold **only one assignment**.                                                        |
| H4  | Break slots skipped          | `getTeachingSlots`, `getSlotPairs`   | Break slots (`isBreak: true`) are **never used for teaching**.                                                       |
| H5  | Block never as orphan single | Phase 2+ orphan fill                 | Subjects with `blockSize ≥ 2` are **NEVER placed alone** in an orphan half-pair slot.                                |
| H6  | Day restrictions             | `dayRestrictions` filter             | If a subject has `dayRestrictions: ["Saturday"]`, it can **only** be placed on those days.                           |
| H7  | Daily period cap             | `countSubjectInDay >= periodsPerDay` | A subject cannot exceed its **`periodsPerDay`** limit within a single day for a section.                             |
| H8  | Weekly period quota          | `remainingPeriods`                   | Each subject tracks `remainingPeriods` starting at `periodsPerWeek`; placement decrements it; scheduling stops at 0. |
| H9  | Activity fixed slots         | `canScheduleActivity`                | If `ActivityConfig.fixedSlots` is set, the activity may **only** be placed on listed `(day, slotId)` combinations.   |
| H10 | Zero weekly quota skipped    | allocation build                     | Subjects with `periodsPerWeek ≤ 0` are **excluded** from the workload entirely.                                      |

---

## 5. Block & Pairing Rules

| #   | Rule                       | Detail                                                                                                                          |
| --- | -------------------------- | ------------------------------------------------------------------------------------------------------------------------------- |
| B1  | Consecutive pairs only     | Block subjects (`periodsPerDay ≥ 2`) must occupy **two adjacent non-break slots** with no break between them.                   |
| B2  | Pair definition            | `getSlotPairs()` builds pairs from consecutive non-break slots; after pairing slot N and N+1, slot N+1 is skipped (no overlap). |
| B3  | Single pairing             | Two `periodsPerDay = 1` subjects are **bound as partners** (Phase 0.6) and placed in the **same consecutive slot-pair** (never the same subject twice). Equal weekly quotas are preferred (e.g. Communication 3 + G.K./Moral 3 → three joint doubles). Day-specific slot pairs never fall back to another day's period IDs (dual-bell safe). |
| B3b | Strict Per Day / Per Week  | Subject Allocation `PeriodsPerDay` and `PeriodsPerWeek` are hard caps on every place path (including force-fill / teacher-replacement). No over-placement. |
| B4  | No same-first-word pairing | Two singles that share the **same first word** (e.g., "English Language" + "English Literature") **cannot be paired together**. |
| B5  | Orphan half-pair           | If one slot of a pair is already filled, only a **single-period subject** may fill the empty half — never a block.              |
| B6  | Block requires 2 remaining | A block placement requires `remainingPeriods ≥ 2` and decrements by 2.                                                          |

---

## 6. Subject Layout Rules

Rules that control **where** subjects appear within a day (beyond teacher/slot availability).

| #   | Rule                           | Function                  | Detail                                                                                                                                                                                               |
| --- | ------------------------------ | ------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| L1  | Same-first-word same-day ban   | `sharesFirstWordOnDay`    | Subjects sharing the **first word** (e.g., "English Language" / "English Literature") **cannot appear on the same day** for the same section. First word = first token after trim, case-insensitive. |
| L2  | No break-crossing              | `wouldCrossBreak`         | A subject cannot appear as the **last slot before a break AND the first slot after a break** on the same day (prevents visual "split block" across break).                                           |
| L3  | Max 3 consecutive same subject | `wouldCreate4Consecutive` | Placing a subject is rejected if it would create **3+ consecutive slots** with the same subject (breaks interrupt the count). This prevents 4-period runs when two 2-period blocks would abut.       |
| L4  | Day reservation (Phase 0.4)    | `allowedDaysForSubject`   | For same-first-word subject groups, days are **pre-assigned per section** so each variant gets exclusive days, preventing late-phase deadlocks.                                                      |
| L5  | Cross-section day staggering   | Phase 0.4                 | When the same teacher teaches both "English Language" and "English Literature" across sections, day assignments are **rotated per section index** to spread teacher load.                            |

---

## 7. Games & Library Rules

Hardcoded subject-specific placement rules.

| #   | Rule                       | Detail                                                                                                                    |
| --- | -------------------------- | ------------------------------------------------------------------------------------------------------------------------- |
| G1  | Avoid first pair           | `Games` and `Library` **cannot** be placed in the **first teaching slot pair** (1A/1B) of any day.                        |
| G2  | Class 11 & 12 same-day ban | For sections where `className` is `"Class 11"` or `"Class 12"`: **Games and Library cannot both appear on the same day**. |
| G3  | Applies in all phases      | These rules are checked via `isBlockedByGamesLibraryRules` in Phases 0C, 1, 2, 2.5, and busy-teacher fallback.            |

---

## 8. Activity (Curricular) Rules

| #    | Rule                             | Detail                                                                                                                       |
| ---- | -------------------------------- | ---------------------------------------------------------------------------------------------------------------------------- |
| AC1  | Fixed slot — exact               | `{ day: "Monday", slotId: "s3" }` → activity **must** be on that exact day and period.                                       |
| AC2  | Fixed slot — day only            | `{ day: "Saturday", slotId: null }` → activity **must** be on that day, any teaching period.                                 |
| AC3  | Fixed slot day lock              | Activity with `fixedSlots` may **only** be scheduled on days listed in `fixedSlots`.                                         |
| AC4  | Block activity                   | Activities with `periodsPerDay ≥ 2` are placed in **consecutive non-break pairs**.                                           |
| AC5  | Single activity                  | Activities with `periodsPerDay = 1` use any available teaching slot.                                                         |
| AC5b | Multiple fixed activities first  | **Phase 0B pass 1:** every activity group with `FixedSlots` is locked into those day/period cells (many per class OK) before subjects or free activities. Pass 2 seeds activities without FixedSlots. |
| AC6  | Preferred slots                  | When specific `slotId`s are listed for a day, those slots are **tried first** before any other slot on that day.             |
| AC7  | Day restrictions additive        | `dayRestrictions` on the allocation **further restrict** placement even when `fixedSlots` exist.                             |
| AC8  | Instructors not conflict-checked | Activity instructors are **not checked for availability** and **not booked** — assumes whole-school simultaneous scheduling. |
| AC9  | Display code                     | Cell shows `activityConfig.code` (short code, e.g., "PA") with full name in `groupName` for hover.                           |
| AC10 | Weekly quota                     | Activities track `remainingPeriods` from `periodsPerWeek` (default 2 if unset).                                              |

---

## 9. Room Allocation Rules

| #   | Rule                    | Detail             |
| --- | ----------------------- | ------------------ |
| R1  | Subject → room type map | Hardcoded mapping: |

| Subject      | Required Room Type |
| ------------ | ------------------ |
| Computer     | Computer Lab       |
| Science      | Science Lab        |
| Physics      | Science Lab        |
| Chemistry    | Science Lab        |
| Biology      | Science Lab        |
| Physical Ed. | Sports Ground      |
| Games        | Sports Ground      |

| #   | Rule                   | Detail                                                                                                     |
| --- | ---------------------- | ---------------------------------------------------------------------------------------------------------- |
| R2  | No room double-booking | `roomBooked[roomId][day][slotId]` — a room cannot serve two classes in the same slot.                      |
| R3  | First available match  | `getAvailableRoom` returns the **first room** of matching type that is free in that slot.                  |
| R4  | Optional rooms         | Subjects not in the map require **no special room** (`roomId: null`).                                      |
| R5  | Currently inactive     | Controller passes empty `rooms[]` — room rules are **implemented but not active** in production API calls. |

---

## 10. Day & Quota Rules

| #   | Rule                         | Detail                                                                                                                                      |
| --- | ---------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------- |
| Q1  | `periodsPerWeek`             | Total periods a subject must receive across the week (from `ClassAllocation.subjects[]`). Default: **5** for regular, **2** for activities. |
| Q2  | `periodsPerDay`              | Maximum periods of a subject on **one day** for one section. Default: **1** for regular, **2** for activities. Also determines `blockSize`. |
| Q3  | Quota shortfall reporting    | After all phases, any subject with `remainingPeriods > 0` is logged in `diagnostics.quotaShortfalls`.                                       |
| Q4  | Bell profile per section-day | Each weekday is owned by one period group (`PeriodGroupPeriod.dayOfWeek`). Empty `assignedSectionIds` = school-wide.                       |
| Q4b | Merged week layout           | If **more than one** bell profile is followed in the same week, build a **merged week format** (see §2.1). Used for display **and** generation. |
| Q5  | Bell profile minimum         | A bell profile **must have at least one non-break slot**.                                                                                    |

---

## 11. Shared-Teacher & Section Interleaving Rules

| #   | Rule                         | Detail                                                                                                                                                                       |
| --- | ---------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| S1  | Shared teacher detection     | A teacher assigned to subjects in **2+ sections** is treated as "shared".                                                                                                    |
| S2  | Pre-distribution (Phase 0.5) | Shared teachers' **block subjects** are distributed round-robin across sections **before** general scheduling.                                                               |
| S3  | Capacity reservation         | Phase 0.5 calculates `maxBlockPeriods = totalSlotsPerWeek - singlesNeeded` — block placement stops when this cap is reached, **reserving slots for single-period subjects**. |
| S4  | Singles not pre-distributed  | Single-period subjects for shared teachers are **not** pre-distributed; they rely on Phase 1/2 pairing logic.                                                                |
| S5  | Urgency scoring              | In Phase 1, sections with workloads depending on **higher-demand (more constrained) teachers** are processed first for each slot-pair.                                       |
| S6  | Demand formula               | `sectionUrgency = sum of (teacherDemand + remainingPeriods)` for block candidates needing that slot-pair.                                                                    |

---

## 12. Scheduling Phase Order & What Each Phase Does

The engine runs phases **sequentially**. Later phases only fill what earlier phases missed.

| Phase    | Name                            | Key Rules Applied                                                                                                                                                     |
| -------- | ------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **0A**   | Pin explicit slots              | Pre-locked assignments from `pinnedSlots[]` are committed first; deducts from quota.                                                                                  |
| **0B**   | Fixed-slot activities           | Activities with `fixedSlots` placed on allowed days/slots; weekly quota distributed round-robin across allowed days.                                                  |
| **0C**   | Day-restricted subjects         | Activities and regular subjects with `dayRestrictions` (no fixed slots) placed on restricted days only.                                                               |
| **0.4**  | Day reservation                 | Pre-assigns exclusive days to same-first-word subject variants per section (cross-section staggered).                                                                 |
| **0.5**  | Shared-teacher pre-distribution | Round-robin block placement for teachers shared across 2+ sections; respects capacity cap for singles.                                                                |
| **1**    | Slot-interleaved blocks         | For each shuffled day → each slot-pair → all sections (by urgency): try block subjects, then paired singles, then individual singles. Also places unfixed activities. |
| **2**    | Pair-based fill                 | Second pass: empty pairs get blocks or paired singles; orphan half-pairs get singles only.                                                                            |
| **2.5**  | Retry pass                      | Third pass with strict `periodsPerDay` enforcement; orphan singles only for half-pairs.                                                                               |
| **2.5b** | Busy-teacher fallback           | Slots where assigned teacher is busy: place subject with `needsTeacherReplacement: true` + up to 10 suggested replacements.                                           |
| **2.75** | Force-fill                      | Progressive relaxation (see Section 14).                                                                                                                              |
| **3**    | Validation                      | Scans for teacher double-booking conflicts → `diagnostics.conflicts`.                                                                                                 |
| **4**    | Merged sections                 | Mirrors first section's schedule for a subject to all other sections in the merge group.                                                                              |

---

## 13. Priority & Tie-Breaking Rules (Soft Preferences)

These affect **which subject gets a slot** when multiple candidates are valid — not whether the slot can be used.

| #    | Rule                     | When                     | Priority Order (highest first)                                            |
| ---- | ------------------------ | ------------------------ | ------------------------------------------------------------------------- |
| PR1  | Day-restricted boost     | Phase 1 block candidates | +2000 if subject's `dayRestrictions` includes current day                 |
| PR2  | Day-restricted boost     | Phase 2 block candidates | +5000 if day-restricted and matches current day                           |
| PR3  | Day reservation boost    | Phase 1                  | +500 if subject is allowed on this day per Phase 0.4 reservation          |
| PR4  | Over-daily-limit penalty | Phase 2 sort             | −2000 if subject already at `periodsPerDay` on this day                   |
| PR5  | Remaining periods        | All phases               | Higher `remainingPeriods` → scheduled sooner                              |
| PR6  | Teacher demand           | Phase 1                  | Higher shared-teacher demand → scheduled sooner                           |
| PR7  | Random tie-break         | Phase 1, 2               | Equal priority → `Math.random() - 0.5` for generation variance            |
| PR8  | Day shuffle              | Phase 1                  | `activeDays` order is **Fisher-Yates shuffled** each run                  |
| PR9  | Section shuffle          | Phase 1, 2               | Section processing order shuffled within each slot-pair                   |
| PR10 | Activity section stagger | Phase 1                  | Unfixed activities: section order rotated by `dayIndex % sections.length` |

---

## 14. Force-Fill & Relaxation Rules (Last Resort)

Phase 2.75 runs when slots remain empty despite earlier phases. Constraints are **progressively relaxed**.

| Pass   | Teacher      | Same-First-Word                              | Daily Limit                              | Notes                                                                |
| ------ | ------------ | -------------------------------------------- | ---------------------------------------- | -------------------------------------------------------------------- |
| **A**  | Must be free | **Enforced**                                 | Soft penalty (−1000 if over daily limit) | Preferred force-fill                                                 |
| **A2** | Must be free | **Relaxed** (soft penalty −3000 if violated) | Soft penalty                             | Last resort with free teacher                                        |
| **B**  | Teacher busy | **Enforced**                                 | Ignored                                  | Places with `needsTeacherReplacement: true` + suggested replacements |

Additional force-fill rules:

- `dayRestrictions` are **still enforced** in all passes (never relaxed).
- Block subjects **can** be placed as singles in force-fill (daily limit soft).
- Empty slot is preferred over violating same-first-word in Pass B.

---

## 15. Merged Sections Rules

Configured via `MergeRule` model and passed as `mergedSections[]` to the engine.

| #   | Rule                    | Detail                                                                                                       |
| --- | ----------------------- | ------------------------------------------------------------------------------------------------------------ |
| M1  | Merge group structure   | `{ classId, subject, sectionIds: [id1, id2, ...] }` — sections that share combined classes for a subject.    |
| M2  | Master section          | `sectionIds[0]` is the **source/master** schedule.                                                           |
| M3  | Mirror after generation | Phase 4 copies all slots where the master has the given `subject` to every other section in the group.       |
| M4  | Clear before mirror     | Target section's existing assignments for that subject are **deleted** before copying.                       |
| M5  | Exact cell copy         | Copied cells are a **shallow clone** of the master cell (same teacher, room, activity data).                 |
| M6  | Persistence             | Merge rules are stored in DB via `PUT /api/v1/timetable/merge-rules` and re-supplied on each generation run. |

---

## 16. Post-Generation Validation Rules

| #   | Rule                       | Detail                                                                                                                                                                                       |
| --- | -------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| V1  | Teacher double-book check  | After all phases, scans `result` for the same `teacherId + day + slotId` in **multiple sections** → logged as `diagnostics.conflicts` (type: `teacher_double_booked`).                       |
| V2  | Activity teachers excluded | Activity instructor double-booking is **not validated** (by design).                                                                                                                         |
| V3  | Diagnostics output         | Engine returns `{ timetableData, diagnostics }` where diagnostics includes: `selfStudySlots`, `quotaShortfalls`, `pinnedSlotsAssigned`, `unallocatedSubjects`, `pairedSingles`, `conflicts`. |

---

## 17. Substitution Engine — Eligibility Rules

A teacher is a **valid substitute candidate** for a period only if ALL of the following pass (`substitutionEngine.analyzeImpact`):

| #   | Rule                          | Detail                                                                                                                               |
| --- | ----------------------------- | ------------------------------------------------------------------------------------------------------------------------------------ |
| SE1 | Not absent                    | Candidate is **not** in `absentTeacherIds` for this run.                                                                             |
| SE2 | Not on off-day                | Candidate's `FacultyAvailability.offDays` does **not** include the day.                                                              |
| SE3 | Not teaching at slot time     | Candidate is **not** already teaching any section at the slot's `start` time in the regular timetable.                               |
| SE4 | Not already assigned as proxy | Candidate is **not** already assigned as a substitute in this slot during the current assignment run.                                |
| SE5 | Period must be impacted       | Only periods where an **absent teacher** appears in the cell (via `teacherId` or `activities[].teacherId/teacherIds`) are processed. |
| SE6 | Break slots skipped           | Break periods are never analyzed for substitution.                                                                                   |

**Absent teacher discovery** (`substitutionService.runAutoSubstitution`):

- Explicit `absentTeacherIds` from the request **plus**
- All teachers whose `FacultyAvailability.offDays` includes the day's weekday name.

---

## 18. Substitution Engine — Priority & Scoring Rules

### Priority Tiers (before scoring)

| Priority | Label                     | Condition                                                                                                           |
| -------- | ------------------------- | ------------------------------------------------------------------------------------------------------------------- |
| **1**    | Same Subject              | Candidate's `subjects[]` includes the cell's subject                                                                |
| **2**    | Familiar with Class       | Candidate teaches **any subject** in that section on that day (but not the same subject — that would be Priority 1) |
| **3**    | Free Teacher (Proxy Duty) | All other eligible candidates                                                                                       |

### Weighted Score Formula

```
score = tierWeight
      + (subCountPenaltyToday × subsAlreadyAssignedToday)
      + (subCountPenaltyMonth × (subsThisMonth / 10))
```

| Weight Key             | Default  | Meaning                                                     |
| ---------------------- | -------- | ----------------------------------------------------------- |
| `sameSubject`          | **+10**  | Priority 1 tier bonus                                       |
| `sameClass`            | **+6**   | Priority 2 tier bonus                                       |
| `freeTeacher`          | **+3**   | Priority 3 tier bonus                                       |
| `subCountPenaltyToday` | **−2**   | Per substitution already assigned to this teacher **today** |
| `subCountPenaltyMonth` | **−0.5** | Per substitution this month, **scaled by ÷10**              |

Weights are configurable per school via `SubstitutionConfig.priorityWeights`.

### Tie-Breaking

| #   | Rule                | Detail                                                                |
| --- | ------------------- | --------------------------------------------------------------------- |
| ST1 | Higher score wins   | Primary sort: descending score.                                       |
| ST2 | Lighter weekly load | If scores tie: candidate with **lower `weeklyPeriods`** is preferred. |

---

## 19. Substitution Engine — Assignment & Cap Rules

| #   | Rule                            | Detail                                                                                                            |
| --- | ------------------------------- | ----------------------------------------------------------------------------------------------------------------- |
| SA1 | Chronological processing        | Impacted periods sorted by `slotStart` time (earliest first).                                                     |
| SA2 | Max subs per day                | A teacher can be assigned at most **`maxSubsPerTeacherPerDay`** substitutions per day (default: **3**).           |
| SA3 | One proxy per slot              | `slotTaken[slotId]` prevents the same teacher from being assigned to **two different absences in the same slot**. |
| SA4 | One assignment per absence-slot | If `proxyAssignments[originalTeacherId][slotId]` already exists in this run, skip.                                |
| SA5 | Unassigned logging              | Periods with no eligible candidate after filtering → `unassigned[]`.                                              |
| SA6 | Status on save                  | Log status = `"finalized"` if `finalize=true` OR `config.autoFinalizeEnabled=true`; else `"auto-assigned"`.       |
| SA7 | Empty absence list              | If no absent teachers: creates/updates log with empty assignments, status `"draft"`.                              |
| SA8 | Latest timetable used           | Uses the **most recently created** `GeneratedTimetable` for the school + academic year.                           |

---

## 20. Substitution Engine — Auto-Trigger Rules (Leave Approval)

When an admin approves leave (`leaveController.updateLeaveStatus`):

| #   | Rule                   | Detail                                                                                                                                    |
| --- | ---------------------- | ----------------------------------------------------------------------------------------------------------------------------------------- |
| AT1 | Trigger condition      | Runs only when leave status is set to **`"approved"`**.                                                                                   |
| AT2 | User → Staff bridge    | Maps `Leave.teacherId` (User) to `StaffDirectory` by **email match**. If no match, sync is skipped silently.                              |
| AT3 | Off-day sync           | Each date in the leave range is added to `FacultyAvailability.offDays` as the **weekday name** (e.g., `"Monday"`).                        |
| AT4 | Auto-substitution gate | Runs `runAutoSubstitution` only if `SubstitutionConfig.autoAssignOnLeaveApproval` is **true** (defaults to **true** if no config exists). |
| AT5 | Per-day execution      | Auto-substitution runs **once per leave date** in the range.                                                                              |
| AT6 | Failure isolation      | Sync/substitution errors are **logged but do not fail** the leave approval response.                                                      |
| AT7 | Flag on leave          | Sets `leave.autoSubstitutionTriggered = true` after successful auto-run.                                                                  |

---

## 21. Configuration Defaults (SubstitutionConfig)

| Setting                                | Default | Effect                                                                  |
| -------------------------------------- | ------- | ----------------------------------------------------------------------- |
| `maxSubsPerTeacherPerDay`              | 3       | Max substitution assignments per teacher per day                        |
| `autoAssignOnLeaveApproval`            | true    | Auto-run substitution when leave is approved                            |
| `autoFinalizeEnabled`                  | false   | Auto-finalize substitution logs after auto-run                          |
| `scheduledRunTime`                     | "16:00" | Planned time for next-day prep (stored, not cron-implemented in engine) |
| `notifyViaEmail`                       | false   | Email notification flag                                                 |
| `notifyInApp`                          | true    | In-app notification flag                                                |
| `priorityWeights.sameSubject`          | 10      | Scoring weight                                                          |
| `priorityWeights.sameClass`            | 6       | Scoring weight                                                          |
| `priorityWeights.freeTeacher`          | 3       | Scoring weight                                                          |
| `priorityWeights.subCountPenaltyToday` | -2      | Scoring penalty                                                         |
| `priorityWeights.subCountPenaltyMonth` | -0.5    | Scoring penalty                                                         |

---

## 22. Data Model Validation Rules

### FacultyAvailability

| #   | Rule                | Detail                                               |
| --- | ------------------- | ---------------------------------------------------- |
| DM1 | Valid weekdays only | `offDays` entries must be one of: Monday–Saturday.   |
| DM2 | Cannot be fully off | A teacher **cannot have all 6 days** as off-days.    |
| DM3 | Unique per school   | One availability record per `(schoolId, teacherId)`. |

### BellProfile

| #   | Rule                   | Detail                                        |
| --- | ---------------------- | --------------------------------------------- |
| DM4 | Minimum teaching slots | Must contain **at least one non-break slot**. |

### ActivityConfig

| #   | Rule                   | Detail                             |
| --- | ---------------------- | ---------------------------------- |
| DM5 | Unique code per school | `(schoolId, code)` must be unique. |

### MergeRule

| #   | Rule            | Detail                                            |
| --- | --------------- | ------------------------------------------------- |
| DM6 | Required fields | `schoolId`, `classId`, `subject`, `sectionIds[]`. |

---

## 23. Rules Mentioned in Comments but NOT Enforced in Code

| Mentioned Rule                                                                         | Actual Behavior                                                                                    |
| -------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------- |
| "ALL activity group teachers must be available simultaneously" (engine header comment) | Activity instructors are **not checked for availability** and **not booked** in `teacherBooked`.   |
| "Teacher resolution from StaffDirectory.allocations (not stored in ClassAllocation)"   | **Partially true** — explicit `ClassAllocation.subjects[].teacherId` **is honored** when set (T2). |
| Room allocation for lab/sports subjects                                                | Logic exists but **`rooms: []`** is passed — rooms are never assigned in practice.                 |
| `seed:bell`, `seed:timetable`, etc.                                                    | Referenced in `package.json` scripts but **seeder files are missing** from the repo.               |

---

## 24. Quick Reference — Rule Severity Matrix

| Severity         | Meaning                                                    | Examples                                                         |
| ---------------- | ---------------------------------------------------------- | ---------------------------------------------------------------- |
| **HARD**         | Never violated in Phases 0–2.5; only relaxed in force-fill | H1–H10, B1–B6, G1–G2, L1–L3, AC1–AC3                             |
| **SOFT**         | Affects sort priority, not eligibility                     | PR1–PR10, daily limit penalties in force-fill                    |
| **RELAXED**      | Violated only in Phase 2.75 force-fill                     | L1 (Pass A2), daily limits (Pass A/A2), block-as-single (Pass A) |
| **POST-HOC**     | Applied after main scheduling                              | M1–M5 (merge mirror), V1 (conflict scan)                         |
| **SUBSTITUTION** | Separate engine, runs on generated timetable               | SE1–SE8, SA1–SA8, ST1–ST2                                        |

---

## Related Documentation

- [`12-End-to-End-Flows.md`](./12-End-to-End-Flows.md) — Leave → approval → auto-substitution journey
- [`01-System-Overview-and-Architecture.md`](./01-System-Overview-and-Architecture.md) — System architecture overview
- Source: `server/src/services/timetable/generationEngine.js` (1773 lines)
- Source: `server/src/services/timetable/substitutionEngine.js` (308 lines)
- Source: `server/src/services/timetable/substitutionService.js` (163 lines)
