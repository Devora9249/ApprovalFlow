# DEVELOPMENT.md — ApprovalFlow

Local development environment reference: what runs where, how to start it, and how to debug it. See [ARCHITECTURE.md](ARCHITECTURE.md) for system design and [CLAUDE.md](CLAUDE.md) for build rules.

---

## 1. Services and ports

| Service | Docker Compose (host → container) | Local `dotnet run` port (launchSettings, no Dapr) | Notes |
|---|---|---|---|
| Gateway | `5000` → `8080` | `5010` | Entry point, rate limiting, no business logic |
| IngestionService | `5001` → `8080` | `5027` | `POST /invoices` |
| DecisionService | `5002` → `8080` | `5282` (launchSettings) / `5002` (VS Code debug config) | Subscribes to `invoice.submitted`, policy gate; also subscribes to `payment.succeeded`/`payment.failed` |
| PaymentService | `5003` → `8080` | — | Subscribes to `invoice.approved`, runs the payment saga |
| Redis | `6380` → `6379` | — | Backs both Dapr state store and pub/sub |

Each app service also has its own Dapr sidecar container (`<service>-dapr`), sharing that service's network namespace (`network_mode: service:<name>`), running `daprd` on its own internal `3500` (HTTP) / gRPC — not exposed to the host individually.

**Important:** the "local `dotnet run` port" column only works for endpoints that don't touch Dapr. Any call to `DaprClient` (state store, pub/sub, service invocation) fails with a connection error unless a Dapr sidecar is actually attached — plain `dotnet run` / IDE launch never starts one. This is why DecisionService needs the VS Code debug setup described below instead of a plain F5.

---

## 2. Running the full stack (Docker Compose)

```powershell
docker compose up --build
```

Starts `redis`, `gateway` (+ sidecar), `ingestion-service` (+ sidecar), `decision-service` (+ sidecar), `payment-service` (+ sidecar). Health checks:

```powershell
Invoke-RestMethod http://localhost:5000/health
Invoke-RestMethod http://localhost:5001/health
Invoke-RestMethod http://localhost:5002/health
Invoke-RestMethod http://localhost:5003/health
```

To exercise Journey D (payment failure + compensation) with a specific invoice instead of relying on the `INV-1012` fixture, set `FORCE_PAYMENT_FAIL=true` in `.env` before `docker compose up` — PaymentService will fail every mock payment while it's set.

Submit a test invoice (see `fixtures/sample-invoices.json` for ready-made scenarios covering each PolicyGate check):

```powershell
$invoice = (Get-Content fixtures/sample-invoices.json | ConvertFrom-Json)[0]
$response = Invoke-RestMethod -Method Post -Uri http://localhost:5001/invoices -Body ($invoice | ConvertTo-Json -Depth 5) -ContentType "application/json"
Invoke-RestMethod http://localhost:5002/invoices/$($response.trackingId)/status
```

---

## 3. Debugging DecisionService locally (VS Code)

Files: `.vscode/tasks.json`, `.vscode/launch.json`, `dapr/components-local/*.yaml` (not committed — `.vscode/` is gitignored; `dapr/components-local/` is tracked).

**Why a separate setup is needed:** F5 alone just runs the DLL — no Dapr sidecar, so every `DaprClient` call fails. The debug config instead runs the Dapr sidecar as a background task, then launches `DecisionService.dll` directly under the debugger with `DAPR_HTTP_PORT`/`DAPR_GRPC_PORT` set so it can find that sidecar.

**Steps:**
1. Stop the containerized instance so port 5002 is free:
   ```powershell
   docker compose stop decision-service decision-service-dapr
   ```
2. Open the Run and Debug panel (`Ctrl+Shift+D`), select **"DecisionService (with Dapr sidecar)"** from the dropdown.
3. Press F5 (or the play ▶ button). This runs, in order:
   - `build-decision-service` — `dotnet build`
   - `start-dapr-sidecar-decision-service` — runs `dapr run --app-id decision-service --app-port 5002 --dapr-http-port 3500 --dapr-grpc-port 50001 --resources-path dapr/components-local` as a background task (its own terminal tab; waits for the line `You're up and running!` before VS Code proceeds)
   - Launches `DecisionService.dll` directly under the debugger, `ASPNETCORE_URLS=http://localhost:5002`

**Why `dapr/components-local/` exists separately from `dapr/components/`:** the real components point `redisHost` at `redis:6379` — the Docker-internal DNS name, which only resolves inside the compose network. A sidecar run directly on the host (not in a container) needs `localhost:6380` instead — the host-mapped Redis port. Both component sets point at the *same* Redis container; it's just two different addresses for reaching it.

