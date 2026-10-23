# Teacher Class Assignments

| Field | Value |
| --- | --- |
| Source | `RIMS_DB_17_Backup_18_07.sql` |
| Table | `public.teaching_assignments` |
| Scope | Active, non-deleted allocations · Academic year 2026-27 |
| View | Class level (sections excluded from detail tables) |
| Related | `CLASS_WISE_SUBJECTS.md` · `TEACHER_CLASS_ALLOCATION.xlsx` |

Active teacher-to-class assignments consolidated at **class level**. Section-level detail is available in the Excel workbook.

---

## Contents

1. [Summary](#1-summary)
2. [Teacher roster](#2-teacher-roster)
3. [Unassigned placeholders](#3-unassigned-placeholder-allocations)
4. [Assignments by teacher](#4-teacher-assignments-by-class)
5. [Data quality notes](#5-data-quality-notes)

---

## 1. Summary

| Metric | Count |
| --- | ---: |
| Active section-level assignments | 587 |
| Teachers with assignments | 44 |
| Class-level assignment rows | 210 |
| Class-teacher flags | 0 |
| Placeholder rows (`UNASSIGNED-TT`) | 92 |
| Teaching staff with no assignment | 12 |
| Zero-workload active rows | 142 |

---

## 2. Teacher Roster

| # | Code | Teacher | Classes | Subjects / Activities | Periods / Week |
| ---: | --- | --- | --- | ---: | ---: |
| 1 | `002` | Ruchi Singh | Class 1, Class 2, Class 7 | 1 | 45 |
| 2 | `100047` | Ritika Malviya | Class 3, Class 4, Class 6, Class 7, Class 8 | 3 | 75 |
| 3 | `100048` | P Aliya Aliya Papua | Class 2 | 2 | 40 |
| 4 | `100049` | Jyoti Solanki | Class 5, Class 6 | 1 | 20 |
| 5 | `100052` | Prarthana Goswami | Class 2 | 1 | 20 |
| 6 | `100059` | Harsha Talreja | Class 4 | 2 | 0 |
| 7 | `100091` | Ayushi Sharma | Class 1, Class 2, Class 3, Class 4, Class 5, Class 7 | 1 | 90 |
| 8 | `E1014` | Laxmi Ghatt | Class 6, Class 7, Class 8, Class 9, Class 10, Class 11, Class 12 | 1 | 60 |
| 9 | `E2133` | Sujata Naidu | Class 5, Class 6, Class 7, Class 8, Class 9, Class 10 | 2 | 0 |
| 10 | `E2336` | Mohd Fazal Rehman | Class 4, Class 5, Class 6, Class 7, Class 8, Class 9, Class 10, Class 11, Class 12 | 2 | 0 |
| 11 | `E2628` | Kavita A | Class 4, Class 5, Class 6, Class 7, Class 8, Class 9, Class 10, Class 11, Class 12 | 2 | 90 |
| 12 | `E2701` | Harpreet Kaur | Class 7, Class 8, Class 9, Class 10, Class 11, Class 12 | 1 | 0 |
| 13 | `E2787` | Rajni Soni | Class 5 | 4 | 40 |
| 14 | `E2904` | Priyanka Rana | Class 1 | 1 | 20 |
| 15 | `GEN-2745` | Firdosh . | Class 5, Class 6, Class 7, Class 8 | 1 | 0 |
| 16 | `GEN-3812` | Arunav Kishor | Class 4, Class 5, Class 6, Class 7, Class 8 | 1 | 45 |
| 17 | `GEN-7649` | Priyanka Singh | Class 6, Class 7 | 2 | 30 |
| 18 | `GEN-9148` | Anjali Dewangan | Class 4, Class 5, Class 6, Class 7, Class 8 | 1 | 45 |
| 19 | `RVS06` | Harshita Malviya | Class 1 | 1 | 0 |
| 20 | `RVS09` | Dhanu Priya Kolte | Class 3, Class 4 | 1 | 35 |
| 21 | `RVS54` | Ruhamah David | Class 9, Class 10, Class 11, Class 12 | 1 | 40 |
| 22 | `RVS67` | Namita Nikam | Class 8, Class 9, Class 10, Class 11, Class 12 | 1 | 0 |
| 23 | `RVS76` | Seema Khatri | Class 1, Class 2, Class 4 | 2 | 0 |
| 24 | `RVS81` | Prajakta Dhandore | Class 1, Class 2, Class 3, Class 4, Class 5, Class 6, Class 7, Class 8 | 1 | 105 |
| 25 | `RVS85` | Anjulika Singh | Class 4, Class 5, Class 6, Class 7, Class 8, Class 9, Class 10, Class 11, Class 12 | 2 | 85 |
| 26 | `RVS95` | Preeti Muchhal | Class 3, Class 4 | 1 | 35 |
| 27 | `RVS115` | Ashish Thakur | Class 9, Class 10, Class 11, Class 12 | 1 | 0 |
| 28 | `RVS136` | Shashi Shukla | Class 8, Class 9, Class 10, Class 11, Class 12 | 1 | 45 |
| 29 | `RVS143` | Meenakshi Singh | Class 3, Class 4, Class 5 | 1 | 0 |
| 30 | `RVS148` | Neelam Pawar Digarse | Class 1, Class 2, Class 3, Class 6 | 1 | 70 |
| 31 | `RVS149` | Pratha Choubey | Class 2 | 2 | 40 |
| 32 | `RVS150` | Sugatha Unnithan | Class 1, Class 3 | 2 | 0 |
| 33 | `RVS154` | Soma Dinda | Class 2, Class 4, Class 5 | 3 | 60 |
| 34 | `RVS157` | Noshin Siddiqui | Class 3, Class 4 | 1 | 35 |
| 35 | `RVS159` | Saksham Sharma | Class 8, Class 9, Class 10, Class 11, Class 12 | 1 | 0 |
| 36 | `RVS172` | Swati Budholiya | Class 1 | 2 | 40 |
| 37 | `RVS173` | Shivendra Bopche | Class 1, Class 2, Class 3, Class 4, Class 5, Class 6, Class 7, Class 8 | 1 | 105 |
| 38 | `RVS174` | Ishwari Dubey | Class 2, Class 3, Class 4, Class 5 | 1 | 0 |
| 39 | `RVS188` | Kajal Shrivastava | Class 1, Class 2, Class 3, Class 4, Class 5, Class 6, Class 7, Class 9, Class 10, Class 11, Class 12 | 8 | 170 |
| 40 | `RVS189` | Neha Guha | Class 9, Class 10, Class 11, Class 12 | 2 | 0 |
| 41 | `RVS190` | Soumya Nair | Class 9, Class 10, Class 11, Class 12 | 1 | 40 |
| 42 | `RVS195` | Anjali Singh Chauhan | Class 1, Class 2 | 2 | 40 |
| 43 | `RVT095` | Aleena Khan | Class 1, Class 3 | 2 | 52 |
| 44 | `SPS26` | Leena Jain | Class 6, Class 7, Class 8, Class 9, Class 10 | 2 | 50 |

---

## 3. Unassigned Placeholder Allocations

> **Warning:** `UNASSIGNED-TT` holds **92** active placeholder rows marked `NEEDS_TEACHER_ASSIGNMENT`. Reassign before timetable generation.

### `UNASSIGNED-TT` — Unassigned Teacher

| Class | Type | Subject / Activity |
| --- | --- | --- |
| Class 1 | Subject | Art Education |
| Class 1 | Subject | G.K./Moral |
| Class 2 | Subject | English Language |
| Class 2 | Subject | English Literature |
| Class 2 | Subject | G.K./Moral |
| Class 2 | Subject | Hindi Vyakaran |
| Class 2 | Subject | Mathematics |
| Class 3 | Subject | Art Education |
| Class 3 | Subject | English Language |
| Class 3 | Subject | English Literature |
| Class 3 | Subject | Hindi Vyakaran |
| Class 3 | Subject | Science |
| Class 4 | Subject | Art Education |
| Class 4 | Subject | English Language |
| Class 4 | Subject | English Literature |
| Class 4 | Subject | Hindi Vyakaran |
| Class 5 | Subject | Art Education |
| Class 5 | Subject | English Language |
| Class 5 | Subject | English Literature |
| Class 5 | Subject | Hindi Vyakaran |
| Class 9 | Subject | English Language |
| Class 9 | Subject | English Literature |
| Class 9 | Subject | History |
| Class 9 | Subject | Physics |
| Class 10 | Subject | English Language |
| Class 10 | Subject | English Literature |
| Class 10 | Subject | History |
| Class 10 | Subject | Physics |
| Class 11 | Subject | English Language |
| Class 11 | Subject | English Literature |
| Class 12 | Subject | English Language |
| Class 12 | Subject | English Literature |

---

## 4. Teacher Assignments by Class

### `002` — Ruchi Singh

| | |
| --- | --- |
| Classes | **Class 1, Class 2, Class 7** |
| Total periods / week | **45** |

| Class | Type | Subject / Activity | Periods / Week |
| --- | --- | --- | ---: |
| Class 1 | Subject | Art Education | 20 |
| Class 2 | Subject | Art Education | 20 |
| Class 7 | Subject | Art Education | 5 |

### `100047` — Ritika Malviya

| | |
| --- | --- |
| Classes | **Class 3, Class 4, Class 6, Class 7, Class 8** |
| Total periods / week | **75** |

| Class | Type | Subject / Activity | Periods / Week |
| --- | --- | --- | ---: |
| Class 3 | Subject | Communication | 20 |
| Class 3 | Subject | G.K./Moral | 20 |
| Class 4 | Subject | G.K./Moral | 15 |
| Class 6 | Subject | English | 10 |
| Class 7 | Subject | English | 5 |
| Class 8 | Subject | English | 5 |

### `100048` — P Aliya Aliya Papua

| | |
| --- | --- |
| Classes | **Class 2** |
| Total periods / week | **40** |

| Class | Type | Subject / Activity | Periods / Week |
| --- | --- | --- | ---: |
| Class 2 | Subject | Communication | 20 |
| Class 2 | Subject | English | 20 |

### `100049` — Jyoti Solanki

| | |
| --- | --- |
| Classes | **Class 5, Class 6** |
| Total periods / week | **20** |

| Class | Type | Subject / Activity | Periods / Week |
| --- | --- | --- | ---: |
| Class 5 | Subject | Hindi | 10 |
| Class 6 | Subject | Hindi | 10 |

### `100052` — Prarthana Goswami

| | |
| --- | --- |
| Classes | **Class 2** |
| Total periods / week | **20** |

| Class | Type | Subject / Activity | Periods / Week |
| --- | --- | --- | ---: |
| Class 2 | Subject | Mathematics | 20 |

### `100059` — Harsha Talreja

| | |
| --- | --- |
| Classes | **Class 4** |
| Total periods / week | **0** |

| Class | Type | Subject / Activity | Periods / Week |
| --- | --- | --- | ---: |
| Class 4 | Subject | Mathematics | 0 |
| Class 4 | Subject | Science | 0 |

### `100091` — Ayushi Sharma

| | |
| --- | --- |
| Classes | **Class 1, Class 2, Class 3, Class 4, Class 5, Class 7** |
| Total periods / week | **90** |

| Class | Type | Subject / Activity | Periods / Week |
| --- | --- | --- | ---: |
| Class 1 | Subject | Games | 20 |
| Class 2 | Subject | Games | 20 |
| Class 3 | Subject | Games | 20 |
| Class 4 | Subject | Games | 15 |
| Class 5 | Subject | Games | 10 |
| Class 7 | Subject | Games | 5 |

### `E1014` — Laxmi Ghatt

| | |
| --- | --- |
| Classes | **Class 6, Class 7, Class 8, Class 9, Class 10, Class 11, Class 12** |
| Total periods / week | **60** |

| Class | Type | Subject / Activity | Periods / Week |
| --- | --- | --- | ---: |
| Class 6 | Subject | Art Education | 10 |
| Class 7 | Subject | Art Education | 5 |
| Class 8 | Subject | Art Education | 5 |
| Class 9 | Subject | Art Education | 10 |
| Class 10 | Subject | Art Education | 10 |
| Class 11 | Subject | Art Education | 10 |
| Class 12 | Subject | Art Education | 10 |

### `E2133` — Sujata Naidu

| | |
| --- | --- |
| Classes | **Class 5, Class 6, Class 7, Class 8, Class 9, Class 10** |
| Total periods / week | **0** |

| Class | Type | Subject / Activity | Periods / Week |
| --- | --- | --- | ---: |
| Class 5 | Subject | Hindi | 0 |
| Class 5 | Activity | Sanskrit | 0 |
| Class 6 | Subject | Hindi | 0 |
| Class 6 | Activity | Sanskrit | 0 |
| Class 7 | Subject | Hindi | 0 |
| Class 7 | Activity | Sanskrit | 0 |
| Class 8 | Subject | Hindi | 0 |
| Class 8 | Activity | Sanskrit | 0 |
| Class 9 | Subject | Hindi | 0 |
| Class 10 | Subject | Hindi | 0 |

### `E2336` — Mohd Fazal Rehman

| | |
| --- | --- |
| Classes | **Class 4, Class 5, Class 6, Class 7, Class 8, Class 9, Class 10, Class 11, Class 12** |
| Total periods / week | **0** |

| Class | Type | Subject / Activity | Periods / Week |
| --- | --- | --- | ---: |
| Class 4 | Subject | Games | 0 |
| Class 5 | Subject | Games | 0 |
| Class 6 | Subject | Games | 0 |
| Class 7 | Subject | Games | 0 |
| Class 8 | Subject | Games | 0 |
| Class 9 | Subject | Games | 0 |
| Class 9 | Subject | Physical Education | 0 |
| Class 10 | Subject | Games | 0 |
| Class 10 | Subject | Physical Education | 0 |
| Class 11 | Subject | Games | 0 |
| Class 11 | Subject | Physical Education | 0 |
| Class 12 | Subject | Games | 0 |
| Class 12 | Subject | Physical Education | 0 |

### `E2628` — Kavita A

| | |
| --- | --- |
| Classes | **Class 4, Class 5, Class 6, Class 7, Class 8, Class 9, Class 10, Class 11, Class 12** |
| Total periods / week | **90** |

| Class | Type | Subject / Activity | Periods / Week |
| --- | --- | --- | ---: |
| Class 4 | Subject | Library | 15 |
| Class 5 | Subject | Library | 10 |
| Class 6 | Subject | Geography | 10 |
| Class 7 | Subject | Geography | 5 |
| Class 7 | Subject | Library | 5 |
| Class 8 | Subject | Geography | 5 |
| Class 9 | Subject | Geography | 10 |
| Class 10 | Subject | Geography | 10 |
| Class 11 | Subject | Library | 10 |
| Class 12 | Subject | Library | 10 |

### `E2701` — Harpreet Kaur

| | |
| --- | --- |
| Classes | **Class 7, Class 8, Class 9, Class 10, Class 11, Class 12** |
| Total periods / week | **0** |

| Class | Type | Subject / Activity | Periods / Week |
| --- | --- | --- | ---: |
| Class 7 | Subject | Computer | 0 |
| Class 8 | Subject | Computer | 0 |
| Class 9 | Subject | Computer | 0 |
| Class 10 | Subject | Computer | 0 |
| Class 11 | Subject | Computer | 0 |
| Class 12 | Subject | Computer | 0 |

### `E2787` — Rajni Soni

| | |
| --- | --- |
| Classes | **Class 5** |
| Total periods / week | **40** |

| Class | Type | Subject / Activity | Periods / Week |
| --- | --- | --- | ---: |
| Class 5 | Subject | Communication | 10 |
| Class 5 | Subject | G.K./Moral | 10 |
| Class 5 | Subject | Mathematics | 10 |
| Class 5 | Subject | Social Studies | 10 |

### `E2904` — Priyanka Rana

| | |
| --- | --- |
| Classes | **Class 1** |
| Total periods / week | **20** |

| Class | Type | Subject / Activity | Periods / Week |
| --- | --- | --- | ---: |
| Class 1 | Subject | English | 20 |

### `GEN-2745` — Firdosh .

| | |
| --- | --- |
| Classes | **Class 5, Class 6, Class 7, Class 8** |
| Total periods / week | **0** |

| Class | Type | Subject / Activity | Periods / Week |
| --- | --- | --- | ---: |
| Class 5 | Activity | French | 0 |
| Class 6 | Activity | French | 0 |
| Class 7 | Activity | French | 0 |
| Class 8 | Activity | French | 0 |

### `GEN-3812` — Arunav Kishor

| | |
| --- | --- |
| Classes | **Class 4, Class 5, Class 6, Class 7, Class 8** |
| Total periods / week | **45** |

| Class | Type | Subject / Activity | Periods / Week |
| --- | --- | --- | ---: |
| Class 4 | Activity | Robotics | 15 |
| Class 5 | Activity | Robotics | 10 |
| Class 6 | Activity | Robotics | 10 |
| Class 7 | Activity | Robotics | 5 |
| Class 8 | Activity | Robotics | 5 |

### `GEN-7649` — Priyanka Singh

| | |
| --- | --- |
| Classes | **Class 6, Class 7** |
| Total periods / week | **30** |

| Class | Type | Subject / Activity | Periods / Week |
| --- | --- | --- | ---: |
| Class 6 | Subject | Mathematics | 10 |
| Class 6 | Subject | Physics | 10 |
| Class 7 | Subject | Mathematics | 5 |
| Class 7 | Subject | Physics | 5 |

### `GEN-9148` — Anjali Dewangan

| | |
| --- | --- |
| Classes | **Class 4, Class 5, Class 6, Class 7, Class 8** |
| Total periods / week | **45** |

| Class | Type | Subject / Activity | Periods / Week |
| --- | --- | --- | ---: |
| Class 4 | Activity | Abacus | 15 |
| Class 5 | Activity | Abacus | 10 |
| Class 6 | Activity | Abacus | 10 |
| Class 7 | Activity | Abacus | 5 |
| Class 8 | Activity | Abacus | 5 |

### `RVS06` — Harshita Malviya

| | |
| --- | --- |
| Classes | **Class 1** |
| Total periods / week | **0** |

| Class | Type | Subject / Activity | Periods / Week |
| --- | --- | --- | ---: |
| Class 1 | Subject | Mathematics | 0 |

### `RVS09` — Dhanu Priya Kolte

| | |
| --- | --- |
| Classes | **Class 3, Class 4** |
| Total periods / week | **35** |

| Class | Type | Subject / Activity | Periods / Week |
| --- | --- | --- | ---: |
| Class 3 | Subject | Hindi | 20 |
| Class 4 | Subject | Hindi | 15 |

### `RVS54` — Ruhamah David

| | |
| --- | --- |
| Classes | **Class 9, Class 10, Class 11, Class 12** |
| Total periods / week | **40** |

| Class | Type | Subject / Activity | Periods / Week |
| --- | --- | --- | ---: |
| Class 9 | Subject | Economics | 10 |
| Class 10 | Subject | Economics | 10 |
| Class 11 | Subject | Economics | 10 |
| Class 12 | Subject | Economics | 10 |

### `RVS67` — Namita Nikam

| | |
| --- | --- |
| Classes | **Class 8, Class 9, Class 10, Class 11, Class 12** |
| Total periods / week | **0** |

| Class | Type | Subject / Activity | Periods / Week |
| --- | --- | --- | ---: |
| Class 8 | Subject | Biology | 0 |
| Class 9 | Subject | Biology | 0 |
| Class 10 | Subject | Biology | 0 |
| Class 11 | Subject | Biology | 0 |
| Class 12 | Subject | Biology | 0 |

### `RVS76` — Seema Khatri

| | |
| --- | --- |
| Classes | **Class 1, Class 2, Class 4** |
| Total periods / week | **0** |

| Class | Type | Subject / Activity | Periods / Week |
| --- | --- | --- | ---: |
| Class 1 | Subject | Communication | 0 |
| Class 2 | Subject | Hindi | 0 |
| Class 4 | Subject | Hindi | 0 |

### `RVS81` — Prajakta Dhandore

| | |
| --- | --- |
| Classes | **Class 1, Class 2, Class 3, Class 4, Class 5, Class 6, Class 7, Class 8** |
| Total periods / week | **105** |

| Class | Type | Subject / Activity | Periods / Week |
| --- | --- | --- | ---: |
| Class 1 | Subject | Dance | 20 |
| Class 2 | Subject | Dance | 20 |
| Class 3 | Subject | Dance | 20 |
| Class 4 | Subject | Dance | 15 |
| Class 5 | Subject | Dance | 10 |
| Class 6 | Subject | Dance | 10 |
| Class 7 | Subject | Dance | 5 |
| Class 8 | Subject | Dance | 5 |

### `RVS85` — Anjulika Singh

| | |
| --- | --- |
| Classes | **Class 4, Class 5, Class 6, Class 7, Class 8, Class 9, Class 10, Class 11, Class 12** |
| Total periods / week | **85** |

| Class | Type | Subject / Activity | Periods / Week |
| --- | --- | --- | ---: |
| Class 4 | Activity | Entrepreneurship | 15 |
| Class 5 | Activity | Entrepreneurship | 10 |
| Class 6 | Activity | Entrepreneurship | 10 |
| Class 7 | Activity | Entrepreneurship | 5 |
| Class 8 | Activity | Entrepreneurship | 5 |
| Class 9 | Subject | Accounts | 10 |
| Class 10 | Subject | Accounts | 10 |
| Class 11 | Subject | Accounts | 10 |
| Class 12 | Subject | Accounts | 10 |

### `RVS95` — Preeti Muchhal

| | |
| --- | --- |
| Classes | **Class 3, Class 4** |
| Total periods / week | **35** |

| Class | Type | Subject / Activity | Periods / Week |
| --- | --- | --- | ---: |
| Class 3 | Subject | Social Studies | 20 |
| Class 4 | Subject | Social Studies | 15 |

### `RVS115` — Ashish Thakur

| | |
| --- | --- |
| Classes | **Class 9, Class 10, Class 11, Class 12** |
| Total periods / week | **0** |

| Class | Type | Subject / Activity | Periods / Week |
| --- | --- | --- | ---: |
| Class 9 | Subject | Mathematics | 0 |
| Class 10 | Subject | Mathematics | 0 |
| Class 11 | Subject | Mathematics | 0 |
| Class 12 | Subject | Mathematics | 0 |

### `RVS136` — Shashi Shukla

| | |
| --- | --- |
| Classes | **Class 8, Class 9, Class 10, Class 11, Class 12** |
| Total periods / week | **45** |

| Class | Type | Subject / Activity | Periods / Week |
| --- | --- | --- | ---: |
| Class 8 | Subject | Physics | 5 |
| Class 9 | Subject | Physics | 10 |
| Class 10 | Subject | Physics | 10 |
| Class 11 | Subject | Physics | 10 |
| Class 12 | Subject | Physics | 10 |

### `RVS143` — Meenakshi Singh

| | |
| --- | --- |
| Classes | **Class 3, Class 4, Class 5** |
| Total periods / week | **0** |

| Class | Type | Subject / Activity | Periods / Week |
| --- | --- | --- | ---: |
| Class 3 | Subject | Computer | 0 |
| Class 4 | Subject | Computer | 0 |
| Class 5 | Subject | Computer | 0 |

### `RVS148` — Neelam Pawar Digarse

| | |
| --- | --- |
| Classes | **Class 1, Class 2, Class 3, Class 6** |
| Total periods / week | **70** |

| Class | Type | Subject / Activity | Periods / Week |
| --- | --- | --- | ---: |
| Class 1 | Subject | Computer | 20 |
| Class 2 | Subject | Computer | 20 |
| Class 3 | Subject | Computer | 20 |
| Class 6 | Subject | Computer | 10 |

### `RVS149` — Pratha Choubey

| | |
| --- | --- |
| Classes | **Class 2** |
| Total periods / week | **40** |

| Class | Type | Subject / Activity | Periods / Week |
| --- | --- | --- | ---: |
| Class 2 | Subject | Communication | 20 |
| Class 2 | Subject | E.V.S. | 20 |

### `RVS150` — Sugatha Unnithan

| | |
| --- | --- |
| Classes | **Class 1, Class 3** |
| Total periods / week | **0** |

| Class | Type | Subject / Activity | Periods / Week |
| --- | --- | --- | ---: |
| Class 1 | Subject | Communication | 0 |
| Class 3 | Subject | English | 0 |

### `RVS154` — Soma Dinda

| | |
| --- | --- |
| Classes | **Class 2, Class 4, Class 5** |
| Total periods / week | **60** |

| Class | Type | Subject / Activity | Periods / Week |
| --- | --- | --- | ---: |
| Class 2 | Subject | Communication | 20 |
| Class 4 | Subject | Communication | 15 |
| Class 4 | Subject | G.K./Moral | 15 |
| Class 5 | Subject | English | 10 |

### `RVS157` — Noshin Siddiqui

| | |
| --- | --- |
| Classes | **Class 3, Class 4** |
| Total periods / week | **35** |

| Class | Type | Subject / Activity | Periods / Week |
| --- | --- | --- | ---: |
| Class 3 | Subject | Science | 20 |
| Class 4 | Subject | Science | 15 |

### `RVS159` — Saksham Sharma

| | |
| --- | --- |
| Classes | **Class 8, Class 9, Class 10, Class 11, Class 12** |
| Total periods / week | **0** |

| Class | Type | Subject / Activity | Periods / Week |
| --- | --- | --- | ---: |
| Class 8 | Subject | Chemistry | 0 |
| Class 9 | Subject | Chemistry | 0 |
| Class 10 | Subject | Chemistry | 0 |
| Class 11 | Subject | Chemistry | 0 |
| Class 12 | Subject | Chemistry | 0 |

### `RVS172` — Swati Budholiya

| | |
| --- | --- |
| Classes | **Class 1** |
| Total periods / week | **40** |

| Class | Type | Subject / Activity | Periods / Week |
| --- | --- | --- | ---: |
| Class 1 | Subject | Computer | 20 |
| Class 1 | Subject | Hindi | 20 |

### `RVS173` — Shivendra Bopche

| | |
| --- | --- |
| Classes | **Class 1, Class 2, Class 3, Class 4, Class 5, Class 6, Class 7, Class 8** |
| Total periods / week | **105** |

| Class | Type | Subject / Activity | Periods / Week |
| --- | --- | --- | ---: |
| Class 1 | Activity | Music | 20 |
| Class 2 | Activity | Music | 20 |
| Class 3 | Activity | Music | 20 |
| Class 4 | Activity | Music | 15 |
| Class 5 | Activity | Music | 10 |
| Class 6 | Activity | Music | 10 |
| Class 7 | Activity | Music | 5 |
| Class 8 | Activity | Music | 5 |

### `RVS174` — Ishwari Dubey

| | |
| --- | --- |
| Classes | **Class 2, Class 3, Class 4, Class 5** |
| Total periods / week | **0** |

| Class | Type | Subject / Activity | Periods / Week |
| --- | --- | --- | ---: |
| Class 2 | Subject | Library | 0 |
| Class 3 | Subject | Library | 0 |
| Class 4 | Subject | Library | 0 |
| Class 5 | Subject | Library | 0 |

### `RVS188` — Kajal Shrivastava

| | |
| --- | --- |
| Classes | **Class 1, Class 2, Class 3, Class 4, Class 5, Class 6, Class 7, Class 9, Class 10, Class 11, Class 12** |
| Total periods / week | **170** |

| Class | Type | Subject / Activity | Periods / Week |
| --- | --- | --- | ---: |
| Class 1 | Activity | Games | 16 |
| Class 1 | Activity | Happy Feet | 8 |
| Class 1 | Activity | Karate | 8 |
| Class 2 | Activity | Happy Feet | 12 |
| Class 2 | Activity | Karate | 8 |
| Class 3 | Activity | Happy Feet | 8 |
| Class 3 | Activity | Karate | 8 |
| Class 4 | Activity | Karate | 6 |
| Class 4 | Activity | Robotics | 6 |
| Class 5 | Activity | Karate | 4 |
| Class 5 | Activity | Sanskrit | 8 |
| Class 5 | Subject | Science | 10 |
| Class 6 | Subject | Biology | 10 |
| Class 6 | Subject | Chemistry | 10 |
| Class 6 | Activity | Karate | 2 |
| Class 6 | Activity | Sanskrit | 4 |
| Class 7 | Subject | Biology | 5 |
| Class 7 | Subject | Chemistry | 5 |
| Class 9 | Activity | Games | 8 |
| Class 10 | Activity | Games | 8 |
| Class 11 | Activity | Games | 8 |
| Class 12 | Activity | Games | 8 |

### `RVS189` — Neha Guha

| | |
| --- | --- |
| Classes | **Class 9, Class 10, Class 11, Class 12** |
| Total periods / week | **0** |

| Class | Type | Subject / Activity | Periods / Week |
| --- | --- | --- | ---: |
| Class 9 | Subject | Commercial Studies | 0 |
| Class 10 | Subject | Commercial Studies | 0 |
| Class 11 | Subject | Business Studies | 0 |
| Class 12 | Subject | Business Studies | 0 |

### `RVS190` — Soumya Nair

| | |
| --- | --- |
| Classes | **Class 9, Class 10, Class 11, Class 12** |
| Total periods / week | **40** |

| Class | Type | Subject / Activity | Periods / Week |
| --- | --- | --- | ---: |
| Class 9 | Subject | English | 10 |
| Class 10 | Subject | English | 10 |
| Class 11 | Subject | English | 10 |
| Class 12 | Subject | English | 10 |

### `RVS195` — Anjali Singh Chauhan

| | |
| --- | --- |
| Classes | **Class 1, Class 2** |
| Total periods / week | **40** |

| Class | Type | Subject / Activity | Periods / Week |
| --- | --- | --- | ---: |
| Class 1 | Subject | E.V.S. | 20 |
| Class 2 | Subject | Communication | 20 |

### `RVT095` — Aleena Khan

| | |
| --- | --- |
| Classes | **Class 1, Class 3** |
| Total periods / week | **52** |

| Class | Type | Subject / Activity | Periods / Week |
| --- | --- | --- | ---: |
| Class 1 | Subject | Communication | 20 |
| Class 3 | Subject | Mathematics | 32 |

### `SPS26` — Leena Jain

| | |
| --- | --- |
| Classes | **Class 6, Class 7, Class 8, Class 9, Class 10** |
| Total periods / week | **50** |

| Class | Type | Subject / Activity | Periods / Week |
| --- | --- | --- | ---: |
| Class 6 | Subject | G.K./Moral | 10 |
| Class 6 | Subject | History & Civics | 10 |
| Class 7 | Subject | History & Civics | 5 |
| Class 8 | Subject | History & Civics | 5 |
| Class 9 | Subject | History & Civics | 10 |
| Class 10 | Subject | History & Civics | 10 |

---

## 5. Data-Quality Notes

### Zero-workload allocations

**142** active rows have `workload_per_week = 0`. See Excel sheet **Section Assignments** for full detail.

### Teaching staff with no active assignment

| Code | Name |
| --- | --- |
| `E2887` | Hema Pahuja |
| `GEN-1047` | Mehreen Hasan |
| `GEN-8962` | Jaskaran Kaur |
| `RVS13` | Yogita Menghani |
| `RVS28` | Kanchan Pinjare |
| `RVS55` | Rama Rajput |
| `RVS118` | Nitika Yadav |
| `RVS130` | Nidhi Namdeo |
| `RVS144` | Shaziya Khan |
| `RVS146` | Ruchi Sharma |
| `RVS175` | Rishita Chadar |
| `RVS185` | Anshika Mishra |

---

*Generated from `RIMS_DB_17_Backup_18_07.sql` · Class-level view · Full section detail in `TEACHER_CLASS_ALLOCATION.xlsx`*
