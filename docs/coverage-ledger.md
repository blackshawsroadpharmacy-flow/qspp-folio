# QSPP Manager — Seed Coverage Ledger (Milestone 1)

Retrieved 2026-10-19 from pack `~/Projects/qspp-pack` (pack README version: prepared 26 Sep 2026).
Line anchors are **approximate only** (clause-index convention ±20): pdftotext text flow with
`\f` page breaks; lines counted 1-indexed via Python `text.split('\n')`. Never cite them as
exact page numbers. Seed files: `seed/requirements.json`, `seed/issues.json`, `seed/documents.json`.

## 1. Requirement counts by kind

| Kind | Count | Coverage |
|---|---|---|
| STD | 40 | 28 subclauses (1.1.1–1.1.7, 1.2.1–1.2.6, 2.1.1–2.1.3, 2.2.1–2.2.5, 2.3.1–2.3.4, 2.4.1–2.4.3) + 12 sub-subclauses 2.2.3.1–2.2.3.12 |
| UAG | 26 | 25 sections (1.1–4.9) + 1 dedicated potency-cadence requirement (UAG 2.4 key task) |
| UAGKeyTask | 284 | Lettered actions quoted in each section's Standard Subclause block (letters from the quoted AS85000 subclauses; see §4 note) |
| RULE | 51 | Program Rules **1–51** (the brief said 1–50; the source verifies 51 — Rule 51 "General" definitions cross-reference) |
| TC | 23 | Terms & Conditions clauses 1–23 |
| POLICY | 8 | One per policy file (Accreditation-Revocation, Assessments, Exceptional Circumstances, Failure to Comply, Payment, Privacy, Procedural Fairness & Impartiality, Sanctions Management), each flagged `verify-todo` |
| OBLIGATION | 5 | Annual attestation (T&C Cl 5c), notification table (T&C Cl 9), fees/invoices strict 28-day terms (Rule 36–37 + Payment Policy), logo licence (T&C Cl 12), banner-group brand rule (Rule 49) |
| HITLIST | 14 | All readiness-hitlist items, `status: imported_unverified`, `ownersBlank: true`, note "planning item imported; not a confirmed deficiency" |
| **Total** | **451** | |

## 2. Verified standard structure (AS85000:2024, from `extracted/AS85000-2024-full.txt` body)

2 sections → 6 clauses → **28 subclauses** (+12 sub-subclauses under 2.2.3). Verified from the
document body, not the TOC:

| Clause | Subclause range (count) | Body anchor (approx lines) |
|---|---|---|
| 1.1 Clinical Governance | 1.1.1–1.1.7 (7) | 810–1039 |
| 1.2 Business Governance | 1.2.1–1.2.6 (6) | 1054–1186 |
| 2.1 Pharmacy Services | 2.1.1–2.1.3 (3) | 1192–1305 |
| 2.2 Safe & appropriate provision of medicines | 2.2.1–2.2.5 (5); 2.2.3 additionally carries 2.2.3.1–2.2.3.12 | 1316–1496 (2.2.3 body 1412–1455) |
| 2.3 Clinical safety | 2.3.1–2.3.4 (4) | 1503–1642 |
| 2.4 Business Operations | 2.4.1–2.4.3 (3) | 1649–1747 |

**Count discrepancy (verified):** the pack README says "26 sub-clauses" and knowledge-base.md §6
says "27 subclauses" — the extracted standard text proves **28** subclauses at the x.y.z level
(7+6+3+5+4+3), plus the 12 sub-subclauses of 2.2.3. Treat 28 (+12) as canonical.

## 3. UAG V1.1 verified structure — 25 sections

Mapping printed in the UAG TOC table (~lines 584–652; print layout interleaves two columns).
Each section has Intention / Outcome / Standard Subclause (quoted verbatim) / Reflective
Questions / Key Tasks / Resources / Monitoring & Evidence Examples.