**Known gotchas:**
- VS Code does **not** automatically stop the background sidecar task when you stop debugging. Before your next F5, either `Ctrl+C` in that task's terminal tab, or run `dapr stop --app-id decision-service`, or you'll get a port-already-in-use error.
- If `dapr` isn't recognized in a VS Code integrated terminal/task even though it works in a plain terminal: VS Code inherited a stale `PATH` from whenever it was last launched (before `dapr` was added to PATH via `winget`). The task in `tasks.json` calls the absolute path `C:\dapr\dapr.exe` specifically to route around this — if it ever needs to change, that's why.
- Never run the containerized `decision-service` and the local debug instance at the same time — same Dapr app-id means they'd be two competing consumers in the same Redis Streams consumer group, splitting `invoice.submitted` messages unpredictably between them.

---

## 4. Logs

Serilog writes to **both** console and a rolling daily file, for Gateway, IngestionService, and DecisionService. Location depends on how the process is running:

| How it's running | Log file location |
|---|---|
| Docker Compose | `./logs/<service-name>/<service-name>-YYYYMMDD.log` on the host (volume-mounted from `/app/logs` in each container) |
| Local VS Code debug (DecisionService only) | `decision-service/src/DecisionService/logs/decision-service-YYYYMMDD.log` (relative to that process's own working directory — a different location than the Docker-mounted one) |

`*.log` is gitignored, so none of this gets committed. Docker volume mounts only take effect on container **recreation** — a plain restart of an already-running container won't pick up a newly added mount; use `docker compose up --build` (or at least recreate the affected service).

Live-tail a containerized service:
```powershell
docker compose logs -f decision-service
```

---

## 5. Redis / Dapr State Store inspection

Both the Docker sidecars and the local debug sidecar point at the same Redis instance (just via different hostnames — see §3). Useful commands:

```powershell
# List all keys
docker compose exec redis redis-cli KEYS "*"

# Read one invoice's state (both the invoiceId key and the vendor/invoiceNumber/total dedupe key exist per invoice — see InvoiceProcessor.cs)
docker compose exec redis redis-cli GET "decision-service||<invoiceId>"

# Wipe everything (fresh slate for retesting journeys)
docker compose exec redis redis-cli FLUSHALL
```

Every processed invoice is written under **two** Redis keys with identical content: `decision-service||<invoiceId>` (the primary record, used by `GET /invoices/{id}/status`) and `decision-service||<vendor>_<invoiceNumber>_<total>` (a secondary index the duplicate check reads by natural key, since Redis has no secondary indexes of its own). Seeing two entries with the same JSON content is expected, not a bug.

---

## 6. Configuration

Autonomy thresholds (ceiling, confidence, category whitelist, known vendors) live in `AutonomySettings`, bound from each environment's config — **not** hardcoded inside `PolicyGate.cs`. Two layers:

- `decision-service/src/DecisionService/appsettings.json` — defaults for `WhiteListCategories` and `KnownVendors` (arrays are easiest to edit here).
- `docker-compose.yml` environment variables — `Autonomy__CeilingAmount` / `Autonomy__ConfidenceThreshold`, sourced from a local `.env` file (copy `.env.example` → `.env`, not committed) via `${AUTONOMY_CEILING:-500}` / `${AUTONOMY_CONFIDENCE:-0.80}`.

This is a deliberate simplification of CLAUDE.md's "Dapr Configuration Store" requirement — using `IConfiguration` instead of the real Dapr Configuration API (which would need a `configuration.redis` component pre-seeded with matching keys). The chosen tradeoff: reliability now over spec-literalism, since the seeding step couldn't be verified without actually running it. Revisit if a real Dapr Configuration Store becomes a hard requirement later.

`GEMINI_API_KEY`, `LLM_PROVIDER`, `LLM_MODEL` in `.env.example` are not wired up yet — that's Day 6 (the AI agent / Layer 2).

---

## 7. Known design gaps to keep in mind

- **Known-vendor list** (`AutonomySettings.KnownVendors`) is a static config list — CLAUDE.md never specifies where this should come from; this was a judgment call, not a spec requirement.
- **Dapr pub/sub is at-least-once, not exactly-once.** `InvoiceProcessor.ProcessAsync` guards against redelivery of the same `invoiceId` (checks for an existing record before doing anything), but this isn't concurrency-safe against two *simultaneous* deliveries — acceptable for this project's scope, not for a high-throughput production system.
- **The dedupe-key secondary index needs manual upkeep.** When the HITL `request_more_info` endpoint (Day 6+) sets an invoice's own status to `waiting_for_submitter`, it must also update the dedupe-key copy, or a legitimate resubmission will be wrongly flagged as a duplicate (see the `NOTE` comment in `InvoiceProcessor.cs`).
