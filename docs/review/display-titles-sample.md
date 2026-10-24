# Display titles: the 40-title sample (phase 3, for approval)

Phase 4 ships `RequirementDisplay.Resolve` over the whole 470-row corpus: **259** key-task
titles already read as human wording and need nothing. This sample covers everything the
deterministic fallback cannot finish: the fragments, the duplicate headings, and the hit
list rows whose display is still `Hit-List #N Pn`.

Rules (owner brief section 4, now in the service):

1. Editorial titles win when present (`seed/display-titles.json`, written after your approval).
2. Key-task wording is used verbatim when it reads as an instruction; capped at 70 characters
   at a word boundary; rejected when it starts as a subject fragment ("A substitute decision-maker…").
3. Structured headings (UAG section, standard subclause subject, rule heading, clause heading)
   are used when unique inside their kind.
4. Everything else keeps the seeded title and is flagged `NeedsHumanTitle`.

Kind labels now read: Standard, Assessment guide section, Key task, Program rule,
Terms and conditions, Policy, Obligation, Readiness hit list, Remote assessment.
Acronym map (detail panes and tooltips): UAG = User Assessment Guide, STD = the Standard
(AS 85000:2024), T&C = Terms and Conditions, REM = Remote assessment, Hit-List =
Readiness hit list, P1-P4 = Priority 1-4. 8CPA stays as written.

## What the service already fixes on the real corpus (no writing needed)

| Kind | Improved | Stays for you to approve |
|---|---|---|
| Key tasks | 259 of 284 verbatim | 25 |
| Standard subclauses | 36 of 40 clause subjects | 4 |
| UAG sections | 26 of 26 | 0 |
| Rules | 4 of 51 unique headings | 47 |
| Terms | 23 of 23 | 0 |
| Policies | 8 of 8 | 0 |
| Hit list | 1 of 14 verbatim | 13 |
| Obligations | 5 of 5 human already | 0 |
| Remote assessment | 19 of 19 human already | 0 |

## Proposed titles (approve the voice, not each row)

Columns: the seeded title today, what the service shows until you approve, the proposal.

### 20 key tasks across the four domains

| Ref | Today | Until approved the list shows | Proposed |
|---|---|---|---|
| UAG 1.1 1.1.1.1 task d | UAG 1.1 section 1.1 … quoted subclause | Set priorities for safe and quality clinical care and ensure … | Set priorities for safe, quality clinical care |
| UAG 1.1 1.1.1.1 task f | (same wall) | Clearly define the safety and quality roles, responsibilities … | Define safety and quality roles and accountabilities |
| UAG 1.2 1.1.2.1 task c | (wall) | Monitor and take action to improve adherence to policies, … | Monitor and improve adherence to policies and procedures |
| UAG 2.1 2.1.1.9 task a | (wall) | The records are appropriate for the service that is delivered | Keep records appropriate to the service delivered |
| UAG 2.1 2.1.1.9 task b | (wall) | The recording occurs in a consistent manner | Record in a consistent manner |
| UAG 2.1 2.1.1.9 task c | (wall) | The records are securely stored and in compliance with legislation | Store records securely and lawfully |
| UAG 2.1 2.1.1.9 task d | (wall) | The nature of the records is compliant with relevant professional… | Keep records compliant with practice guidelines and contracts |
| UAG 2.6 2.3.1.10 task b | (wall) | A process for critical equipment, instruments and devices… | Maintain an identified register of critical equipment |
| UAG 3.1 1.1.5.2 task a | (wall) | The capacity of a patient to make decisions about their own care | Assess capacity to make decisions about own care |
| UAG 3.1 1.1.5.2 task b | (wall) | A substitute decision-maker if a patient does not have… | Identify a substitute decision-maker where capacity is lacking |
| UAG 3.2 1.1.6.2 task a | (wall) | The services available | Document the services available |
| UAG 3.2 1.1.6.2 task c | (wall) | Who can access the services | Document who can access the services |
| UAG 3.2 1.1.6.2 task g | (wall) | Mechanism for providing feedback and contact details… | Provide a feedback mechanism and complaints contact |
| UAG 3.4 2.3.2.4 task c | (wall) | Who are receiving palliative or end of life care | Identify patients receiving palliative or end of life care |
| UAG 4.2 1.2.2.1 task a | (wall) | The details of all pharmacy owners, business partnerships… | Record all pharmacy owners and partnership agreements |
| UAG 4.2 1.2.2.2 task b | (wall) | The resources (e.g., funds, finance, staff, stock…) | Resource the business plan (funds, staff, stock, equipment) |
| UAG 4.5 2.4.2.1 task b | (wall) | How to report risks, dangerous and hazardous situations… | Report risks and hazardous situations |
| UAG 4.5 2.4.2.2 task a | (wall) | The pharmacy should have access to a defibrillator… | Keep a defibrillator accessible to pharmacy staff |
| UAG 4.9 2.4.1.1 task a | (wall) | A recruitment procedure | Maintain a recruitment procedure |
| UAG 4.9 2.4.1.3 task a | (wall) | An induction process that is completed within three months… | Complete induction within three months of commencement |

