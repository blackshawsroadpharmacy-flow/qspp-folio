#!/usr/bin/env python3
"""Stage-2 accuracy harness for the QSPP Folio 'Process and Sort' pipeline.

Runs the PINNED local checkpoint (laya-typed-decisions via laya-apple serve on
loopback:8642) over labeled pharmacy-document cases adapted from the app's test
fixtures, with the same candidate-descriptions the UI shows. Measures:
  top-choice accuracy (picked the labeled destination / sensible 'multiple'),
  abstention accuracy (unrelated doc -> 'abstain'),
  median latency, and memory of the server process.

The criteria use REF LABELS (never opaque uuids) mirroring the app's wire path;
the C# client sends candidate ids as labels, which the model reads as opaque
strings -- a real risk this harness deliberately exposes.

Usage: python3 bench_process_sort.py            # against 127.0.0.1:8642
"""
import json
import statistics
import subprocess
import sys
import time
import urllib.request

BASE = "http://127.0.0.1:8642"
MODEL = "laya-typed-decisions"

CAND_DESCRIPTIONS = {
    "Rule 10": "Rule 10 — QA Assessment: cancellation or reschedule within ten (10) business days of the Assessment Date triggers a fee; record bookings and cancellations.",
    "Rule 8": "Rule 8 — Assessment bookings: mid-cycle review is a remote assessment scheduled in year 2 (month 15 to month 21 of the 36-month cycle).",
    "Rule 12": "Rule 12 — Accreditation assessments: the Pharmacy must provide information or assistance and access to premises.",
    "1.1.2": "Standard 1.1.2 — Clinical governance: safety and quality systems integrated with governance processes.",
    "2.1.1": "Standard 2.1.1 — Pharmacy services: programs and services provided effectively and in compliance.",
    "REM-05": "Surveillance REM-05 — Vaccine cold-chain (Strive for 5): upload the pharmacy's Strive for 5 self-audit done in the last 12 months.",
    "multiple": "several of these destinations genuinely fit",
    "abstain": "no listed destination fits well from this excerpt",
}

CASES = [
    # (name, excerpt, candidates, expected one of candidate refs)
    ("bookings-sop", "Assessment bookings and cancellations. Our pharmacy records bookings "
     "and cancellations within the required windows. Cancellation windows apply to booked "
     "assessments, and the booking register lists every cancellation with its notice period.",
     ["Rule 10", "Rule 8", "multiple", "abstain"], "Rule 10"),
    ("midcycle-review-memo", "Mid-cycle review memo: our remote assessment in year 2 is scheduled "
     "for month 18 of the cycle, inside the 15 to 21 month window.",
     ["Rule 8", "Rule 10", "multiple", "abstain"], "Rule 8"),
    ("cold-chain-audit", "Strive for 5 self-audit completed 3 March. Every vaccine fridge is "
     "mapped and logged twice daily; the last 12 months of temperature logs are attached.",
     ["REM-05", "multiple", "abstain"], "REM-05"),
    ("governance-policy", "Clinical governance and quality systems: the pharmacy integrates "
     "safety and quality systems with its governance processes and reviews them actively.",
     ["1.1.2", "2.1.1", "multiple", "abstain"], "1.1.2"),
    ("services-review", "Annual review of pharmacy programs and services to keep them effective, "
     "efficient and compliant with their governing conditions.",
     ["2.1.1", "1.1.2", "multiple", "abstain"], "2.1.1"),
    ("access-letter", "Letter confirming the assessor will be given access to premises and any "
     "assistance needed during the accreditation assessment.",
     ["Rule 12", "Rule 10", "multiple", "abstain"], "Rule 12"),
    ("unrelated-weather", "Quarterly weather summary for the garden club; rainfall was near "
     "average and the spring fair was rained out.", ["Rule 10", "2.1.1", "abstain"], "abstain"),
    ("unrelated-menu", "Canteen menu rotation and catering quotes for the practice meeting.",
     ["Rule 12", "1.1.2", "abstain"], "abstain"),
    ("ambiguous-tie", "Assessment cancellation recorded on the booking register with its notice "
     "period; the mid-cycle review window was also checked.",
     ["Rule 10", "Rule 8", "multiple", "abstain"], "multiple"),
]

