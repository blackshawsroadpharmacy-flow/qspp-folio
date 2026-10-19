# Part 2: documents.json, issues.json, coverage ledger. Requires requirements.json from seed_build.py.
import re, json, hashlib, os, io

PACK = '/home/vpnhugh/Projects/qspp-pack'
OUT = '/home/vpnhugh/Projects/qspp-manager'

def loadtxt(rel):
    with io.open(os.path.join(PACK, rel), encoding='utf-8') as f:
        return f.read()

def lines_of(rel):
    return loadtxt(rel).split('\n')

def clean(s):
    return ' '.join(s.split())

manifest = json.load(open(os.path.join(PACK, 'website-scrape/downloads/manifest.json'), encoding='utf-8'))
man_by_name = {}
for m in manifest:
    man_by_name[os.path.basename(m['path'])] = m

def sha(rel):
    with open(os.path.join(PACK, rel), 'rb') as f:
        data = f.read()
    return hashlib.sha256(data).hexdigest(), len(data)

def doc(id, disp, fname, path, ver, role, manifest_key=None, extra=None):
    h, b = sha(path)
    e = {'id': id, 'displayName': disp, 'fileName': fname, 'sourcePath': path,
         'sourceVersion': ver, 'role': role, 'sha256': h, 'fileBytes': b,
         'sha256Source': 'computed (python hashlib)'}
    mk = man_by_name.get(manifest_key or fname)
    if mk and mk.get('sha256'):
        e['manifestSha256'] = mk['sha256']
        e['manifestVerified'] = (mk['sha256'] == h)
    if extra: e.update(extra)
    return e

docs = []
docs.append(doc('DOC-AS85000-2024', 'AS 85000:2024 Australian Community Pharmacy Standard (the Standard)',
                'AS85000-2024_Australian-Community-Pharmacy-Standard.pdf',
                'documents/AS85000-2024_Australian-Community-Pharmacy-Standard.pdf',
                '2024 (published June 2024; ISBN 978-1-76139-672-4)', 'standard'))
u_o = doc('DOC-UAG-V1.1-ORIGINAL', 'QSPP User Assessment Guide V1.1 (original pack copy, origin PDF)',
          'QSPP-User-Assessment-Guide_V1.1_Sep2026.pdf', 'documents/QSPP-User-Assessment-Guide_V1.1_Sep2026.pdf',
          'V1.1 — cover states "Date of publication: September 2026 (Version 1.1)" (extracted text line 43)', 'guide')
u_o['provenance'] = ('origin copy from QSPP onboarding (pack documents/). Distinct from hub mirror copy '
                     '(different sha256); see DOC-UAG-V1.1-HUB')
docs.append(u_o)
u_h = doc('DOC-UAG-V1.1-HUB', 'QSPP User Assessment Guide V1.1 (hub mirror, filename Guide_V1.12.pdf)',
          'Guide_V1.12.pdf', 'website-scrape/downloads/Guide_V1.12.pdf',
          'hub copy (filename Guide_V1.12.pdf), cover states Version 1.1 Sep 2026', 'guide')
u_h['provenance'] = ('hub mirror downloaded from qspp.com knowledge hub 19 Oct 2026; '
                     'sha256 recorded from website-scrape/downloads/manifest.json. '
                     'IMPORTANT: no official "build 2" label appears in the hub text — the pack README\u2019s '
                     '"build 2" phrasing is unverified; text differs slightly from the origin copy (~73 lines per KB §8).')
docs.append(u_h)
docs.append(doc('DOC-QC2020-V1.1', 'QC2020 Requirements V1.1 (outgoing QCPP accreditation baseline)',
                'QC2020-Requirements_V1.1_outgoing.pdf', 'documents/QC2020-Requirements_V1.1_outgoing.pdf',
                'V1.1 (outgoing QC2020; hub mirror QC2020-Program-Rules-1.3-October-22.pdf)', 'rules'))
docs.append(doc('DOC-RULES-V1.0', 'QSPP Program Rules V1.0 (August 2026)',
                'QSPP-Program-Rules-V1.0-August-2026.pdf', 'website-scrape/downloads/QSPP-Program-Rules-V1.0-August-2026.pdf',
                'V1.0 August 2026', 'rules'))
