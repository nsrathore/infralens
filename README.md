# InfraLens

An MCP server that lets Claude investigate an AWS environment **safely**. Access is scoped and read-only by default. Every call is audited and budgeted, and write actions need explicit human approval. A seeded-incident benchmark measures how often Claude finds the real root cause.

> Status: week 1 of 12. Read-only tools and the sandbox come first (weeks 1–4), the benchmark in weeks 5–8, and approved write actions in weeks 9–12. See [docs/ROADMAP.md](docs/ROADMAP.md).

## Why

Letting an LLM look at production is usually either useless (no access) or reckless (admin keys). InfraLens sits in between: least-privilege IAM per tool tier, output redaction, hard limits on time windows and result sizes, and an audit trail of every call. A benchmark then shows whether it actually helps.

## How it fits together

```
Claude Desktop / Claude Code / eval harness (MCP client)
        │  stdio, or Streamable HTTP with --http
        ▼
InfraLens.Server  (C# / .NET 10, ModelContextProtocol 2.x)
   ├─ ToolCallGuard: tier check → call budget → span + metrics → redaction → output cap → audit row
   ├─ Read tools:  ECS service health, stopped tasks, task definitions, CloudWatch Logs Insights
   │               (weeks 2–4: metrics, ALB targets/listeners, SQS, DynamoDB, IAM check, Terraform drift)
   └─ Write tools: weeks 9–12, not registered unless MaxTier=Write, each call needs an approval token
        │  AWS SDK for .NET v4, under a per-tier IAM role (infra/iam)
        ▼
Sandbox (Terraform): orders API + in-process SQS worker on ECS Fargate Spot, ALB, SQS + DLQ, DynamoDB
        ▲
incidents/scenarios.yaml: 20 seeded faults + 1 prompt-injection probe + 2 controls, applied by eval/
```

## Repository layout

| Path | What lives there |
| --- | --- |
| `src/InfraLens.Server` | MCP host and tools. `Safety/` (tiers, scope, budget, redaction, guard), `Audit/`, `Tools/`. |
| `tests/InfraLens.Server.Tests` | xUnit v3 tests for the safety pipeline and tool output shaping. |
| `sandbox/app` | The system under test: a small orders API + SQS worker with `FAULT_MODE` switches. |
| `infra/bootstrap` | Apply once: Terraform state bucket, $20/month budget alarm, ECR repository. |
| `infra/iam` | Apply once: one IAM role per tier (`infralens-reader`, `infralens-operator`) and the audit table. |
| `infra/sandbox` | The environment under test. Every fault is a variable; destroy it after each session. |
| `incidents/` | The benchmark catalog (`scenarios.yaml`) and its generated table (`README.md`). |
| `eval/` | Python harness: inject a scenario, run Claude against the server, grade the diagnosis. |
| `docs/` | [Design and safety model](docs/DESIGN.md), [roadmap](docs/ROADMAP.md). |

## Quick start (local, no AWS)

```bash
dotnet build && dotnet test
cd eval && uv sync && uv run pytest && uv run infralens-eval list
```

## Quick start (sandbox)

```bash
# once
terraform -chdir=infra/bootstrap init && terraform -chdir=infra/bootstrap apply -var alert_email=you@example.com
terraform -chdir=infra/iam init -backend-config="bucket=infralens-tfstate-<account>"
terraform -chdir=infra/iam apply -var trusted_principal_arn=<your IAM user or SSO role ARN>
# add the [profile infralens-reader] block printed in infra/iam/outputs.tf to ~/.aws/config

# every session
scripts/sandbox-up.sh                               # ~5 min
dotnet build -c Release                             # .mcp.json points Claude Code at the built server
cd eval && uv run infralens-eval inject bad-image-tag
# ask Claude Code: "We deployed this morning and now the orders API returns 503. Why?"
uv run infralens-eval restore
cd .. && scripts/sandbox-down.sh                    # do not skip
```

Claude Desktop: add the same command and `AWS_PROFILE=infralens-reader` env from `.mcp.json` to `claude_desktop_config.json`, using an absolute path to `infralens.dll`.

## Safety model (short)

- **Tiers:** `Read` (describe, list, query), `Sensitive` (redacted env/payload reads, opt-in), `Write` (changes state). The server registers only tools at or below `InfraLens:MaxTier`, and the guard re-checks every call.
- **Scope:** allowlists for the cluster, services, log-group prefixes and task-definition families. Anything else is refused before an AWS call is made.
- **IAM is the real boundary.** The `reader` role cannot write, and explicitly denies secret values and data-plane reads, even if the code is wrong.
- **Tool output is untrusted.** Logs can contain text aimed at the model (scenario `log-prompt-injection`). Server instructions say so, and output is redacted and capped.

Details: [docs/DESIGN.md](docs/DESIGN.md).

## Cost

Fargate Spot, no NAT gateway, public subnets, on-demand DynamoDB, 3-day log retention, and `scripts/sandbox-down.sh` after every session. The ALB is the main hourly cost (~$0.55/day if left up). Budget alarm at $20/month in `infra/bootstrap`.

## Scope note

Personal project, built on personal time and equipment, against a synthetic orders domain. It contains no employer code, data, schemas or domain logic.

## License

MIT