### 6 standard subclauses

| Ref | Today | Proposed |
|---|---|---|
| 1.2.2 | 1.2.2 — clause 1.2 Legal Obligations | Maintain business plans and strategies |
| 1.2.5 | 1.2.5 — clause 1.2 Legal Obligations | Identify and manage hazardous situations |
| 1.2.6 | 1.2.6 — clause 1.2 Legal Obligations | Select and manage IT systems and access |
| 2.2.3.10 | 2.2.3.10 — clause 2.2.3 Complex Compounding | Assign beyond-use dates to compounded products |
| 2.4.2 | 2.4.2 — clause 2.4 Business Management | Educate staff on dangerous situations and emergencies |
| 2.4.3 | 2.4.3 — clause 2.4 Business Management | Control access to IT equipment, systems and data |

Notes: 1.2.5 and 2.4.2 both carry the clause subject "Safety and security"; 1.2.6 and 2.4.3
both carry "Information technology (IT)". Their clause subjects collide, so no deterministic
title exists for them; the proposals separate them.

### 6 rules

| Ref | Today | Proposed |
|---|---|---|
| Rule 10 | Rule 10 — QSPP Assessor | Assessor: book, reschedule or cancel rules |
| Rule 11 | Rule 11 — Accreditation assessments | Assessment method: compliant or non-compliant |
| Rule 18 | Rule 18 — Accreditation assessments | Separate-business space excluded when Rule 17 not met |
| Rule 21 | Rule 21 — Accreditation assessments | Assessor may view commercial-in-confidence evidence |
| Rule 23 | Rule 23 — Accreditation assessments | Prior assessment outcomes are considered |
| Rule 29 | Rule 29 — Corrective Actions | Missed corrective action deadlines escalate |

Notes: 47 of 51 rules share a heading with other rules ("Accreditation assessments" 13,
"Corrective Actions" 7, "Assessment bookings" 5, "Feedback and complaints" 4,
"Additional assessments" 4, "Review of Decision" 3, "Sanctions" 3, plus three pairs), so
headings alone cannot title them. The four rules with unique headings already display them
(Pharmacy conduct and three more).

### 8 hit list rows (the priorities 1 and 2 set)

| Ref | Today | Proposed |
|---|---|---|
| Hit-1 (P1) | Hit-List #1 P1 | Beyond-use dating procedure |
| Hit-2 (P1) | Hit-List #2 P1 | Chemical spill kits: supply, training, log |
| Hit-3 (P1) | Hit-List #3 P1 | Annual competency program for all compounding staff |
| Hit-4 (P1) | Hit-List #4 P1 (if sterile offered) | Garbing and aseptic competency evidence |
| Hit-5 (P2) | Hit-List #5 P2 | Single equipment governance calendar |
| Hit-6 (P2) | Hit-List #6 P2 | Hazardous-compounding staff health monitoring |
| Hit-7 (P2) | Hit-List #7 P2 | Supplier register and agreements |
| Hit-8 (P2) | Hit-List #8 P2 | Counselling SOP for compounded medicines |

The remaining six (Hit-9 to Hit-14, priorities 3 and 4) keep either their verbatim summary
(Hit-9: "Batch records template for permitted batching runs") or the seeded title until the
second batch. Their summaries are long enough that I would draft them the same way.

## What approval unlocks

On your word I turn the 40 proposal columns into `seed/display-titles.json`, keep the
`Resolve` wiring for screens (phase 4: Requirements list and detail, Today, palette), and
draft the remaining 5 rules + 6 hit lists in the same voice. Nothing ships to the list
views until then; today's builds keep the seeded titles everywhere.