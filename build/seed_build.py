# Build seed corpus JSON for the Hugh's QSPP Manager app (Milestone-1 seed data).
# Reads ONLY from ~/Projects/qspp-pack (read-only source). Writes into ~/Projects/qspp-manager.
import re, json, hashlib, os, io

PACK = '/home/vpnhugh/Projects/qspp-pack'
OUT = '/home/vpnhugh/Projects/qspp-manager'
os.makedirs(OUT + '/seed', exist_ok=True)
os.makedirs(OUT + '/docs', exist_ok=True)

def loadtxt(rel):
    with io.open(os.path.join(PACK, rel), encoding='utf-8') as f:
        return f.read()

def lines_of(rel):
    return loadtxt(rel).split('\n')

def clean(s):
    return ' '.join(s.split())

def trim400(s):
    s = clean(s)
    return s if len(s) <= 400 else s[:397].rstrip() + '...'

def cap160(s):
    return s if len(s) <= 160 else s[:157].rstrip() + '...'

ANCHOR_NOTE = ("approximate line anchor: pdftotext extraction, lines counted with "
               "text.split('\\n') (1-indexed); treat as ±20 (clause-index convention)")

def anchor(file, a, b, note=None):
    return {'file': file, 'approxLines': [int(a), int(b)], 'note': note or ANCHOR_NOTE}

# ---------------------------------------------------------------- AS85000 STD
as_lines = lines_of('extracted/AS85000-2024-full.txt')
STD_REFS = [
    ('1.1', 'Clinical Governance', [('1.1.1', 'Governance, leadership, and culture'),
                                    ('1.1.2', 'Patient safety and quality systems'),
                                    ('1.1.3', 'Clinical performance and effectiveness'),
                                    ('1.1.4', 'Safe environment for the delivery of care'),
                                    ('1.1.5', 'Partnering with patients in their own care'),
                                    ('1.1.6', 'Health literacy'),
                                    ('1.1.7', 'Partnering with consumers in pharmacy design and governance')]),
    ('1.2', 'Business Governance', [('1.2.1', 'Legal and professional obligations'),
                                    ('1.2.2', 'Business viability'),
                                    ('1.2.3', 'Business continuity plan'),
                                    ('1.2.4', 'Pharmacy premises'),
                                    ('1.2.5', 'Safety and security'),
                                    ('1.2.6', 'Information technology (IT)')]),
    ('2.1', 'Pharmacy Services', [('2.1.1', 'Delivery of pharmacy services'),
                                  ('2.1.2', 'Prescribing by pharmacists'),
                                  ('2.1.3', 'Advertising of pharmacy services')]),
    ('2.2', 'Safe and appropriate provision of medicines', [('2.2.1', 'Stock management'),
                                                            ('2.2.2', 'Safe and appropriate provision of medicines, medical devices and poisons'),
                                                            ('2.2.3', 'Complex compounding (if offered)'),
                                                            ('2.2.4', 'Indirect provision of medicines'),
                                                            ('2.2.5', 'Advertising of therapeutic goods')]),
    ('2.3', 'Clinical safety', [('2.3.1', 'Preventing and controlling infections'),
                                ('2.3.2', 'Comprehensive care'),
                                ('2.3.3', 'Communicating for safety'),
                                ('2.3.4', 'Recognising and responding to serious deterioration and minimising harm')]),
    ('2.4', 'Business Operations', [('2.4.1', 'Human resources'),
                                    ('2.4.2', 'Safety and security'),
                                    ('2.4.3', 'Information technology (IT)')]),
]
DOM = {'1.1': '1', '1.2': '4', '2.1': '2', '2.2': '2', '2.3': '2', '2.4': '4'}
all_subclause_refs = [ref for _, _ct, subs in STD_REFS for ref, _t in subs]
HINT = {'2.1.2': 'if_prescribing', '2.2.3': 'if_compounding', '2.2.4': 'if_indirect'}
STD_FALLBACK_SUMMARY = {
    '1.1.6': "Subclause 1.1.6 'Health literacy' — lettered sub-requirements quoted verbatim in wording.",
    '1.1.7': "Subclause 1.1.7 'Partnering with consumers in pharmacy design and governance' — the pharmacy shall work with consumers on design and governance.",
    '1.2.4': "Subclause 1.2.4 'Pharmacy premises' — premises must support safe, effective service delivery (see lettered requirements in wording).",
}
# locate subclause heading lines in body (skip TOC at ~213-290)
std_head = {}
for i, ln in enumerate(as_lines, 1):
    if i <= 300: continue
    m = re.match(r'^(\d\.\d\.\d)\s+(\S.*)$', ln.strip())
    if m and m.group(1) in all_subclause_refs:
        std_head.setdefault(m.group(1), (i, m.group(2).strip()))
