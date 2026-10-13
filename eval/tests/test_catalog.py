import pytest

from infralens_eval.catalog import CatalogError, load_catalog, validate


@pytest.fixture(scope="module")
def catalog():
    return load_catalog()


def test_catalog_has_twenty_faults_one_safety_probe_and_two_controls(catalog):
    kinds = [s.kind for s in catalog.scenarios]
    assert kinds.count("fault") == 20
    assert kinds.count("safety") == 1
    assert kinds.count("control") == 2


def test_ids_are_unique_and_kebab_case(catalog):
    ids = [s.id for s in catalog.scenarios]
    assert len(ids) == len(set(ids))
    assert all(i == i.lower() and " " not in i and "_" not in i for i in ids)


def test_every_category_has_at_least_one_fault(catalog):
    categories = {s.category for s in catalog.scenarios if s.kind == "fault"}
    assert {"deploy", "config", "networking", "iam", "capacity", "messaging", "data", "drift"} <= categories


def test_difficulty_spread(catalog):
    faults = [s for s in catalog.scenarios if s.kind == "fault"]
    counts = {d: sum(s.difficulty == d for s in faults) for d in ("easy", "medium", "hard")}
    assert all(c >= 3 for c in counts.values()), counts


def test_tfvars_only_use_sandbox_variables(catalog):
    import re

    from infralens_eval.catalog import REPO_ROOT

    variables_tf = (REPO_ROOT / "infra" / "sandbox" / "variables.tf").read_text()
    declared = set(re.findall(r'variable "([a-z_]+)"', variables_tf))
    for s in catalog.scenarios:
        unknown = set(s.tfvars) - declared
        assert not unknown, f"{s.id} sets undeclared variables {unknown}"


def test_reports_do_not_leak_the_answer(catalog):
    # The report is what a human would say; it must not name the injected setting.
    for s in catalog.scenarios:
        for key, value in s.tfvars.items():
            assert key not in s.report, f"{s.id}: report names variable {key}"
            if isinstance(value, str) and len(value) > 3 and value not in ("none",):
                assert value not in s.report, f"{s.id}: report contains injected value {value!r}"


def test_validation_catches_a_bad_scenario(catalog):
    from dataclasses import replace

    broken = replace(catalog.scenarios[0], kind="weird", tfvars={}, actions=[])
    with pytest.raises(CatalogError):
        validate(replace(catalog, scenarios=[broken]))
