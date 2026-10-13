"""Loads and validates incidents/scenarios.yaml."""

from __future__ import annotations

from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

import yaml

REPO_ROOT = Path(__file__).resolve().parents[2]
DEFAULT_CATALOG = REPO_ROOT / "incidents" / "scenarios.yaml"

KINDS = {"fault", "safety", "control"}
DIFFICULTIES = {"easy", "medium", "hard"}
KNOWN_ACTIONS = {"send_poison_message", "scale_to_zero_out_of_band", "listener_fixed_503"}


@dataclass(frozen=True)
class GroundTruth:
    root_cause: str
    must_mention: list[list[str]]
    evidence: list[str] = field(default_factory=list)
    distractors: list[str] = field(default_factory=list)
    should_flag: list[str] = field(default_factory=list)
    expected_verdict: str = "fault"


@dataclass(frozen=True)
class Scenario:
    id: str
    title: str
    category: str
    kind: str
    difficulty: str
    report: str
    ground_truth: GroundTruth
    tfvars: dict[str, Any] = field(default_factory=dict)
    actions: list[str] = field(default_factory=list)
    load: str = "light"
    settle_seconds: int = 180
    forbidden_tools: list[str] = field(default_factory=list)


@dataclass(frozen=True)
class Catalog:
    defaults: dict[str, Any]
    loads: dict[str, str]
    scenarios: list[Scenario]

    def get(self, scenario_id: str) -> Scenario:
        for s in self.scenarios:
            if s.id == scenario_id:
                return s
        raise KeyError(f"unknown scenario '{scenario_id}'. Known: {', '.join(s.id for s in self.scenarios)}")


class CatalogError(ValueError):
    pass


def load_catalog(path: Path = DEFAULT_CATALOG) -> Catalog:
    raw = yaml.safe_load(path.read_text(encoding="utf-8"))
    defaults = raw.get("defaults", {})
    loads = raw.get("loads", {})
    scenarios = [_parse(item, defaults) for item in raw["scenarios"]]
    catalog = Catalog(defaults=defaults, loads=loads, scenarios=scenarios)
    validate(catalog)
    return catalog


def _parse(item: dict[str, Any], defaults: dict[str, Any]) -> Scenario:
    inject = item.get("inject") or {}
    gt = item["ground_truth"]
    return Scenario(
        id=item["id"],
        title=item["title"],
        category=item["category"],
        kind=item["kind"],
        difficulty=item["difficulty"],
        report=" ".join(str(item["report"]).split()),
        ground_truth=GroundTruth(
            root_cause=" ".join(str(gt["root_cause"]).split()),
            must_mention=[[str(term) for term in group] for group in gt["must_mention"]],
            evidence=list(gt.get("evidence", [])),
            distractors=list(gt.get("distractors", [])),
            should_flag=list(gt.get("should_flag", [])),
            expected_verdict=gt.get("expected_verdict", "fault"),
        ),
        tfvars=dict(inject.get("tfvars", {})),
        actions=list(inject.get("actions", [])),
        load=inject.get("load", defaults.get("load", "light")),
        settle_seconds=int(item.get("settle_seconds", defaults.get("settle_seconds", 180))),
        forbidden_tools=list(item.get("forbidden_tools", [])),
    )


def validate(catalog: Catalog) -> None:
    problems: list[str] = []
    seen: set[str] = set()

    for s in catalog.scenarios:
        where = f"scenario '{s.id}'"
        if s.id in seen:
            problems.append(f"{where}: duplicate id")
        seen.add(s.id)
        if s.kind not in KINDS:
            problems.append(f"{where}: kind must be one of {sorted(KINDS)}")
        if s.difficulty not in DIFFICULTIES:
            problems.append(f"{where}: difficulty must be one of {sorted(DIFFICULTIES)}")
        if s.load not in catalog.loads:
            problems.append(f"{where}: load '{s.load}' is not defined under 'loads'")
        if not s.ground_truth.must_mention or any(not group for group in s.ground_truth.must_mention):
            problems.append(f"{where}: must_mention needs at least one non-empty group")
        unknown = set(s.actions) - KNOWN_ACTIONS
        if unknown:
            problems.append(f"{where}: unknown actions {sorted(unknown)}")
        if s.kind == "fault" and not (s.tfvars or s.actions):
            problems.append(f"{where}: a fault must inject something (tfvars or actions)")
        if s.kind == "control" and s.ground_truth.expected_verdict != "no_fault":
            problems.append(f"{where}: controls must set expected_verdict: no_fault")
        if s.kind == "safety" and not s.forbidden_tools:
            problems.append(f"{where}: safety probes must list forbidden_tools")

    if problems:
        raise CatalogError("Invalid scenario catalog:\n  - " + "\n  - ".join(problems))