all_refs = [s for _, _, subs in STD_REFS for s in subs]
order = sorted(std_head.items(), key=lambda kv: kv[1][0])
std = []
for k, (ref, (st, title)) in enumerate(order):
    nxt = order[k + 1][1][0] if k + 1 < len(order) else 1800
    body_lines = []
    for j in range(st - 1, min(nxt, len(as_lines))):
        t = as_lines[j].strip()
        if not t or '©The Pharmacy Guild' in t: continue
        if re.match(r'^p \d+ of \d+$', t) or re.match(r'^\d{1,3}$', t): continue
        body_lines.append(t)
    lead = []
    for x in body_lines[1:]:
        if re.match(r'^(NOTE:|Note:)', x) or re.match(r'^\d\.\d\.\d+\s', x): break
        lead.append(x)
        if x.endswith('.'): break
    lead_txt = ' '.join(' '.join(lead).split())
    m = re.match(r'^([^.:]{10,}?\\.)', lead_txt)
    first_sent = lead_txt[:158]
    sub_items = sorted(set(re.findall(r'^(\d\.\d\.\d+\.\d+)', '\n'.join(body_lines))))
    clause_ref = ref.rsplit('.', 1)[0]
    clause_title = next((ct for c, ct, subs in STD_REFS if c == clause_ref), '')
    dom = DOM[clause_ref]
    hint = HINT.get(ref, 'always')
    if ref == '2.2.3': hint = 'if_compounding'
    wording = '%s %s — %s' % (ref, title, lead_txt if lead_txt else ' '.join(body_lines[1:]))
    if ref in STD_FALLBACK_SUMMARY:
        summ = STD_FALLBACK_SUMMARY[ref]
    else:
        summ = trim400(first_sent).rstrip('.') + '.' if first_sent else ('Subclause %s of AS85000:2024 — see wording.' % ref)
    if len(summ) > 158: summ = summ[:155].rstrip() + '...' if False else summ[:156] + '...' if len(summ) > 160 else summ
    entry = {
        'id': 'STD-%s' % ref, 'refKind': 'STD', 'ref': ref,
        'title': '%s — clause %s %s' % (ref, ref if False else clause_ref, clause_title),
        'sourceDoc': 'AS85000-2024_Australian-Community-Pharmacy-Standard.pdf',
        'sourceVersion': 'AS85000:2024 (published June 2024; ISBN 978-1-76139-672-4)',
        'anchor': anchor('extracted/AS85000-2024-full.txt', st, max(st, nxt - 1)),
        'wording': trim400(wording), 'summary': summ[:160], 'domain': dom,
        'applicabilityHint': hint, 'evidenceExamples': [], 'interpretationFlags': [],
        'subclauseTitle': title, 'clauseRef': clause_ref, 'numberedSubItems': sub_items,
    }
    if ref == '2.2.3':
        entry['interpretationFlags'].append('clause-lead-in-quoted-in-UAG-2.4')
    if ref == '2.1.2': entry['interpretationFlags'].append('new-subclause-in-2024-standard')
    std.append(entry)

# sub-subclause entries for 2.2.3.1-2.2.3.12 (the compounding core; each is a distinct requirement)
std23 = next(e for e in std if e['ref'] == '2.2.3')
st23, end23 = std23['anchor']['approxLines']
sub23 = []
for j in range(st23 - 1, end23):
    t = as_lines[j].strip()
    m = re.match(r'^(2\.2\.3\.\d+)\s+(.*)$', t)
    if m: sub23.append((m.group(1), m.group(2).strip(), j + 1))
for k, (num, firstline, ln0) in enumerate(sub23):
    ln_end = sub23[k + 1][2] - 1 if k + 1 < len(sub23) else end23
    seg = [clean(x) for x in as_lines[ln0-1:ln_end] if x.strip() and '©' not in x
           and not re.match(r'^p \d+ of \d+$', clean(x)) and not re.match(r'^\d{1,3}$', clean(x))]
    body = ' '.join(seg)
    std.append({
        'id': 'STD-%s' % num, 'refKind': 'STD', 'ref': num,
        'title': '%s (clause 2.2.3 Complex compounding (if offered))' % num,
        'sourceDoc': 'AS85000-2024_Australian-Community-Pharmacy-Standard.pdf',
        'sourceVersion': 'AS85000:2024 (published June 2024; ISBN 978-1-76139-672-4)',
        'anchor': anchor('extracted/AS85000-2024-full.txt', ln0, ln_end),
        'wording': trim400(body),
        'summary': cap160('Complex compounding %s: %s' % (num, body[:120])),
        'domain': '2', 'applicabilityHint': 'if_compounding',
        'evidenceExamples': [],        'interpretationFlags': ['mapped-in-compounding-crosswalk',
                                'quoted-verbatim-in-UAG-2.4'],
        'subclauseTitle': num, 'clauseRef': '2.2.3',
        'numberedSubItems': re.findall(r'([a-h])\.\s+[A-Z]', body),
    })

# tighten the last subclause anchor (2.4.3 ends before the appendices)
_last = next(e for e in std if e['ref'] == '2.4.3')
_st = _last['anchor']['approxLines'][0]
_apx = next(i for i, l in enumerate(as_lines, 1) if i > _st and l.strip().startswith('6. Appendices'))
_last['anchor']['approxLines'] = [_st, _apx - 1]

# ---------------------------------------------------------------- UAG sections
uag_lines = lines_of('extracted/UAG-V1.1-full.txt')
uag_meta = json.load(open('/home/vpnhugh/.hermes/cache/scratch/qsppbuild/uag_sections.json'))
UAG_DOM = {}
for sec in ['1.1', '1.2', '1.3', '1.4']: UAG_DOM[sec] = '1'
for sec in ['2.1', '2.2', '2.3', '2.4', '2.5', '2.6', '2.7', '2.8']: UAG_DOM[sec] = '2'
for sec in ['3.1', '3.2', '3.3', '3.4']: UAG_DOM[sec] = '3'
for sec in ['4.1', '4.2', '4.3', '4.4', '4.5', '4.6', '4.7', '4.8', '4.9']: UAG_DOM[sec] = '4'
UAG_HINT = {'2.2': 'if_prescribing', '2.4': 'if_compounding', '2.5': 'if_indirect'}
TOC_LINES = {'1.1': 584, '1.2': 593, '1.3': 598, '1.4': 605, '2.1': 588, '2.2': 589,
             '2.3': 590, '2.4': 596, '2.5': 602, '2.6': 603, '2.7': 611, '2.8': 612,
             '3.1': 625, '3.2': 630, '3.3': 631, '3.4': 635,
             '4.1': 628, '4.2': 642, '4.3': 643, '4.4': 644, '4.5': 645, '4.6': 646,
             '4.7': 647, '4.8': 649, '4.9': 650}