INSTRUCTIONS = ("You file scanned pharmacy documents into a QSPP evidence register. Given the "
                "document excerpt and the candidate list in order, choose the single best "
                "destination label. Choose 'multiple' when several destinations genuinely fit and "
                "the reviewer should pick them; choose 'abstain' when no listed destination fits.")


def ask(state, candidates):
    criteria = {c: CAND_DESCRIPTIONS[c] for c in candidates}
    body = {
        "model": MODEL,
        "state": state,
        "questions": {
            "relevant": {
                "type": "noul",
                "instructions": "Decide whether the document excerpt genuinely belongs with any of the listed QSPP evidence destinations.",
                "criteria": {
                    "true": "the excerpt does NOT match any listed destination; file nothing",
                    "false": "the excerpt DOES match at least one listed destination",
                },
            },
            "destination": {"type": "choice", "instructions": INSTRUCTIONS, "criteria": criteria},
        },
    }
    payload = json.dumps(body).encode()
    req = urllib.request.Request(BASE + "/v1/systemone", data=payload, headers={"Content-Type": "application/json"})
    t0 = time.perf_counter()
    with urllib.request.urlopen(req, timeout=30) as resp:
        d = json.load(resp)
    ms = (time.perf_counter() - t0) * 1000
    a = d["answers"]["destination"]
    noul = d["answers"]["relevant"]
    la = d.get("laya_apple", {})
    rel_choice = noul.get("noul")
    return a["choice"], a.get("answer_confidence", 0.0), rel_choice, ms, la


def server_rss_mb():
    try:
        out = subprocess.check_output(["ps", "aux"]).decode()
        rss = 0
        for line in out.splitlines():
            if "laya-apple serve" in line and "grep" not in line:
                parts = line.split()
                rss += int(parts[5])  # KB
        return rss / 1024 if rss else None
    except Exception:
        return None


def main():
    correct = 0
    n_dest = 0
    dest_correct = 0
    abstain_correct = 0
    abstain_total = 0
    latencies = []
    failures = []
    for name, excerpt, candidates, expected in CASES:
        state = {"document": {"title": f"{name}.pdf", "excerpt": excerpt},
                 "task": "pick the QSPP evidence destination this document belongs in"}
        choice, conf, relevant, ms, la = ask(state, candidates)
        latencies.append(ms)
        if expected == "abstain":
            abstain_total += 1
            # noul true = nothing matches; a low-choice-conf fallback also counts as honest abstain
            ok = relevant is True or (relevant is False and choice == "abstain")
            abstain_correct += int(ok)
            print(f"{'PASS' if ok else 'FAIL'}  {name:22s} expected=abstain   got={choice:9s} noul={relevant} conf={conf:0.2f} {ms:6.0f} ms")
            if not ok:
                failures.append(name)
        else:
            n_dest += 1
            ok = choice == expected
            dest_correct += int(ok)
            print(f"{'PASS' if ok else 'FAIL'}  {name:22s} expected={expected:9s} got={choice:9s} noul={relevant} conf={conf:0.2f} {ms:6.0f} ms")
            if not ok:
                failures.append(name)
        correct += int(ok)

    n = len(CASES)
    rss = server_rss_mb()
    print("\n== summary ==")
    print(f"overall accuracy: {correct}/{n} = {correct / n:.0%}")
    if n_dest:
        print(f"destination choice accuracy: {dest_correct}/{n_dest} = {dest_correct / n_dest:.0%}")
    if abstain_total:
        print(f"abstention accuracy (noul gate): {abstain_correct}/{abstain_total} = {abstain_correct / abstain_total:.0%}")
    print(f"median latency: {statistics.median(latencies):.0f} ms; p95 {sorted(latencies)[min(len(latencies) - 1, int(0.95 * len(latencies)))]:.0f} ms")
    print(f"server RSS: {rss:0.0f} MiB" if rss else "server RSS: n/a")
    if failures:
        print("failures:", ", ".join(failures))


if __name__ == "__main__":
    sys.exit(main())