| # | Ref | Title (body) | Maps to standard subclause(s) | Body anchor | TOC anchor |
|---|---|---|---|---|---|
| 1 | 1.1 | Governance, Leadership, and Culture | 1.1.1 | 749–901 | ~584 |
| 2 | 1.2 | Patient Safety and Quality Systems | 1.1.2 | 902–1308 | ~593 |
| 3 | 1.3 | Clinical Performance and Effectiveness | 1.1.3 | 1309–1492 | ~598 |
| 4 | 1.4 | Safe Environment for the Delivery of Care | 1.1.4 | 1493–2077 | ~605 |
| 5 | 2.1 | Delivery of Pharmacy Services | 2.1.1 | 2078–2430 | ~588 |
| 6 | 2.2 | Prescribing by Pharmacists (if offered) | 2.1.2 | 2431–2654 | ~589 |
| 7 | 2.3 | Safe and Appropriate Provision of Medicines, Medical Devices and Poisons | 2.2.2 | 2655–3046 | ~590 |
| 8 | 2.4 | Complex Compounding (if offered) | 2.2.3 (quotes 2.2.3.1–2.2.3.12 verbatim @3063–3113) | 3047–3481 | ~596 |
| 9 | 2.5 | Indirect Provision of Medicines | 2.2.4 | 3482–3662 | ~602 |
| 10 | 2.6 | Preventing and Controlling Infections | 2.3.1 | 3663–3987 | ~603 |
| 11 | 2.7 | Communicating for Safety | 2.3.3 | 3988–4151 | ~611 |
| 12 | 2.8 | Recognising and Responding to Serious Deterioration and Minimising Harm | 2.3.4 | 4152–4316 | ~612 |
| 13 | 3.1 | Partnering with Patients in their own Care | 1.1.5 | 4317–4494 | ~625 |
| 14 | 3.2 | Health Literacy | 1.1.6 | 4495–4619 | ~630 |
| 15 | 3.3 | Partnering with Consumers in Pharmacy Design and Governance | 1.1.7 | 4620–4735 | ~631 |
| 16 | 3.4 | Comprehensive Care | 2.3.2 | 4736–4948 | ~635 |
| 17 | 4.1 | Legal and Professional Obligations | 1.2.1 | 4949–5131 | ~628 |
| 18 | 4.2 | Business Viability | 1.2.2 | 5132–5324 | ~642 |
| 19 | 4.3 | Business Continuity Plan | 1.2.3 | 5325–5449 | ~643 |
| 20 | 4.4 | Pharmacy Premises | 1.2.4 | 5450–5602 | ~644 |
| 21 | 4.5 | Safety and Security | 1.2.5 & 2.4.2 | 5603–5786 | ~645 |
| 22 | 4.6 | Information Technology (IT) | 1.2.6 & 2.4.3 | 5787–5994 | ~646 |
| 23 | 4.7 | Advertising of Pharmacy Services and Therapeutic Goods | 2.1.3 & 2.2.5 | 5995–6179 | ~647 |
| 24 | 4.8 | Stock Management | 2.2.1 | 6180–6387 | ~649 |
| 25 | 4.9 | Human Resources (HR) | 2.4.1 | 6388–end | ~650 |

Both UAG copies state on their cover: `Date of publication: September 2026 (Version 1.1)`
(line 43 of both extracted texts). The hub text contains **no official "build 2" label** —
the hub copy is recorded as: `hub copy (filename Guide_V1.12.pdf), cover states Version 1.1 Sep 2026`.

## 4. What the letters actually are (verified nuance)

The lettered items (a., b., …) inside each UAG section are the **quoted AS85000 subclause
actions** in the "Standard Subclause" block (e.g. 1.1.1.1 The pharmacy shall: a–h @765–775) —
these are the atoms the hub's QC2020↔QSPP mapping page cross-references. The UAG's own
"Key Tasks" bullet lists are **not lettered** in the extraction. Where a quoted subclause
contains no lettered actions (3.3 → 1.1.7.1; 4.3 → 1.2.3.1–1.2.3.3), the requirement entry has
`lettersVerified: false` and no letter entries; everywhere else the letters were verified
contiguously (roman-numeral sub-items i./ii. are excluded from letter entries). 284 letter
entries total.

## 5. Potency cadence (recorded as an unresolved interpretation)