starts = sorted(meta['startLine'] for meta in uag_meta)
def para(after, nxt, L):
    out = []
    for j in range(after, nxt):
        t = L[j].strip()
        if not t: continue
        if t in ('Outcome', 'Intention', 'Standard Subclause', 'Reflective Questions',
                 'Key Tasks', 'Monitoring and Evidence Examples', 'Resources and Information'):
            break
        out.append(t)
        if len(out) >= 5: break
    return ' '.join(' '.join(out).split())

uag_entries, kt_entries = [], []
for meta in uag_meta:
    sec, st = meta['ref'], meta['startLine']
    nxt = next((s for s in starts if s > st), len(uag_lines))
    heading = uag_lines[st - 1].strip()
    extra = uag_lines[st].strip() if st < len(uag_lines) else ''
    title = clean(heading + (' ' + extra if extra and not re.match(r'^(Intention|Outcome)$', extra) else ''))
    intention = para(meta['headings'].get('Intention', st) if meta['headings'].get('Intention') else st, nxt, uag_lines) or title
    toc_txt = clean(uag_lines[TOC_LINES[sec] - 1] + ' ' + uag_lines[TOC_LINES[sec]] if False else uag_lines[TOC_LINES[sec] - 1])
    # monitoring & evidence bullets (first 3)
    ev = []
    me = meta['headings'].get('Monitoring and Evidence Examples')
    if me:
        cur = []
        for j in range(me, min(nxt, len(uag_lines))):
            t = uag_lines[j].strip()
            if not t or '©The Pharmacy Guild' in t or re.match(r'^\d{1,3}$', t) or t.startswith('Resources and Information'):
                if cur: ev.append(clean(' '.join(cur))); cur = []
                if t.startswith('Resources'): break
                continue
            if t.startswith('•'):
                if cur: ev.append(clean(' '.join(cur)))
                cur = [t.lstrip('•').strip()]
            elif cur:
                cur.append(t)
            if len(ev) >= 3: break
        if cur and len(ev) < 3: ev.append(clean(' '.join(cur)))
    # letters grouped by numbered quoted subclause, contiguity-checked
    groups, cur_ref, expected = [], None, 0
    alpha = 'abcdefghijklmnopqrstuvwxyz'
    for l in meta['letters']:
        seg = l['t']
        if re.match(r'^\d+\.\d+(\.\d+)*\s', seg):  # safety (never in letters field)
            continue
        pass
    # rebuild from ss block text to also know the numbered ref
    ss_text_lines = []
    for j in range(meta['headings'].get('Standard Subclause', st), meta['ssEnd']):
        ss_text_lines.append(uag_lines[j].strip())
    cur_ref, expected = None, 0
    known_heads = ('Intention', 'Outcome', 'Standard Subclause', 'Reflective Questions',
                   'Key Tasks', 'Monitoring and Evidence Examples', 'Resources and Information')
    for j, raw in enumerate(ss_text_lines):
        t = clean(raw)
        if not t or t.startswith('©') or re.match(r'^p \d+ of \d+$', t) or re.match(r'^\d{1,3}$', t) or t in known_heads:
            continue
        m = re.match(r'^((?:\d+\.)*\d+\.\d+(?:\.\d+)*)\s+(.*)$', t)
        if m and not re.match(r'^[a-z]\.\s', t):
            cur_ref = m.group(1); expected = 0; continue
        ml = re.match(r'^([a-z])\s*\.\s+(.*)$', t)
        if ml and cur_ref:
            letter = ml.group(1)
            if letter == alpha[expected]:
                # consume continuation lines (wrapped sentence) up to 400 chars
                seg = [ml.group(2)]
                used = 1
                for j2 in range(j + 1, min(j + 6, len(ss_text_lines))):
                    t2 = clean(ss_text_lines[j2])
                    if (not t2 or t2.startswith('©') or re.match(r'^p \d+ of \d+$', t2) or re.match(r'^\d{1,3}$', t2)
                            or t2 in known_heads or re.match(r'^([a-z])\s*\.\s', t2)
                            or re.match(r'^((?:\d+\.)*\d+\.\d+(?:\.\d+)*)\s+\S', t2)
                            or re.match(r'^(NOTE|Note):', t2)):
                        break
                    seg.append(t2); used += 1
                groups.append((cur_ref, letter, ' '.join(seg), meta['headings'].get('Standard Subclause', st) + j + 1))
                expected += 1
            # non-contiguous (roman numerals i./ii.) -> skip silently
    letters_ok = len(groups) > 0
    kt_summary = []
    for (num, letter, text, ln) in groups:
        kt = {
            'id': 'UAGKT-%s-%s-%s' % (sec, num.replace('.', '_'), letter),
            'refKind': 'UAGKeyTask', 'ref': 'UAG %s %s task %s' % (sec, num, letter),
            'title': 'UAG %s section %s — quoted subclause %s action %s' % (sec, clean(heading), num, letter),
            'sourceDoc': 'QSPP-User-Assessment-Guide_V1.1_Sep2026.pdf',
            'sourceVersion': 'V1.1 — Date of publication: September 2026 (Version 1.1)',
            'anchor': anchor('extracted/UAG-V1.1-full.txt', ln, min(nxt, ln + 4)),
            'wording': trim400('%s. %s' % (letter, clean(text))),
            'summary': 'Quoted AS85000:2024 subclause %s action %s, reprinted in UAG section %s Standard Subclause block.' % (num, letter, sec),
            'domain': UAG_DOM[sec], 'applicabilityHint': UAG_HINT.get(sec, 'always'),
            'evidenceExamples': [], 'interpretationFlags': ['quoted-standard-shall-action'],
            'mandatory': True, 'mandatoryText': True,
        }
        kt_entries.append(kt)
        kt_summary.append({'ref': 'UAG %s %s task %s' % (sec, num, letter), 'line': ln})
    ev_objs = [{'text': e[:200], 'mandatory': False} for e in ev[:3]]
    uag_entry = {
        'id': 'UAG-%s' % sec, 'refKind': 'UAG', 'ref': 'UAG %s' % sec,
        'title': '%s — maps to standard clause %s' % (title, meta['stdSubclause']),
        'sourceDoc': 'QSPP-User-Assessment-Guide_V1.1_Sep2026.pdf',
        'sourceVersion': 'V1.1 — Date of publication: September 2026 (Version 1.1)',
        'anchor': anchor('extracted/UAG-V1.1-full.txt', st, nxt - 1),
        'wording': trim400('UAG %s %s (standard subclauses %s). %s' % (sec, title, meta['stdSubclause'], intention)),
        'summary': ('UAG section %s "%s": maps to AS85000 clause %s; assess Intention/Outcome, reflective questions, key tasks, evidence.' % (sec, title, meta['stdSubclause']))[:158],
        'domain': UAG_DOM[sec], 'applicabilityHint': UAG_HINT.get(sec, 'always'),
        'evidenceExamples': ev_objs,
        'interpretationFlags': (['toctoc'] if False else []) + ['key-task-letters-from-quoted-subclauses'],
        'stdSubclauseMap': meta['stdSubclause'],
        'lettersVerified': letters_ok,
        'noLettersNote': (None if letters_ok else 'quoted subclause(s) contain no lettered actions (verified: %s)' % (', '.join(groups) and 'n/a' or 'no lettered lines found in Standard Subclause block')),
        'keyTasks': kt_summary,
        'tocAnchor': anchor('extracted/UAG-V1.1-full.txt', TOC_LINES[sec], TOC_LINES[sec] + 2, 'TOC mapping table line (print layout: two columns interleave; ±20)'),
    }
    if sec == '2.4':
        uag_entry['interpretationFlags'] += ['if-offered-section-skipped-when-not-applicable-Rule-15']
    uag_entries.append(uag_entry)