docs.append(doc('DOC-TC-V1.0', 'QSPP Terms and Conditions V1.0 (August 2026)',
                'QSPP-Terms-and-Conditions-V1.0-August-2026.pdf', 'website-scrape/downloads/QSPP-Terms-and-Conditions-V1.0-August-2026.pdf',
                'V1.0 August 2026', 'terms'))
pol_map = [('QSPP-Accreditation-Revocation-Policy.pdf', 'QSPP Accreditation & Revocation Policy', 'QSPP-Policy-Accreditation-Revocation.txt'),
           ('QSPP-Assessments-Policy.pdf', 'QSPP Assessments Policy', 'QSPP-Policy-Assessments.txt'),
           ('QSPP-Exceptional-Circumstances-Policy.pdf', 'QSPP Exceptional Circumstances Policy', 'QSPP-Policy-Exceptional-Circumstances.txt'),
           ('QSPP-Failure-to-Comply-Policy.pdf', 'QSPP Failure to Comply Policy', 'QSPP-Policy-Failure-to-Comply.txt'),
           ('QSPP-Payment-Policy.pdf', 'QSPP Payment Policy', 'QSPP-Policy-Payment.txt'),
           ('QSPP-Privacy-Policy.pdf', 'QSPP Privacy Policy', 'QSPP-Policy-Privacy.txt'),
           ('QSPP-Procedural-Fairness-and-Impartiality-Policy.pdf', 'QSPP Procedural Fairness & Impartiality Policy', 'QSPP-Policy-Procedural-Fairness-Impartiality.txt'),
           ('QSPP-Sanctions-Management-Policy.pdf', 'QSPP Sanctions Management Policy', 'QSPP-Policy-Sanctions-Management.txt')]
for pdf, disp, txt in pol_map:
    pl = lines_of('extracted/' + txt)
    vl = '; '.join(clean(x) for x in pl if re.match(r'^(Approved|Reviewed|Owner|Next review date)', x.strip()))
    docs.append(doc('DOC-POLICY-' + disp.split()[-2].lower() + '-' + disp.split()[-1].lower(), disp, pdf,
                    'website-scrape/downloads/' + pdf, vl or 'version line not parsed',
                    'policy', extra={'extractedText': 'extracted/' + txt}))
docs.append(doc('DOC-TRAINING-REQ', 'QSPP Training requirements (S2/S3 staff fact sheet)',
                'QSPP-Training-requirements.pdf', 'website-scrape/downloads/QSPP-Training-requirements.pdf',
                'current QSPP training fact sheet (hub resource, scraped 19 Oct 2026)', 'policy',
                extra={'extractedText': 'extracted/QSPP-Training-requirements.txt'}))
docs.append(doc('DOC-REFRESHER-LIST', 'QSPP Approved Refresher Training List',
                'QSPP-Approved-Refresher-Training-List-6-October-2026.pdf',
                'website-scrape/downloads/QSPP-Approved-Refresher-Training-List-6-October-2026.pdf',
                'version 6 October 2026 (11 pages; RT-coded approved courses)', 'reference',
                extra={'extractedText': 'extracted/QSPP-Approved-Refresher-Training-List.txt'}))
docs.append(doc('DOC-CODE-ETHICS', 'PSA Code of Ethics for Pharmacists (2026 refresh)',
                'Code-of-Ethics_Digital.pdf', 'website-scrape/downloads/Code-of-Ethics_Digital.pdf',
                '2026 (PSA code refresh; government-funded per KB §7)', 'guidance',
                extra={'extractedText': 'extracted/Code-of-Ethics-2026.txt'}))
docs.append(doc('DOC-OPEN-DISCLOSURE-FW', 'Australian Open Disclosure Framework (ACSQHC)',
                'Australian-Open-Disclosure-Framework_1.pdf', 'Australian-Open-Disclosure-Framework_1.pdf',
                'June 2026 revision (per KB §7)', 'guidance',
                extra={'extractedText': 'extracted/Australian-Open-Disclosure-Framework.txt',
                       'locationNote': 'pack root, not website-scrape/downloads/ (not in manifest.json)'}))
docs.append(doc('DOC-OPEN-DISCLOSURE-FACT', 'Open Disclosure fact sheet — when and how to do it',
                'open-disclosure-fact-sheet-when-and-how-do-it.pdf',
                'open-disclosure-fact-sheet-when-and-how-do-it.pdf',
                'current fact sheet accompanying the June 2026 framework', 'guidance',
                extra={'extractedText': 'extracted/Open-Disclosure-Fact-Sheet.txt',
                       'locationNote': 'pack root copy; identical file also in website-scrape/downloads/'}))
