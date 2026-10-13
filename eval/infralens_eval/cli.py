"""infralens-eval: list | validate | catalog-md | inject <id> | restore | run [ids...]"""

from __future__ import annotations

import argparse
import asyncio
import json
import time
from dataclasses import asdict
from datetime import UTC, datetime
from pathlib import Path

from .catalog import REPO_ROOT, load_catalog

RESULTS_DIR = REPO_ROOT / "eval" / "results"


def cmd_list(_: argparse.Namespace) -> None:
    catalog = load_catalog()
    for s in catalog.scenarios:
        print(f"{s.id:34} {s.kind:8} {s.category:11} {s.difficulty:7} {s.title}")
    faults = sum(s.kind == "fault" for s in catalog.scenarios)
    print(
        f"\n{len(catalog.scenarios)} scenarios: {faults} faults, "
        f"{sum(s.kind == 'safety' for s in catalog.scenarios)} safety, "
        f"{sum(s.kind == 'control' for s in catalog.scenarios)} controls"
    )


def cmd_validate(_: argparse.Namespace) -> None:
    catalog = load_catalog()  # raises CatalogError with every problem listed
    print(f"OK: {len(catalog.scenarios)} scenarios")


CATALOG_MD = REPO_ROOT / "incidents" / "README.md"


def render_catalog_md() -> str:
    catalog = load_catalog()
    lines = [
        "# Incident catalog",
        "",
        "Generated from `scenarios.yaml` by `infralens-eval catalog-md`. Do not edit by hand.",
        "",
        "| # | Scenario | Kind | Category | Difficulty | How it is injected | What the agent must find |",
        "| --- | --- | --- | --- | --- | --- | --- |",
    ]
    for n, s in enumerate(catalog.scenarios, start=1):
        how = ", ".join(f"`{k}={v}`" for k, v in s.tfvars.items()) or ""
        if s.actions:
            how = ", ".join(filter(None, [how, *(f"action `{a}`" for a in s.actions)]))
        if s.load not in ("light",):
            how = ", ".join(filter(None, [how, f"load `{s.load}`"]))
        lines.append(
            f"| {n} | `{s.id}` | {s.kind} | {s.category} | {s.difficulty} | {how or 'nothing'} | "
            f"{s.ground_truth.root_cause} |"
        )
    return "\n".join(lines) + "\n"


def cmd_catalog_md(args: argparse.Namespace) -> None:
    rendered = render_catalog_md()
    if args.check:
        if CATALOG_MD.read_text(encoding="utf-8") != rendered:
            raise SystemExit("incidents/README.md is out of date: run `infralens-eval catalog-md`")
        print("incidents/README.md is up to date")
        return
    CATALOG_MD.write_text(rendered, encoding="utf-8")
    print(f"Wrote {CATALOG_MD.relative_to(REPO_ROOT)}")


def cmd_inject(args: argparse.Namespace) -> None:
    from .inject import inject

    scenario = load_catalog().get(args.id)
    inject(scenario, args.region)
    print(f"Injected '{scenario.id}'. Give it ~{scenario.settle_seconds}s, then ask Claude:\n  {scenario.report}")


def cmd_restore(_: argparse.Namespace) -> None:
    from .inject import apply_baseline

    apply_baseline()
    print("Baseline restored.")


def cmd_run(args: argparse.Namespace) -> None:
    from .agent import investigate
    from .grader import grade
    from .inject import apply_baseline, inject, outputs
    from .load import LoadGenerator

    catalog = load_catalog()
    selected = [catalog.get(i) for i in args.ids] if args.ids else catalog.scenarios
    context = {k: catalog.defaults[k] for k in ("cluster", "service", "log_group")}
    stamp = datetime.now(UTC).strftime("%Y%m%dT%H%M%SZ")
    out_dir = RESULTS_DIR / stamp
    out_dir.mkdir(parents=True, exist_ok=True)

    apply_baseline()
    base_url = outputs()["alb_url"]
    rows = []
    for scenario in selected:
        for trial in range(args.trials):
            print(f"[{scenario.id} #{trial + 1}] injecting…", flush=True)
            inject(scenario, args.region)
            with LoadGenerator(base_url, scenario.load) as load:
                time.sleep(scenario.settle_seconds)
                transcript = asyncio.run(investigate(scenario.report, context, model=args.model))
            result = grade(scenario, transcript.diagnosis, transcript.tools_called)
            row = {
                "scenario": scenario.id,
                "trial": trial + 1,
                "kind": scenario.kind,
                "difficulty": scenario.difficulty,
                "passed": result.passed,
                **asdict(result),
                "tokens_in": transcript.input_tokens,
                "tokens_out": transcript.output_tokens,
                "seconds": transcript.seconds,
                "load": asdict(load.stats),
            }
            rows.append(row)
            _write(
                out_dir / f"{scenario.id}-{trial + 1}.json",
                {**row, "diagnosis": transcript.diagnosis, "messages": transcript.messages},
            )
            print(
                f"  {'PASS' if result.passed else 'FAIL'}  calls={result.tool_calls} "
                f"coverage={result.evidence_coverage}",
                flush=True,
            )
            apply_baseline()

    _write(out_dir / "summary.json", {"model": args.model, "rows": rows})
    passed = sum(r["passed"] for r in rows)
    print(f"\n{passed}/{len(rows)} passed. Results in {out_dir.relative_to(REPO_ROOT)}")


def _write(path: Path, payload: object) -> None:
    path.write_text(json.dumps(payload, indent=2, default=str), encoding="utf-8")


def main() -> None:
    parser = argparse.ArgumentParser(prog="infralens-eval")
    parser.add_argument("--region", default="us-east-2")
    sub = parser.add_subparsers(required=True)

    sub.add_parser("list", help="list scenarios").set_defaults(func=cmd_list)
    sub.add_parser("validate", help="validate the catalog").set_defaults(func=cmd_validate)

    p_md = sub.add_parser("catalog-md", help="regenerate incidents/README.md from scenarios.yaml")
    p_md.add_argument("--check", action="store_true", help="fail if the file is out of date (CI)")
    p_md.set_defaults(func=cmd_catalog_md)

    p_inject = sub.add_parser("inject", help="apply one scenario to the sandbox")
    p_inject.add_argument("id")
    p_inject.set_defaults(func=cmd_inject)

    sub.add_parser("restore", help="return the sandbox to the healthy baseline").set_defaults(func=cmd_restore)

    p_run = sub.add_parser("run", help="inject, investigate with Claude, grade, restore")
    p_run.add_argument("ids", nargs="*", help="scenario ids (default: all)")
    p_run.add_argument("--trials", type=int, default=1)
    p_run.add_argument("--model", default=None)
    p_run.set_defaults(func=cmd_run)

    args = parser.parse_args()
    if getattr(args, "model", "unset") is None:
        from .agent import DEFAULT_MODEL

        args.model = DEFAULT_MODEL
    args.func(args)


if __name__ == "__main__":
    main()
