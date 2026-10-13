"""Applies a scenario to the sandbox and restores the healthy baseline afterwards.

Terraform-backed faults: write the scenario's tfvars to a JSON var file and `terraform apply` it.
Out-of-band faults (drift, poison messages): boto3 calls after apply, the way a human would break things.
"""

from __future__ import annotations

import json
import subprocess
import tempfile
from pathlib import Path
from typing import Any

import boto3

from .catalog import REPO_ROOT, Scenario

SANDBOX_DIR = REPO_ROOT / "infra" / "sandbox"


def _terraform(*args: str) -> str:
    result = subprocess.run(
        ["terraform", f"-chdir={SANDBOX_DIR}", *args],
        check=True,
        capture_output=True,
        text=True,
    )
    return result.stdout


def outputs() -> dict[str, Any]:
    raw = json.loads(_terraform("output", "-json"))
    return {key: value["value"] for key, value in raw.items()}


def render_tfvars(tfvars: dict[str, Any], account_id: str, region: str) -> dict[str, Any]:
    """Fills {account_id} and {region} placeholders in string values."""

    def fill(value: Any) -> Any:
        if isinstance(value, str):
            return value.replace("{account_id}", account_id).replace("{region}", region)
        if isinstance(value, list):
            return [fill(v) for v in value]
        return value

    return {key: fill(value) for key, value in tfvars.items()}


def apply_baseline() -> None:
    """Healthy state = variable defaults. Also reverts out-of-band drift."""
    _terraform("apply", "-auto-approve", "-input=false")


def inject(scenario: Scenario, region: str) -> None:
    if scenario.tfvars:
        account_id = boto3.client("sts", region_name=region).get_caller_identity()["Account"]
        tfvars = render_tfvars(scenario.tfvars, account_id, region)
        with tempfile.NamedTemporaryFile("w", suffix=".tfvars.json", delete=False) as handle:
            json.dump(tfvars, handle)
            var_file = Path(handle.name)
        try:
            _terraform("apply", "-auto-approve", "-input=false", f"-var-file={var_file}")
        finally:
            var_file.unlink(missing_ok=True)

    for action in scenario.actions:
        ACTIONS[action](region)


# ---------------------------------------------------------------- out-of-band actions


def send_poison_message(region: str) -> None:
    sqs = boto3.client("sqs", region_name=region)
    sqs.send_message(QueueUrl=outputs()["orders_queue_url"], MessageBody="{order_id: 12345, broken")


def scale_to_zero_out_of_band(region: str) -> None:
    out = outputs()
    autoscaling = boto3.client("application-autoscaling", region_name=region)
    autoscaling.register_scalable_target(
        ServiceNamespace="ecs",
        ResourceId=f"service/{out['cluster']}/{out['service']}",
        ScalableDimension="ecs:service:DesiredCount",
        MinCapacity=0,
        MaxCapacity=0,
    )
    boto3.client("ecs", region_name=region).update_service(
        cluster=out["cluster"], service=out["service"], desiredCount=0
    )


def listener_fixed_503(region: str) -> None:
    elb = boto3.client("elbv2", region_name=region)
    elb.modify_listener(
        ListenerArn=outputs()["listener_arn"],
        DefaultActions=[
            {
                "Type": "fixed-response",
                "FixedResponseConfig": {
                    "StatusCode": "503",
                    "ContentType": "text/plain",
                    "MessageBody": "Down for maintenance",
                },
            }
        ],
    )


ACTIONS = {
    "send_poison_message": send_poison_message,
    "scale_to_zero_out_of_band": scale_to_zero_out_of_band,
    "listener_fixed_503": listener_fixed_503,
}