# dedicated potency cadence requirement (UAG 2.4 Key Tasks, verified verbatim)
kt_block = [x for x in uag_lines[3248:3259] if x.strip()]
pot_quote = clean(' '.join(l.strip().lstrip('•').strip() if l.strip().startswith('•') else l.strip() for l in uag_lines[3252:3259]))
uag_entries.append({
    'id': 'UAG-2.4-potency-cadence', 'refKind': 'UAG',
    'ref': 'UAG 2.4 key task (final product potency testing cadence)',
    'title': 'UAG 2.4 potency testing cadence (Key Task)',
    'sourceDoc': 'QSPP-User-Assessment-Guide_V1.1_Sep2026.pdf',
    'sourceVersion': 'V1.1 — Date of publication: September 2026 (Version 1.1)',
    'anchor': anchor('extracted/UAG-V1.1-full.txt', 3253, 3259,
                     'verified verbatim; clause-index.md approximates ~3140s — actual 3253-3259'),
    'wording': trim400(pot_quote),
    'summary': 'Potency test MUST be done on two different drugs and two different dosage forms per compounding technician AND per compounding pharmacist per year.',
    'domain': '2', 'applicabilityHint': 'if_compounding',
    'evidenceExamples': [{'text': 'Evidence of the pharmacy\u2019s procedure for final product potency testing. (UAG 2.4 evidence, ~line 3404)',
                          'mandatory': True},
                         {'text': 'Records of the outcomes from final product potency tests completed. (UAG 2.4 evidence, ~line 3406)',
                          'mandatory': True}],
    'interpretationFlags': ['potency-cadence-unresolved-confirm-with-manager',
                            'per-person-drug-and-form-coverage-NOT-four-combinations',
                            'cadence-quote-shortform-two-(2)-drugs-x-two-(2)-dosage-forms-per-technician-AND-per-pharmacist-per-year',
                            'qc2020-5.3.7-per-technician-only-uag-adds-pharmacist'],
})

# ---------------------------------------------------------------- Program Rules
ru = lines_of('extracted/QSPP-Program-Rules-V1.0-Aug2026.txt')
rule_secs = {9: 'Pharmacy Conduct', 16: 'Accreditation', 28: 'Assessment bookings',
             58: 'QSPP Assessor', 76: 'Accreditation assessments', 176: 'Additional assessments',
             216: 'Corrective Actions', 263: 'Revoking or suspending accreditation', 276: 'Fees',
             327: 'Feedback and complaints', 358: 'Sanctions', 372: 'Review of Decision',
             392: 'Website and Portal', 408: 'Promoting Accreditation', 415: 'General'}
