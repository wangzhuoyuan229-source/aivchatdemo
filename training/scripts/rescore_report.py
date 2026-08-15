#!/usr/bin/env python3
"""Reapply the current deterministic rules to an existing generation report."""

from __future__ import annotations

import argparse
import json
from pathlib import Path

from evaluate_model import score_response, summarize


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--report", type=Path, required=True)
    parser.add_argument("--cases", type=Path, required=True)
    args = parser.parse_args()

    report = json.loads(args.report.read_text(encoding="utf-8"))
    cases = {
        case["id"]: case
        for case in json.loads(args.cases.read_text(encoding="utf-8"))
    }
    for row in report["results"]:
        row["score"] = score_response(cases[row["id"]], row["response"])
    report["summary"] = summarize(report["results"])
    args.report.write_text(
        json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8"
    )
    print(json.dumps(report["summary"], ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
