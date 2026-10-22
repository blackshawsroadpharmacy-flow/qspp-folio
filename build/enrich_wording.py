# Enrich seed/requirements.json wording: replace the 400-char M1 excerpt with the FULL
# anchored segment from freshly regenerated source texts (poppler pdftotext).
#
# Gate per item (both strict): the shipped 400-char wording must be contained (after
# whitespace normalisation) in the new text. If the regenerated extraction's line numbers
# drift from the original build, relocate by CONTENT instead: normalised substring search
# in the whole file, re-spanned with the old anchor's span size; the relocated segment
# must still contain the old wording. Items that pass neither gate keep their wording.
# Usage: python3 enrich_wording.py <repo> <extract_dir> <out_json> [cap]
import json, os, re, sys

repo, extract_dir, out_path = sys.argv[1], sys.argv[2], sys.argv[3]
CAP = int(sys.argv[4]) if len(sys.argv) > 4 else 2800

def clean(s):
    return ' '.join(s.split())

def norm(s):
    return re.sub(r'\s+', ' ', s.lower())

_files = {}
def lines_of(file):
    if file not in _files:
        p = os.path.join(extract_dir, file)
        _files[file] = open(p, encoding='utf-8').read().split('\n')
    return _files[file]

def segment_lines(txt, st, end):
    seg = []
    for j in range(max(st - 1, 0), min(end, len(txt))):
        t = txt[j].strip()
        if not t or '©' in t:
            continue
        if re.match(r'^p \d+ of \d+$', t) or re.match(r'^\d{1,3}$', t):
            continue
        if re.match(r'^QSPP (Program Rules|Terms and Conditions)', t) or 'Australian-Community-Pharmacy-Standard' in t:
            continue   # repeated page-break header lines inside the body text
        seg.append(clean(t))
    return seg

def old_core(o):
    # STD wording carries a "ref title — " prefix the builder added
    core = o['wording']
    if core.endswith('...'):
        core = core[:-3]
    if o.get('refKind') == 'STD':
        pref = f"{o.get('ref')} {o.get('subclauseTitle', o.get('title', ''))} — "
        if core.startswith(pref):
            core = core[len(pref):]
    return core

seed = json.load(open(os.path.join(repo, 'seed/requirements.json'), encoding='utf-8'))
items = seed['requirements']

direct, relocated, failed, enriched = 0, {}, [], 0
out = []
for o in items:
    anchor = o.get('anchor')
    old_w = o['wording']
    core = old_core(o)
    ncore = norm(core)
    probe_core = ncore[:110]
    new = old_w
    note = ''
    if (not anchor or 'approxLines' not in anchor
            or not old_w.endswith('...')                  # never truncated — nothing to enrich
            or o.get('refKind') in ('OBLIGATION', 'HITLIST')):   # analysis-derived text, not source quotes
        out.append(dict(o))
        continue
    try:
        seg = segment_lines(lines_of(anchor['file']), *anchor['approxLines'])
        if probe_core in norm(' '.join(seg)):
            direct += 1
            note = 'direct'
        else:
            # content relocation inside the same file
            lines = [clean(l) for l in lines_of(anchor['file'])]
            n_lines = [norm(l) for l in lines]
            joined = norm(' '.join(lines))
            # map: cumulative normalized length of each cleaned line
            idxs, acc = [], 0
            for l in lines:
                nl = norm(l)
                idxs.append((acc, acc + len(nl)))
                acc += len(nl) + 1
            pos = joined.find(probe_core.strip('. '))
            if pos < 0:
                failed.append((o['id'], o['ref'], anchor['file'], anchor['approxLines']))
                out.append(dict(o))
                continue
            span = anchor['approxLines'][1] - anchor['approxLines'][0]
            # line index that contains pos
            li = next(k for k, (a, b) in enumerate(idxs) if a <= pos < b)
            seg = segment_lines(lines, li + 1, li + 1 + span)
            if probe_core in norm(' '.join(seg)):
                relocated[o['id']] = [li + 1, li + 1 + span]
                direct += 1
                note = 'relocated'
            else:
                # widen once: span ±40% each way
                seg = segment_lines(lines, max(1, li + 1 - span // 4), li + 1 + span + span // 4)
                if probe_core in norm(' '.join(seg)):
                    relocated[o['id']] = [max(1, li + 1 - span // 4), li + 1 + span + span // 4]
                    direct += 1
                    note = 'relocated+widen'
                else:
                    failed.append((o['id'], o['ref'], anchor['file'], anchor['approxLines']))
                    out.append(dict(o))
                    continue
    except FileNotFoundError:
        # source text unavailable (readiness-hitlist.md) — wording already complete short text
        out.append(dict(o))
        continue
    full = ' '.join(seg)
    if ncore not in norm(full):
        failed.append((o['id'], o['ref'], anchor['file'], note + ':GATE-FAIL'))
        out.append(dict(o))
        continue
    if len(full) > CAP:
        full = full[:CAP].rstrip() + ' ...'
    if full != old_w:
        enriched += 1
    o2 = dict(o)
    o2['wording'] = full
    if note == 'relocated' or note == 'relocated+widen':
        o2['anchor'] = dict(anchor)
        o2['anchor']['approxLines'] = relocated[o['id']]
        o2['anchor']['note'] = (anchor.get('note') or '') + ' | line range re-derived from the regenerated source text extraction'
    out.append(o2)

res = dict(seed)
res['requirements'] = out
json.dump(res, open(out_path, 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
print(f'items: {len(items)}  validated: {direct}  enriched: {enriched}  failed: {len(failed)}')
for m in failed:
    print('  UNCHANGED:', m)