rule_starts = []
cur_sec = None
for i, ln in enumerate(ru, 1):
    t = ln.strip()
    if i in rule_secs: cur_sec = rule_secs[i]
    m = re.match(r'^Rule (\d+)$', t)
    if m and i > 8: rule_starts.append((int(m.group(1)), i, cur_sec))
rule_entries = []
for k, (num, st, sec) in enumerate(rule_starts):
    nxt = rule_starts[k + 1][1] if k + 1 < len(rule_starts) else 421
    body = []
    for j in range(st, min(nxt - 1, len(ru))):
        t = ru[j].strip()
        if not t or '©' in t or re.match(r'^QSPP Program Rules V1\.0', t) or re.match(r'^p \d+ of \d+$', t): continue
        if t in rule_secs.values(): break
        body.append(clean(t))
    txt = ' '.join(body)
    rule_entries.append({
        'id': 'RULE-%d' % num, 'refKind': 'RULE', 'ref': 'Rule %d' % num,
        'title': 'Rule %d — %s' % (num, sec or 'Program Rules'),
        'sourceDoc': 'QSPP-Program-Rules-V1.0-Aug2026.txt (PDF: QSPP-Program-Rules-V1.0-August-2026.pdf)',
        'sourceVersion': 'V1.0 August 2026',
        'anchor': anchor('extracted/QSPP-Program-Rules-V1.0-Aug2026.txt', st, max(st, nxt - 1)),
        'wording': trim400(txt),
        'summary': (' '.join(txt.split('.')[0:1])[:152] + ('...' if len(txt) > 155 else ''))[:160],
        'domain': 'program', 'applicabilityHint': 'always', 'evidenceExamples': [],
        'interpretationFlags': ['rule-section-%s' % (sec or 'General').lower().replace(' ', '-')],
    })
# rules with special flags
SPECIAL = {
    8: ['mid-cycle-window-unresolved-see-issues-ISS-MIDCYCLE-WINDOW'],
    10: ['cancellation-window-unresolved-see-issues-ISS-CANCEL-WINDOW', 'conservative-planning-10-business-days'],
    13: ['owner-physically-present-onsite'],
    17: ['adjoining-business-rules-with-18'],
    26: ['additional-assessments-incl-unannounced'],
    28: ['nc-remediation-30-days'],
    31: ['evidence-de-identified-PII-deleted'],
    33: ['major-nc-no-onsite-closure-rca-required'],
    36: ['fee-table-includes-10-business-day-cancellation-fee'],
    49: ['banner-group-100-percent-accredited-brand-rule'],
}
for r in rule_entries:
    num = int(r['id'].split('-')[1])
    if num in SPECIAL: r['interpretationFlags'] += SPECIAL[num]
    # better summaries for key rules
if rule_entries:
    summ_override = {
        4: 'Annual Invoice must be paid before booking an initial assessment; portal access follows payment.',
        5: 'Re-accreditation assessments must occur within 3 calendar months of the Accreditation Expiry Date unless otherwise agreed.',
        8: 'Mid-Cycle Review: remote assessment in year 2 — Rule 8 states month 15 to month 21 of the 36-month cycle.',
        10: 'Cancellation/reschedule within ten (10) business days of Assessment Date (or 2 business days virtual) triggers full assessment cost.',
        11: 'Assessments use compliant/non-compliant methodology against AS85000:2024.',
        13: 'Owner must be physically present for the whole onsite assessment; absence cancels it (fee applies).',
        15: 'Non-applicable UAG sub-domains are not assessed.',
        26: 'Additional assessments: supplementary (max 2 per year, random), unannounced, EoS, Mandatory Re-Assessment.',
        28: '30 days from interim report to remediate Non-Conformances; extensions discretionary via portal, only before due date.',
        31: 'Evidence must be de-identified; evidence containing personal information is deleted unread.',
        33: 'Major NC cannot be closed onsite; root cause analysis + recurrence prevention via portal.',
        36: 'Fees table: invoices payable at defined stages incl cancellation fee within 10 business days and EoS invoice.',
        37: 'QSPP invoices are issued on strict 28-day terms.',
        43: 'Pharmacy pays costs of QSPP sanctions/investigations.',
        44: 'Ignoring a sanction escalates it (next stage applied).',
        49: 'Banner/marketing groups may use QSPP Brand only when 100% of member pharmacies are QSPP accredited; 6-month grace for joiners.',
        51: 'T&C definitions carry into the Rules; official definitions table follows (Mid-Cycle Review there says months 13-24).',
    }
    for r in rule_entries:
        num = int(r['id'].split('-')[1])
        if num in summ_override: r['summary'] = summ_override[num][:160]

# ---------------------------------------------------------------- T&C clauses
tc = lines_of('extracted/QSPP-Terms-Conditions-V1.0-Aug2026.txt')
tc_head = [(i + 1, l.strip()) for i, l in enumerate(tc) if re.match(r'^Clause (\d+):', l.strip())]
# build unique clause index (first occurrence)
seen = set(); tc_idx = []
for ln, t in tc_head:
    num = int(t.split(':')[0].split()[1])
    if num in seen: continue
    seen.add(num); tc_idx.append((num, ln, t))
