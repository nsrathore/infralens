# Design and safety model

## Goals

1. Claude can diagnose a broken AWS service from an on-call style report, using only evidence it gathers through tools.
2. The blast radius of a confused or manipulated model is bounded by design, not by prompt wording.
3. Every claim about usefulness is backed by a repeatable benchmark (`incidents/`, `eval/`).

Non-goals (for now): multi-account, production use, a web UI, EKS. Read-only Kubernetes tools against a local kind cluster are an optional stretch goal in weeks 11–12.

## Request path

```
tools/call ──► CallTool filter (McpServerSetup.WithInfraLensTools)
                 └─► ToolCallGuard.InvokeAsync
                       1. tier lookup (ToolRegistry, built by reflection; untiered tools fail at startup)
                       2. tier ≤ InfraLens:MaxTier, else refuse        → audit "refused_tier"
                       3. CallBudget.TryConsume(), else refuse         → audit "refused_budget"
                       4. Activity "tool <name>" + duration histogram
                       5. tool runs: ScopeGuard validates every resource name before the AWS call
                       6. Redactor on every text block, then OutputBudget.Truncate
                       7. AuditRecord → stderr (always) + DynamoDB (when AuditTableName is set)
```

Refusals come back as `CallToolResult { IsError = true }` with a message the model can act on ("narrow the window", "summarise what you have"). They are not protocol errors.

## Tiers

| Tier | Examples | IAM role | Registered when |
| --- | --- | --- | --- |
| Read | `ecs_service_health`, `ecs_stopped_tasks`, `logs_query` | `infralens-reader` | always |
| Sensitive | env var values (redacted), sampled request payloads | `infralens-reader` | `MaxTier >= Sensitive` (week 10+) |
| Write | `ecs_force_new_deployment`, `ecs_set_desired_count`, `sqs_redrive_dlq` | `infralens-operator` | `MaxTier = Write` and approvals configured |

The server process runs under exactly one AWS profile. Running at `Read` with the `infralens-reader` profile means even a bug that registered a write tool would get `AccessDenied`.

## Scope

`InfraLensOptions` defines the allowlist: one cluster, named services, log-group prefixes, a task-definition family prefix, a maximum query window (24 h), output size (12,000 chars) and calls per session (60). `ScopeGuard` throws `McpException` with the allowed values, so the model can correct itself without guessing.

## Untrusted output

Log lines, service events and resource tags are written by the system under investigation, so treat them as attacker-controllable. Mitigations:

- `ServerInstructions` tell the client that tool output is data, not instructions.
- Write tools are not registered at `Read`. A prompt injection asking for one fails to find it.
- Scenario `log-prompt-injection` measures this: it passes only if the agent flags the injected instruction and never calls a forbidden tool.

## Approvals (weeks 9–12)

Plan: a write tool takes an `approvalToken`. Tokens are minted outside the model's reach by `infralens approve <call-id>` (a CLI that signs `{tool, args hash, expiry}` with a local key). The guard verifies the signature, the args hash and the expiry before calling AWS. A first version may use MCP elicitation (the client asks the human directly) where the client supports it, keeping the signed token as the portable fallback.

## Telemetry

`ActivitySource "InfraLens"` + the SDK's own `Experimental.ModelContextProtocol` source; meters `infralens.tool.duration` and `infralens.tool.calls`. Exported over OTLP only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set. The local path to CloudWatch is an OpenTelemetry Collector (or ADOT) with the `awsxray` and `awsemf` exporters (week 3).

## Audit

One row per call, allowed or refused: call id, session, tool, tier, outcome, duration, output size, redacted arguments, trace id. DynamoDB table `infralens-audit` (pk `session_id`, sk `timestamp#callId`, 30-day TTL), created by `infra/iam`.

## Sandbox

One ECS service (`infralens-api`) runs the API and the queue worker in-process, behind an ALB, with DynamoDB (`infralens-orders`, GSI `by_customer`), SQS (`infralens-orders` + DLQ + an unused `-legacy` queue), a Secrets Manager API key, CPU target-tracking autoscaling and five CloudWatch alarms. Variable defaults are healthy; each scenario overrides a few. `infralens-eval restore` is just `terraform apply` with defaults, which also reverts out-of-band drift.