docs.append(doc('DOC-GUIDE-TOOL', 'G.U.I.D.E decision tool (PSA ethics decision tool)',
                'G.U.I.D.E-Framework.pdf', 'website-scrape/downloads/G.U.I.D.E-Framework.pdf',
                'v6 (per KB §7)', 'guidance', extra={'extractedText': 'extracted/GUIDE-Framework.txt'}))
docs.append(doc('DOC-COMMUNICATING', 'Communicating with Aboriginal and Torres Strait Islander people (Queensland Health)',
                'communicating.pdf', 'website-scrape/downloads/communicating.pdf',
                'current QLD Health guide (supports 1.1.3.2 / 1.1.4.4 evidence per KB §7)', 'guidance',
                extra={'extractedText': 'extracted/Communicating-with-Patients.txt'}))
# excluded record
cap = next(m for m in manifest if m['path'].endswith('capacity-toolkit.pdf'))
docs.append({'id': 'DOC-EXCLUDED-capacity-toolkit', 'displayName': 'Capacity Australia toolkit (EXCLUDED)',
             'fileName': 'capacity-toolkit.pdf', 'sourcePath': 'website-scrape/downloads/capacity-toolkit.pdf',
             'sourceVersion': 'n/a', 'role': 'reference', 'sha256': None, 'fileBytes': cap['size'],
             'excludedNotes': ('NOT imported: external resource hosted on capacityaustralia.org.au, the download '
                               'returned a 221-byte HTML captcha wall (manifest kind "html-wall"), no usable PDF text. '
                               'Treat as reference-only. Per brief and KB §8 item 7.'),
             'manifestKind': cap.get('kind')})

# verify the AS85000 copies are byte-identical (pack vs hub download)
h_doc, _ = sha('documents/AS85000-2024_Australian-Community-Pharmacy-Standard.pdf')
h_hub = man_by_name['as85000-2024.pdf']['sha256']
as_identical = (h_doc == h_hub)
assert as_identical, 'AS85000 copies unexpectedly differ'
docs[0]['crossCopyNote'] = 'hub download as85000-2024.pdf sha256 matches the pack copy byte-for-byte (manifest sha256 = %s)' % h_hub
assert docs[1]['sha256'] != docs[2]['sha256'], 'UAG copies unexpectedly identical'