tc_idx.sort()
tc_entries = []
for k, (num, st, t) in enumerate(tc_idx):
    nxt = tc_idx[k + 1][1] if k + 1 < len(tc_idx) else 494
    body = []
    for j in range(st, min(nxt - 1, len(tc))):
        x = tc[j].strip()
        if not x or '©' in x or re.match(r'^QSPP Terms and Conditions', x) or re.match(r'^p \d+ of \d+$', x): continue
        body.append(clean(x))
    txt = ' '.join(body)
    tc_entries.append({
        'id': 'TC-%d' % num, 'refKind': 'TC', 'ref': 'T&C %d' % num,
        'title': t.rstrip(','),
        'sourceDoc': 'QSPP-Terms-Conditions-V1.0-Aug2026.txt (PDF: QSPP-Terms-and-Conditions-V1.0-August-2026.pdf)',
        'sourceVersion': 'V1.0 August 2026',
        'anchor': anchor('extracted/QSPP-Terms-Conditions-V1.0-Aug2026.txt', st, max(st, nxt - 1)),
        'wording': trim400(txt),
        'summary': (' '.join(txt.split('.')[0:1])[:152] + ('...' if len(txt) > 155 else ''))[:160],
        'domain': 'program', 'applicabilityHint': 'always', 'evidenceExamples': [],
        'interpretationFlags': [],
    })
TC_FLAGS = {5: ['attestation-completed-annually-Cl-5c'], 9: ['notification-table-see-OBL-notification'],
            12: ['logo-licence-see-OBL-logo'], 13: ['fees-unpaid-block-services']}
for e in tc_entries:
    num = int(e['id'].split('-')[1])
    if num in TC_FLAGS: e['interpretationFlags'] += TC_FLAGS[num]
TC_SUM = {1: 'T&C apply to participation in QSPP; agreement order Registration Form → T&C → Rules → Policies → Attestation → AS85000.',
          3: 'QSPP engages the pharmacy; accreditation follows assessment against AS85000:2024 and the User Assessment Guide.',
          4: 'Scope: accredited services only may claim QSPP accreditation; advertising unaccredited services may draw a sanction (Rule 42).',
          5: 'Accreditation conferred/renewed subject to: assessment compliance, fees paid by due date, ANNUAL Attestation Statement, lawful compliance.',
          6: 'Accreditation valid until 11:59pm AEST on the Accreditation Expiry Date unless revoked earlier.',
          7: 'QSPP may assess at any time (onsite/remote/interview/document/video); additional assessments incl Mid-cycle; notice normally given.',
          8: 'Pharmacy must comply with AS85000:2024 throughout the accreditation period; failures draw sanctions.',
          9: 'Notification table: PBS number, ownership/control, shareholders/directors, trustees, insolvency, relocation, contacts — 1 calendar month; NEW SERVICES BEFORE commencement; material events at once.',
          10: 'Ownership disposal: written notice before completion (where practicable) to support@qspp.com; new owner must accept Program Materials.',
          12: 'Limited logo licence while accredited and in scope; breach terminates rights; revoked/expired ⇒ cease use + remove materials.',
          13: 'Pharmacy bears all QSPP fees (applications, assessments, corrective actions, sanctions); unpaid fees ⇒ sanctions/no services.',
          14: 'Information handled per QSPP privacy policy; de-identified data may be used statistically; individual consent required for PI supplied.',
          16: 'QSPP confidential information must be protected; notify suspected breaches immediately.'}
for e in tc_entries:
    num = int(e['id'].split('-')[1])
    if num in TC_SUM: e['summary'] = TC_SUM[num][:160]

# ---------------------------------------------------------------- Policies
policy_files = [
    ('QSPP-Policy-Accreditation-Revocation.txt', 'Accreditation & Revocation', 'QSPP-Accreditation-Revocation-Policy.pdf'),
    ('QSPP-Policy-Assessments.txt', 'Assessments', 'QSPP-Assessments-Policy.pdf'),
    ('QSPP-Policy-Exceptional-Circumstances.txt', 'Exceptional Circumstances', 'QSPP-Exceptional-Circumstances-Policy.pdf'),
    ('QSPP-Policy-Failure-to-Comply.txt', 'Failure to Comply', 'QSPP-Failure-to-Comply-Policy.pdf'),
    ('QSPP-Policy-Payment.txt', 'Payment', 'QSPP-Payment-Policy.pdf'),
    ('QSPP-Policy-Privacy.txt', 'Privacy', 'QSPP-Privacy-Policy.pdf'),
    ('QSPP-Policy-Procedural-Fairness-Impartiality.txt', 'Procedural Fairness & Impartiality', 'QSPP-Procedural-Fairness-and-Impartiality-Policy.pdf'),
    ('QSPP-Policy-Sanctions-Management.txt', 'Sanctions Management', 'QSPP-Sanctions-Management-Policy.pdf'),
]
policy_entries = []
for fname, title, pdf in policy_files:
    pl = lines_of('extracted/' + fname)
    version_line = ''
    for x in pl:
        t = x.strip()
        if re.match(r'^(Approved|Reviewed|Owner|Next review date|Version)', t):
            version_line += clean(t) + '; '
        if 'Version' in t and 'policy' in t.lower(): version_line += clean(t) + '; '
    body = ' '.join(clean(x) for x in pl[1:14] if x.strip())
    m = re.search(r'Purpose\s*(.*?)(?:Application|$)', body)
    purpose = clean(m.group(1)) if m else body
    policy_entries.append({
        'id': 'POLICY-' + title.lower().replace(' & ', '-and-').replace(' ', '-'),
        'refKind': 'POLICY', 'ref': 'Policy: %s' % title,
        'title': 'QSPP %s Policy' % title,
        'sourceDoc': fname + ' (PDF: ' + pdf + ')',
        'sourceVersion': (version_line.rstrip('; ') or 'version line not parsed'),
        'anchor': anchor('extracted/' + fname, 1, len(pl)),
        'wording': trim400(purpose),
        'summary': (' '.join(purpose.split('.')[0:1])[:150] + ('...' if len(purpose) > 153 else ''))[:160],
        'domain': 'program', 'applicabilityHint': 'always', 'evidenceExamples': [],
        'interpretationFlags': ['policy-imported', 'verify-todo'],
        'verifyState': 'todo',
    })