- Verbatim (UAG 2.4 Key Tasks, ~lines 3253–3259; `clause-index.md`'s ~3140s pointer is stale —
  3140s is Reflective Questions, the cadence sits at 3253–3259):
  "Establish a system for final product potency testing to ensure consistency, efficacy and
  quality. A final product potency test must be completed on two different drugs and two
  different dosage forms per compounding technician and compounding pharmacist per year."
- The source does **not** use "(2)" numerals or the "×" sign; the planning short-form kept in
  the seed is `two (2) different drugs × two (2) dosage forms per compounding technician AND
  per compounding pharmacist per year`, and the recorded interpretation is **per-person
  drug+form coverage — NOT four combinations of each**. Status `unresolved-confirm-with-manager`
  (`ISS-POTENCY-CADENCE`); QC2020 5.3.7 (@~3780–3788) said per-technician-only, the UAG adds the
  pharmacist cohort.

## 6. Not captured / unverified (deliberately marked, not invented)

1. **FAQ answers** — hub accordions collapsed at scrape time; only questions captured. Not in seeds.
2. **Full lettered QC2020↔UAG mapping** — hub mapping page partial only; NOT seeded anywhere.
3. **Hub-only pages** — hub corpus (`extracted/hub-pages-corpus.txt`, 71 pages) and
   `search-index.json` are in the pack but not imported as documents in `documents.json`
   (out of brief scope).
4. `capacity-toolkit.pdf` — excluded (221-byte HTML captcha wall; manifest kind `html-wall`);
   recorded with `excludedNotes` only.
5. **Policies seeded at purpose level** — one POLICY requirement per policy + version/approval
   lines; clause-level detail (e.g. sanctions ladder stages) is in the pack text only.
6. **UAG original vs hub copy diffs** — KB §8 claims ~73 non-whitespace line differences
   (resource wording/order); NOT reconciled line-by-line in this pass.
7. **Fee amounts** ($2,750 annual, $1,595 late cancellation, $682 virtual change, $374 EoS,
   $30/$10 certificate/decal, 1.95% Amex surcharge) come from the hub Fees page + Payment
   Policy/Rule 36; no fee-schedule PDF exists to hash-verify against.
8. Refresher-training certificate logo practice (QCPP logo legitimate; QSPP-logo = red flag) is
   KB-reported, not document-verified here (recorded as logo-obligation interpretation flag).

## 7. Documents seeded (documents.json)

22 entries; 21 with computed sha256 (python hashlib); download-hash cross-verified against
`website-scrape/downloads/manifest.json` where the manifest has one (17/17 matches).
The two UAG copies have **distinct** sha256 values (origin `documents/QSPP-User-Assessment-Guide_V1.1_Sep2026.pdf`
= 0d33f7f2…; hub mirror `website-scrape/downloads/Guide_V1.12.pdf` = 2717a071… per manifest, recompute matches).
AS85000 hub download (`as85000-2024.pdf`, b2e0bb75…) is byte-identical to the pack copy — one
canonical standard. The open-disclosure fact-sheet at pack root byte-matches the manifest copy.
Roles: standard 1, guide 2, rules 2, terms 1, policy 9, reference 2, guidance 5.

## 8. Milestone-2 import must still verify

- json.load all three seed files, recompute document hashes, confirm counts (451 requirements;
  STD 40; UAG 25 sections + cadence; RULE 51; TC 23; POLICY 8; OBL 5; HITLIST 14).
- Re-extract with `pdftotext -layout` to confirm the lettering model (quoted-subclause letters vs
  bulleted Key Tasks) matches the app's rendering intent; reconcile for the printed PDF.
- Resolve the three `issues.json` items with an authoritative source (QSPP support /
  interpretations@qspp.com / CDP re-scrape): cancellation window unit (business vs calendar
  days), mid-cycle window (months 15–21 vs 13–24 vs ≈18 months), potency cadence (confirm with
  pharmacist manager).
- Import per-policy clause detail and the fee schedule once a schedulable source exists.
- Import FAQ answers + the complete per-letter QC2020↔QSPP mapping after a hub re-scrape.
- Version-watch the UAG hub copy (both copies currently state Version 1.1, Sep 2026; no build
  label — re-check on each re-scrape and hash-diff).