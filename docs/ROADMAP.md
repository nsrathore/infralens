# Roadmap (Oct 12, 2026 – Jan 3, 2027)

About 5–7 hours a week go to InfraLens (Thursday evening + a 3-hour Saturday block). Interview prep and applications get the rest.

| Milestone | Weeks | Ends | Done when |
| --- | --- | --- | --- |
| **v1: read-only tools on a live sandbox** | 1–4 | Sun Nov 8 | All read tools below work in Claude Code against the sandbox; CI green; audit in DynamoDB; traces in CloudWatch; README with a demo GIF. |
| **v2: seeded-incident benchmark** | 5–8 | Sun Dec 6 | All 23 scenarios × 3 trials run unattended; LLM judge calibrated against keyword grades; results table in `docs/RESULTS.md`. |
| **v3: approved writes + write-up** | 9–12 | Sun Jan 3 | Write tier behind signed approval tokens; safety probe passes; write-up post with results and costs. |

## Week 1 (Oct 12–18): skeleton to first live diagnosis

- [ ] Read the employment agreement's IP / side-project clause. Keep the repo private until it is clear.
- [ ] Create `nsrathore/infralens`, push this skeleton, and get CI green. The C# was written against the MCP C# SDK 2.2 source but never compiled, so expect a few fixes. Then set `TreatWarningsAsErrors=true`.
- [ ] Turn on MFA (passkey) for the Google/GitHub account used to sign in; use `aws login --profile infralens-admin` for daily CLI use.
- [ ] `infra/bootstrap` apply: state bucket, $20 budget alarm (confirm the email), ECR repo.
- [ ] `infra/iam` apply: `infralens-reader` role + audit table; add the `infralens-reader` profile to `~/.aws/config`.
- [ ] `scripts/sandbox-up.sh`, then hit `/health` and `POST /orders` through the ALB. Note how long `up` takes and what the AWS bill shows next morning.
- [ ] Point Claude Code at the server (`.mcp.json`). Ask for the health of `infralens-api`; check that `ecs_service_health`, `ecs_stopped_tasks` and `logs_recent_errors` return sensible output, and that audit lines appear on stderr.
- [ ] `uv run infralens-eval inject bad-image-tag`, diagnose it with Claude, `restore`. Write down which tool you wished you had.
- [ ] `scripts/sandbox-down.sh`.

Stretch: `elb_target_health` (unblocks 5 scenarios).

## Weeks 2–4: remaining read tools (names match `incidents/scenarios.yaml` evidence lists)

| Tool | Unblocks | AWS calls |
| --- | --- | --- |
| `elb_target_health` | health-check path, SG, grace, port mismatch | `DescribeTargetHealth` |
| `cloudwatch_metrics` | OOM, throttling, autoscaling, ALB timeout, controls | `GetMetricData` (ALB, ECS, SQS, DynamoDB presets) |
| `sqs_queue_stats` | visibility, poison, DLQ, wrong queue | `GetQueueAttributes` incl. redrive policy |
| `dynamodb_table_summary` | throttling cap, missing GSI | `DescribeTable` |
| `iam_role_check` | task-role incidents, safety probe | `SimulatePrincipalPolicy` on the task role |
| `elb_listener_rules` | listener drift | `DescribeListeners`, `DescribeRules` |
| `terraform_drift` | both drift incidents | `terraform plan -refresh-only -json`, summarised |

Also: DynamoDB audit sink on, OpenTelemetry Collector → CloudWatch, GitHub OIDC role for `terraform plan` in CI, demo GIF.

## Weeks 5–8: benchmark

- Run every scenario 3× with the default model; record pass rate, tool calls, tokens, cost and wall time per scenario.
- Add an LLM judge, calibrated against keyword grades on ~40 hand-labelled runs; report agreement.
- Ablations: with and without `logs_recent_errors`; a smaller model; a capped call budget.
- Turn on the nightly 4-scenario subset (`.github/workflows/nightly-eval.yml`).

## Weeks 9–12: writes, safety, write-up

- Write tier: `ecs_force_new_deployment`, `ecs_set_desired_count`, `sqs_redrive_dlq`, each requiring a signed approval token (see DESIGN.md).
- Re-run the safety probe at `MaxTier=Write`; it must still pass.
- Optional: read-only Kubernetes tools against a local kind cluster.
- Write-up: problem, design, safety model, results table, cost, what failed.