AP = policy_entries[1]
AP['interpretationFlags'].append('cl-2.1-late-cancellation-10-CALENDAR-days-see-ISS-CANCEL-WINDOW')
AP['interpretationFlags'].append('conservative-planning-10-business-days')

# ---------------------------------------------------------------- Obligations
obligations = [
 {'id': 'OBL-annual-attestation', 'refKind': 'OBLIGATION',
  'ref': 'T&C Cl 5c + Program Materials definition "Attestation Statement"',
  'title': 'Annual Attestation Statement (formal annual compliance confirmation)',
  'sourceDoc': 'QSPP-Terms-Conditions-V1.0-Aug2026.txt + QSPP-Program-Rules-V1.0-Aug2026.txt',
  'sourceVersion': 'V1.0 August 2026',
  'anchor': anchor('extracted/QSPP-Terms-Conditions-V1.0-Aug2026.txt', 43, 50),
  'wording': 'Clause 5: ... c) the Attestation Statement being completed annually ... [Rules definitions] "A formal document the Pharmacy submits annually to QSPP to confirm compliance with AS85000:2024 Australian Community Pharmacy Standard."',
  'summary': 'Complete and submit the annual Attestation Statement confirming compliance with AS85000:2024 — required for accreditation (T&C Cl 5c).',
  'domain': 'program', 'applicabilityHint': 'always',
  'evidenceExamples': [{'text': 'Annual Attestation Statement confirming compliance and commitment with the Quality & Safety Pharmacy Program. (UAG 1.1 evidence, ~line 864)',
                        'mandatory': True}],
  'interpretationFlags': ['annual-cadence-mandatory', 'also-listed-UAG-Fact-Sheet-7-per-KB', 'add-as-hitlist-item-per-KB-§9'],
  'secondaryAnchor': anchor('extracted/QSPP-Program-Rules-V1.0-Aug2026.txt', 449, 453)},
 {'id': 'OBL-notification-change-events', 'refKind': 'OBLIGATION',
  'ref': 'T&C Cl 9 (notification table rows)',
  'title': 'Notify QSPP of change events within stated timeframes',
  'sourceDoc': 'QSPP-Terms-Conditions-V1.0-Aug2026.txt',
  'sourceVersion': 'V1.0 August 2026',
  'anchor': anchor('extracted/QSPP-Terms-Conditions-V1.0-Aug2026.txt', 82, 142),
  'wording': 'You must give us written notice of the occurrence of any of the following events or circumstances along with relevant details within the following time frames: [Event / Timeframe table]',
  'summary': 'PBS number, ownership/control, shareholders/directors, trustees, insolvency, relocation, contacts: within 1 calendar month; NEW services: BEFORE commencement; material events: at the time they occur.',
  'domain': 'program', 'applicabilityHint': 'always',
  'evidenceExamples': [
      {'text': 'PBS approval number change — within one calendar month after the change occurs (~line 89-92)', 'mandatory': True},
      {'text': 'New service(s) — notify PRIOR to commencement of the new service(s) being offered (~lines 130-134)', 'mandatory': True},
      {'text': 'Any change of contact details — within one calendar month (~lines 126-128)', 'mandatory': True}],
  'interpretationFlags': ['portal-owner-sync-2-business-days-before-onsite-row', 'rows-quoted-in-anchor-range']},
 {'id': 'OBL-fees-invoices-28day', 'refKind': 'OBLIGATION',
  'ref': 'Rule 36 + Rule 37 + Payment Policy',
  'title': 'Fees and invoices: pay on strict 28-day terms',
  'sourceDoc': 'QSPP-Program-Rules-V1.0-Aug2026.txt + QSPP-Policy-Payment.txt',
  'sourceVersion': 'V1.0 August 2026 (rules); Payment Policy approved 13 May 2026, reviewed 31 Aug 2026',
  'anchor': anchor('extracted/QSPP-Program-Rules-V1.0-Aug2026.txt', 277, 327),
  'wording': 'Rule 37: "QSPP invoices are issued on strict 28-day terms." Rule 36 table: Administration, Assessment Cancellation Fee [Rule 10, 10 business days], Annual Invoice, Additional Assessment Expenses, Expansion of Services Invoice, Verification of Corrective Action.',
  'summary': 'All QSPP invoices run on strict 28-day terms; unpaid invoices can withdraw/deny accreditation; no payment plans, no pro-rata refunds (Payment Policy).',
  'domain': 'program', 'applicabilityHint': 'always',
  'evidenceExamples': [{'text': 'Payment Policy: EFT / BPAY / cheque / card (Amex 1.95% surcharge; portal excludes Amex); overdue ⇒ withdraw/suspend/deny accreditation (~lines 11-22)', 'mandatory': True}],
  'interpretationFlags': ['eos-invoice-terminated-if-overdue-Rule-27', 'no-payment-plans', 'no-pro-rata-refunds']},
 {'id': 'OBL-logo-use', 'refKind': 'OBLIGATION',
  'ref': 'T&C Cl 12 (Use of QSPP Logo)',
  'title': 'QSPP logo: limited licence; cease on revocation/expiry',
  'sourceDoc': 'QSPP-Terms-Conditions-V1.0-Aug2026.txt',
  'sourceVersion': 'V1.0 August 2026',
  'anchor': anchor('extracted/QSPP-Terms-Conditions-V1.0-Aug2026.txt', 158, 184),
  'wording': 'Upon successful accreditation ... we grant to you a limited, royalty-free, non-transferrable, non-sub-licensable, non-exclusive right to reproduce and/or display the QSPP logo ... solely for purposes associated with the Pharmacy.',
  'summary': 'Logo use only while accredited and only within accredited scope; comply with Visual Identity Guidelines; breach terminates rights; revoked/suspended/expired ⇒ immediate cease + removal.',
  'domain': 'program', 'applicabilityHint': 'always',
  'evidenceExamples': [{'text': 'QSPP may change/discontinue the Brand and can withdraw the licence at any time "for any reason or no reason" (~lines 177-182)', 'mandatory': False}],
  'interpretationFlags': ['refresher-certificates-may-carry-QCPP-logo-legitimately-per-KB-§5-report-QSPP-logo-certs']},
 {'id': 'OBL-banner-group-brand', 'refKind': 'OBLIGATION',
  'ref': 'Rule 49',
  'title': 'Banner groups: QSPP Brand only when 100% of members accredited',
  'sourceDoc': 'QSPP-Program-Rules-V1.0-Aug2026.txt',
  'sourceVersion': 'V1.0 August 2026',
  'anchor': anchor('extracted/QSPP-Program-Rules-V1.0-Aug2026.txt', 409, 413),
  'wording': 'Marketing and/or banner groups may only use the QSPP Brand on group branded collateral, both printed and electronic and including webpages, where 100 percent of the pharmacies within the banner group are QSPP accredited. Pharmacies joining the marketing group have a period of six months to become QSPP accredited without affecting the use of the QSPP Brand by the banner group as a whole.',
  'summary': 'Group branded collateral may carry the QSPP Brand only while 100% of member pharmacies are accredited; new members get a 6-month grace period.',
  'domain': 'program', 'applicabilityHint': 'if_indirect',
  'evidenceExamples': [], 'interpretationFlags': ['six-month-grace-for-joiners']},
]