doc_path = OUT + '/seed/documents.json'
json.dump({'retrievedDate': '2026-10-19', 'packVersion': '26 Sep 2026', 'documents': docs},
          open(doc_path, 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
print('documents.json written:', doc_path, 'entries:', len(docs), 'with sha256:', sum(1 for d in docs if d.get('sha256')))

# ------------------------------------------------------------- issues.json
AN = ("approximate line anchor: pdftotext extraction, lines counted with text.split('\\n') "
      "(1-indexed); treat as ±20 (clause-index convention)")
ru = lines_of('extracted/QSPP-Program-Rules-V1.0-Aug2026.txt')
tc = lines_of('extracted/QSPP-Terms-Conditions-V1.0-Aug2026.txt')
ap = lines_of('extracted/QSPP-Policy-Assessments.txt')
corpus = lines_of('extracted/hub-pages-corpus.txt')
uag = lines_of('extracted/UAG-V1.1-full.txt')
qc = lines_of('extracted/QC2020-full.txt')

def Q(L, a, b):
    return [clean(x) for x in L[a-1:b] if x.strip()]

issues = {
    'retrievedDate': '2026-10-19',
    'packVersion': '26 Sep 2026 (pack README "Prepared 26 Sep 2026")',
    'statusVocabulary': 'unresolved / unresolved-awaiting-authoritative-source / unresolved-confirm-with-manager',
    'issues': [
        {
            'id': 'ISS-CANCEL-WINDOW',
            'title': 'Late-assessment-cancellation window: 10 business days (Program Rules) vs 10 calendar days (Assessments Policy + Fees page)',
            'topic': 'assessment cancellation fee window',
            'status': 'unresolved-awaiting-authoritative-source',
            'conservativePlanning': '10 business days (do not book/cancel any closer than 10 business days from the assessment date; $1,595 late-cancellation fee position)',
            'sources': [
                {'label': 'Program Rules Rule 10 (authoritative rules doc)', 'file': 'extracted/QSPP-Program-Rules-V1.0-Aug2026.txt', 'approxLines': [70, 74], 'anchorsAreApprox': True, 'anchorNote': AN,
                 'quote': 'If an assessment is cancelled and/or rescheduled within ten (10) business days of the Assessment Date, or the virtual/remote component of the assessment is cancelled and/or rescheduled within two (2) business days of the scheduled time for the virtual/remote component, then the full cost of one (1) assessment plus any travel expenses incurred to that point in time, will be paid by and invoiced to the Pharmacy'},
                {'label': 'Program Rules Rule 36 fee table (repeats 10 business days)', 'file': 'extracted/QSPP-Program-Rules-V1.0-Aug2026.txt', 'approxLines': [289, 290], 'anchorsAreApprox': True, 'anchorNote': AN,
                 'quote': 'Assessment Cancellation Fee incurred if assessment is cancelled within 10 business days of assessment date or failure to complete virtual/remote component of assessment [Rule 10]'},
                {'label': 'Assessments Policy 2.1 (policy doc)', 'file': 'extracted/QSPP-Policy-Assessments.txt', 'approxLines': [29, 33], 'anchorsAreApprox': True, 'anchorNote': AN,
                 'quote': '2.1 Cancellation or changes less than 10 calendar days — Should an assessment date booking be changed or cancelled by a pharmacy owner or authorised delegate ... a cancellation fee equivalent to the current assessment fee (see schedule of fees), will be charged'},
                {'label': 'QSPP website Fees page (hub scrape)', 'file': 'extracted/hub-pages-corpus.txt', 'approxLines': [3435, 3446], 'anchorsAreApprox': True, 'anchorNote': AN,
                 'quote': 'Fee Schedule as at 1 October 2026 ... Replacement assessment (late cancellation) $1,595 ... If you cancel a scheduled on-site assessment within 10 calendar days of the scheduled date, you will automatically incur...'}],
            'conflict': 'Rule 10 (business days) and Rule 36 (business days) vs Assessments Policy 2.1 + Fees page (calendar days). Rules and policy disagree on the unit.',
            'resolutionHint': 'Ask QSPP (support@qspp.com / interpretations@qspp.com) which unit is authoritative; meanwhile plan to the stricter window (10 business days). Agreement order places Program Rules above policies, which supports the business-days reading.',
            'impacts': ['assessment booking guardrails', 'calendar countdown for $1,595 fee exposure'],
        },
        {
            'id': 'ISS-MIDCYCLE-WINDOW',
            'title': 'Mid-Cycle Review scheduling window: four different statements',
            'topic': 'mid-cycle review timing',
            'status': 'unresolved',
            'conservativePlanning': 'months 15-21 within year 2 (align with Rule 8 + T&C definition; hub approximative)',
            'sources': [
                {'label': 'Program Rules Rule 8 (operative rule)', 'file': 'extracted/QSPP-Program-Rules-V1.0-Aug2026.txt', 'approxLines': [53, 56], 'anchorsAreApprox': True, 'anchorNote': AN,
                 'quote': 'A Mid-Cycle Review will be undertaken as a remote assessment to check-in and assess ongoing compliance with the program. This assessment is to be scheduled within the second year of the accreditation cycle (month 15 - month 21 of the 36-month accreditation cycle).'},
                {'label': 'Program Rules definitions table (same document)', 'file': 'extracted/QSPP-Program-Rules-V1.0-Aug2026.txt', 'approxLines': [476, 479], 'anchorsAreApprox': True, 'anchorNote': AN,
                 'quote': 'Mid-Cycle Review — A virtual review conducted remotely between months 13 and 24 of the 36-month accreditation cycle to assess ongoing compliance.'},
                {'label': 'Terms & Conditions definitions', 'file': 'extracted/QSPP-Terms-Conditions-V1.0-Aug2026.txt', 'approxLines': [392, 395], 'anchorsAreApprox': True, 'anchorNote': AN,
                 'quote': 'Mid-cycle Review — A virtual review conducted between months 15 and 21 of the 36-month accreditation cycle to assess ongoing compliance.'},
                {'label': 'QSPP website (hub scrape)', 'file': 'extracted/hub-pages-corpus.txt', 'approxLines': [1943, 1944], 'anchorsAreApprox': True, 'anchorNote': AN,
                 'quote': 'One Onsite Accreditation Assessment every three years / One Mid-cycle Review approximately 18 months after the Onsite Assessment'}],
            'conflict': 'Rule 8 says months 15-21; the Rules\u2019 own definitions table says months 13-24; T&C definition says months 15-21; hub page says approximately 18 months after the onsite assessment.',
            'resolutionHint': 'Plan to months 15-21 (matches Rule 8 and the T&C definition both); the Rules definitions table (13-24) and the hub approximation (~18 months) are treated as descriptive, not operative.',
            'impacts': ['year-2 calendar planning', 'booking reminders'],
        },
        {
            'id': 'ISS-POTENCY-CADENCE',
            'title': 'Potency testing cadence wording — per person, drug + dosage-form coverage (confirm with pharmacist manager)',
            'topic': '2.2.3.8 final product potency testing cadence',
            'status': 'unresolved-confirm-with-manager',
            'conservativePlanning': 'Schedule potency tests so each compounding technician AND each compounding pharmacist completes tests on two different drugs and two different dosage forms per year (i.e. per-person drug+form coverage, NOT four combinations of each person; also NOT a single shared program).',
            'requiredShortform': 'two (2) different drugs × two (2) dosage forms per compounding technician AND per compounding pharmacist per year',
            'shortformNote': ('requiredShortform is the planning paraphrase; the UAG source wording below does NOT use '
                              '"(2)" numerals or the "×" sign — keep the verbatim quote in any compliance artefact.'),
            'sources': [
                {'label': 'UAG V1.1 section 2.4 Key Tasks (verbatim; source of the cadence)', 'file': 'extracted/UAG-V1.1-full.txt', 'approxLines': [3253, 3259], 'anchorsAreApprox': True, 'anchorNote': AN + '; clause-index.md approximates ~3140s, actual location is 3253-3259',
                 'quote': 'Establish a system for final product potency testing to ensure consistency, efficacy and quality. A final product potency test must be completed on two different drugs and two different dosage forms per compounding technician and compounding pharmacist per year.'},
                {'label': 'UAG 2.4 evidence examples (records burden)', 'file': 'extracted/UAG-V1.1-full.txt', 'approxLines': [3404, 3407], 'anchorsAreApprox': True, 'anchorNote': AN,
                 'quote': 'Evidence of the pharmacy\u2019s procedure for final product potency testing. / Records of the outcomes from final product potency tests completed.'},
                {'label': 'AS85000:2024 clause 2.2.3.8 (the underlying standard wording, no cadence)', 'file': 'extracted/AS85000-2024-full.txt', 'approxLines': [1466, 1469], 'anchorsAreApprox': True, 'anchorNote': AN + '; clause-index gives 2.2.3.8 @1466 (±20)',
                 'quote': '2.2.3.8 The pharmacy shall maintain and follow a system to ensure consistency of compounded products, including appropriate instructions for compounding and a system for final product potency testing.'},
                {'label': 'QC2020 5.3.7 (outgoing; per technician only — UAG adds the pharmacist cohort)', 'file': 'extracted/QC2020-full.txt', 'approxLines': [3780, 3788], 'anchorsAreApprox': True, 'anchorNote': AN,
                 'quote': 'The procedure for ensuring consistency of compounded products including a system for final product potency testing. Documentation of results from final product potency tests completed. A final product potency test must be completed on two different drugs and two different dosage forms per technician per year.'}],
            'conflict': 'Not a direct contradiction, but an interpretation: UAG wording "per compounding technician and compounding pharmacist per year" is ambiguous between (a) each person individually covers 2 drugs x 2 dosage forms, and (b) two test programs total (one per technician, one per pharmacist).QC2020 said per technician only.',
            'resolutionHint': 'Confirm with the pharmacist manager (also flagging readiness-hitlist item 13). Working interpretation: per-person drug+form coverage — each in-scope person does tests on two different drugs and two different dosage forms each year. NOT four combinations of each drug/form pair, NOT a shared single set.',
            'impacts': ['potency testing scheduler', 'evidence records per person', 'hitlist item 13'],
        }
    ]
}
iss_path = OUT + '/seed/issues.json'
json.dump(issues, open(iss_path, 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
print('issues.json written:', iss_path, 'issues:', len(issues['issues']))