# ---------------------------------------------------------------- HITLIST
hl_lines = lines_of('readiness-hitlist.md')
hits = []
for i, ln in enumerate(hl_lines, 1):
    if re.match(r'^\|\s*\d+\s*\|', ln):
        cells = [c.strip().replace('**', '') for c in [x for x in ln.strip().strip('|').split('|')]]
        if len(cells) >= 6: hits.append((i, cells))
HL_DOMAIN = {14: 'program'}
HL_HINT_DEFAULT = 'if_compounding'
hit_entries = []
for (row_no, cells) in hits:
    n = int(cells[0])
    priority = cells[1]
    action = cells[2]
    clause = cells[3]
    evidence = cells[4]
    dom = HL_DOMAIN.get(n, '2' if clause.startswith('2.2.3') else ('1' if clause.startswith('1.1') else 'program'))
    hint = HL_HINT_DEFAULT if (n <= 13) else 'always'
    hit_entries.append({
        'id': 'HITLIST-%d' % n, 'refKind': 'HITLIST', 'ref': 'Hit-%d' % n,
        'title': 'Hit-List #%d %s' % (n, priority.split('—')[0].strip()),
        'sourceDoc': 'readiness-hitlist.md', 'sourceVersion': 'crosswalk analysis, 26 Sep 2026',
        'anchor': anchor('readiness-hitlist.md', row_no, row_no, 'markdown table row'),
        'wording': trim400(action), 'summary': trim400(action)[:160],
        'domain': dom, 'applicabilityHint': hint,
        'evidenceExamples': [{'text': evidence, 'mandatory': False}] if evidence else [],
        'interpretationFlags': ['planning-item-imported', 'not-a-confirmed-deficiency'],
        'priority': priority, 'action': action, 'as85000Clause': clause,
        'evidenceToHold': evidence,
        'status': 'imported_unverified', 'ownersBlank': True,
        'note': 'planning item imported; not a confirmed deficiency',
    })

requirements = std + uag_entries + kt_entries + rule_entries + tc_entries + policy_entries + obligations + hit_entries

for o in obligations:
    o['summary'] = cap160(o['summary'])
    o['wording'] = trim400(o['wording'])

req_path = OUT + '/seed/requirements.json'
json.dump({'retrievedDate': '2026-10-19', 'packVersion': '26 Sep 2026', 'requirements': requirements},
          open(req_path, 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
cnt = {}
for r in requirements: cnt[r['refKind']] = cnt.get(r['refKind'], 0) + 1
print('requirements.json written:', req_path)
print('counts:', json.dumps(cnt))
print('STD:', len(std), 'UAG:', len(uag_entries), 'KT:', len(kt_entries), 'RULES:', len(rule_entries), 'TC:', len(tc_entries), 'POLICY:', len(policy_entries), 'OBL:', len(obligations), 'HIT:', len(hit_entries))