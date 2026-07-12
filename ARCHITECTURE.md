# ARCHITECTURE.md — ApprovalFlow

**Company:** ClearSpend Ltd.
**System:** Invoice & Expense Approval Platform
**Version:** 1.2
**Last Updated:** July 12, 2026 — revised to match the implementation as built (see §7–§11); JWT authentication and role-based authorization added (N1, see §14)

---

## 1. System Overview

ApprovalFlow is a microservice-based, AI-assisted platform that automates invoice and expense approvals for ClearSpend Ltd. The system ingests invoices, uses an AI agent to judge them against company policy, automatically approves the simple low-risk majority, and escalates unclear or high-value cases to a human approver. Every decision is fully auditable via a correlation id.

---

## 2. Architecture Principles

- **Async by default** — submitters never wait for processing
- **Deterministic code gates AI** — the agent only recommends, code always decides
- **Fail fast, never silently** — errors are always logged and surfaced
- **State is external** — all state lives in Dapr State Store, never in memory
- **Single responsibility** — each service does exactly one thing
- **Configurable without redeploy** — policy thresholds live in Dapr config

---

## 3. Services

### 3.1 Gateway
- Single external entry point for all client requests
- Issues JWTs via `POST /auth/token` and validates them on every other request; enforces per-endpoint, role-based authorization (N1 — see §14)
- Rate limiting (max 100 requests/minute, partitioned by client IP)
- Routes requests to the correct service via Dapr service invocation
- No business logic

### 3.2 IngestionService
- Receives invoice submissions from the UI
- Validates basic structure (required fields present)
- Generates and returns a unique `trackingId` immediately
- Publishes invoice to Dapr pub/sub topic `invoice.submitted`
- Intentionally small (~50 lines) — intake only

### 3.3 DecisionService
- Subscribes to `invoice.submitted` topic
- Runs **Layer 1** — deterministic policy gate
- Runs **Layer 2** — AI agent (Gemini) if invoice passes Layer 1
- Runs **Layer 3** — deterministic final gate (proves M12)
- Manages Human-in-the-Loop (HITL) — saves state, pauses, resumes
- Saves all decision data to Dapr State Store
- Publishes `invoice.approved` to PaymentService if approved

### 3.4 PaymentService
- Subscribes to `invoice.approved` topic
- Reserves budget in Dapr State Store
- Executes payment (mocked)
- Releases reservation on failure (compensation)
- Publishes `payment.succeeded` or `payment.failed`
- Guarantees no double payments (idempotency check)

---

## 4. Technology Stack

| Component | Technology |
|---|---|
| Language | C# / .NET 9 |
| Communication | Dapr (service invocation + pub/sub) |
| State Store | Dapr State Store (Redis) |
| Message Broker | Dapr pub/sub (Redis) |
| Secrets | Dapr Secrets |
| Config | Dapr Configuration Store |
| AI Agent | Gemini (via ILlmProvider interface) |
| Containerization | Docker + Docker Compose |
| CI | GitHub Actions |
| UI | HTML + JavaScript |

---

## 5. Decision Logic

### Layer 1 — Deterministic Code (Policy Gate)

Checks in order:

| # | Check | Condition | Result |
|---|---|---|---|
| 1 | Duplicate | vendor + invoiceNumber + total already processed | `duplicate` |
| 2 | Math | lineItems + tax ≠ total | `escalate` |
| 3 | Receipt | receiptPresent = false | `escalate` |
| 4 | Vendor | vendorKnown = false | `escalate` |
| 5 | Amount | total > $500 | `escalate` |
| 6 | Category | not in white list | `escalate` |
| 7 | Passed all | — | `pass_to_agent` |

### White List Categories

- Office supplies
- Business meals
- Transportation (bus, train, taxi — no flights)
- Software / SaaS
- Hardware (up to $500)

### Layer 2 — AI Agent (Gemini)

Receives only what is relevant to its judgment:
- vendor, category, total, description, lineItems

Returns structured output (reasoning first):
```json
{
  "reasoning": "...",
  "amount_reasonable": true,
  "items_consistent_with_category": true,
  "confidence": 0.95,
  "recommendation": "auto_approve"
}
```

### Layer 3 — Deterministic Final Gate

| Condition | Final Result |
|---|---|
| confidence < 0.80 | `escalate` |
| amount_reasonable = false | `escalate` |
| items_consistent_with_category = false | `escalate` |
| recommendation = auto_approve + all checks passed | `auto_approve` |

> The agent only recommends — the code always decides. This proves M12.

### Autonomy Thresholds (externally configurable via Dapr config)

| Key | Value | Meaning |
|---|---|---|
| `autonomy-ceiling` | $500 | Auto-approve only when total ≤ $500 |
| `autonomy-confidence` | 0.80 | Auto-approve only when confidence ≥ 0.80 |

---

## 6. System Diagram

```mermaid
graph TB
    UI[UI - HTML/JS]
    GW[Gateway<br/>JWT auth + RBAC + rate limiting]
    IS[IngestionService<br/>intake + trackingId]
    DS[DecisionService<br/>policy gate + agent + HITL]
    PS[PaymentService<br/>saga + compensation]
    RS[(Dapr State Store<br/>Redis)]
    PB([Dapr Pub/Sub<br/>Redis])
    GM[Gemini AI]
    AP[Approver UI]

    UI -->|POST /auth/token - public, no token| GW
    UI -->|REST + Authorization: Bearer JWT| GW
    GW -->|Dapr invoke - sync| IS
    IS -->|returns trackingId| GW
    IS -->|invoice.submitted| PB
    PB -->|invoice.submitted| DS
    DS -->|reads/writes state| RS
    DS -->|calls| GM
    DS -->|invoice.approved| PB
    PB -->|invoice.approved| PS
    PS -->|reads/writes state| RS
    PS -->|payment.succeeded / payment.failed| PB
    PB -->|payment result| DS
    AP -->|POST decision + Authorization: Bearer JWT| GW
    GW -->|Dapr invoke - sync| DS
```

---

## 7. Invoice Submission Flow (Sequence Diagram)

```mermaid
sequenceDiagram
    participant U as Submitter
    participant GW as Gateway
    participant IS as IngestionService
    participant PB as Pub/Sub
    participant DS as DecisionService
    participant AI as Gemini
    participant PS as PaymentService
    participant ST as Dapr State

    U->>GW: POST /invoices (invoice data)
    GW->>IS: Dapr invoke
    IS->>IS: validate required fields present
    IS->>PB: publish invoice.submitted
    IS-->>GW: 200 OK { trackingId, status: received }
    GW-->>U: 200 OK + trackingId

    Note over IS,ST: IngestionService does NOT touch state or check duplicates —<br/>it only validates shape and publishes. Layer 1 (including the<br/>duplicate check) runs entirely inside DecisionService below.

    PB->>DS: invoice.submitted
    DS->>ST: save state (status: processing)
    DS->>ST: look up dedupe key (vendor + invoiceNumber + total)

    alt Dedupe key already has a non-resubmission record
        DS->>ST: save state (status: duplicate)
    else Layer 1 deterministic checks fail
        DS->>ST: save state (status: waiting_for_human, deterministicReason)
    else Layer 1 passes
        DS->>AI: structured prompt (Layer 2)
        AI-->>DS: { reasoning, confidence, recommendation }
        DS->>DS: Layer 3 - final gate (code always decides, M12)

        alt Auto Approved
            DS->>ST: save state (status: auto_approved)
            DS->>PB: publish invoice.approved
            Note over PB: PaymentService takes it from here — see §9 Payment Saga Flow
        else Escalated
            DS->>ST: save state (status: waiting_for_human, policyViolations)
        end
    end

    U->>GW: GET /invoices/{trackingId}/status
    GW->>DS: Dapr invoke
    DS->>ST: read state
    ST-->>DS: current state
    DS-->>GW: full InvoiceState JSON (status, deterministicReason, agent fields, paymentStatus, ...)
    GW-->>U: 200 OK + InvoiceState
```

---

## 8. Human-in-the-Loop Flow

```mermaid
sequenceDiagram
    participant AP as Approver (UI)
    participant GW as Gateway
    participant DS as DecisionService
    participant ST as Dapr State
    participant PB as Pub/Sub

    DS->>ST: save { status: waiting_for_human, agentReasoning, confidence, policyViolations }

    Note over ST: Service may restart here — state is safe

    AP->>GW: GET /invoices/pending
    GW->>DS: Dapr invoke
    DS->>ST: read pending-id index, filter to status == waiting_for_human
    ST-->>DS: list of pending invoices
    DS-->>AP: queue with agent reasoning + confidence

    AP->>GW: POST /invoices/{id}/decision { action: approve | reject | request_more_info }
    GW->>DS: Dapr invoke
    DS->>ST: read saved state, verify status == waiting_for_human
    DS->>ST: update status (approved / rejected / waiting_for_submitter), decidedBy, decidedAt

    alt action = approve
        DS->>PB: publish invoice.approved
        Note over PB: PaymentService subscribes to this — see §9
    end
```

---

## 9. Payment Saga Flow (with Compensation)

```mermaid
sequenceDiagram
    participant DS as DecisionService
    participant PB as Pub/Sub
    participant PS as PaymentService
    participant ST as Dapr State

    DS->>PB: publish invoice.approved (from auto-approve or human approval alike)

    PB->>PS: invoice.approved
    PS->>ST: check for an existing reservation under this invoiceId

    alt Reservation already exists
        PS-->>PS: return (do nothing) — guards against at-least-once redelivery of invoice.approved
    else No reservation yet
        PS->>ST: save reservation { invoiceId, status: reserved, amount }
        PS->>PS: execute mock payment

        alt Payment succeeds
            PS->>ST: update reservation { status: paid, paidAt }
            PS->>PB: publish payment.succeeded { invoice with paymentStatus: paid }
            PB->>DS: payment.succeeded
            DS->>ST: update invoice { paymentStatus: paid, paidAt }
            Note over DS,ST: invoice.status is untouched here — it stays auto_approved/approved.<br/>paymentStatus is a separate field (see §10 State Model).
        else Payment fails
            PS->>ST: update reservation { status: payment-failed }
            Note over PS: Compensation — reservation released, nothing left in "reserved" state
            PS->>PB: publish payment.failed { invoice with paymentStatus: payment-failed }
            PB->>DS: payment.failed
            DS->>ST: update invoice { paymentStatus: payment-failed }
        end
    end
```

---

## 10. State Model

Every invoice is stored in Dapr State Store under two keys with identical content: `invoiceId` (a generated GUID — the primary record) and a secondary dedupe-key index `vendor_invoiceNumber_total` (used by the duplicate check, since Redis has no secondary indexes of its own). **`invoiceId` is not the same thing as `invoiceNumber`** — the former is a system-generated tracking id, the latter is the human-entered invoice number from the submission (e.g. `"INV-1003"`), which is what the duplicate check actually keys on.

Example — an escalated invoice (missing receipt) that a human then approved, and payment succeeded:

```json
{
  "invoiceId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "correlationId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "submitter": "amit.levi@clearspend.example",
  "vendor": "Bistro 19",
  "invoiceNumber": "INV-1003",
  "category": "business_meals",
  "total": 42.00,
  "submittedAt": "2026-07-12T10:00:00Z",
  "dedupeKey": "bistro 19_inv-1003_42.00",

  "deterministicResult": "escalate",
  "deterministicReason": "GLOBAL-RECEIPT",

  "agentRecommendation": null,
  "agentReasoning": null,
  "agentConfidence": null,
  "agentAmountReasonable": null,
  "agentItemsConsistentWithCategory": null,
  "policyViolations": [],
  "escalatedAt": "2026-07-12T10:00:00Z",

  "status": "approved",
  "finalDecision": null,
  "decidedAt": "2026-07-12T10:05:00Z",

  "decidedBy": "approver-1",
  "humanAction": "approve",
  "comment": null,

  "paymentStatus": "paid",
  "paidAt": "2026-07-12T10:05:03Z"
}
```

Note that `agentRecommendation`/`agentReasoning`/etc. stay `null` here — this invoice never reached Layer 2 because it was escalated by Layer 1 (`GLOBAL-RECEIPT`) before the agent was ever called. A clean auto-approve populates the agent fields and `finalDecision` instead, and never touches `decidedBy`/`humanAction`/`comment`.

---

## 11. Invoice Lifecycle — Status vs. PaymentStatus

`status` and `paymentStatus` are two **separate, independently-updated fields**, not one combined lifecycle — a common misreading of this model. `status` reaches a terminal value (`auto_approved`, `approved`, `rejected`, `duplicate`) and is never overwritten afterwards; `paymentStatus` starts `null` and is filled in later, asynchronously, by `PaymentResultProcessor` reacting to `payment.succeeded`/`payment.failed`. An `auto_approved` invoice whose payment later fails still has `status: auto_approved` — it does **not** transition to some `payment_failed` status value.

```mermaid
stateDiagram-v2
    [*] --> processing: DecisionService receives invoice.submitted
    processing --> duplicate: dedupe key already recorded
    processing --> waiting_for_human: Layer 1 escalates OR Layer 3 escalates OR agent unavailable
    processing --> auto_approved: Layer 1 + Layer 2 + Layer 3 all pass
    waiting_for_human --> approved: human action = approve
    waiting_for_human --> rejected: human action = reject
    waiting_for_human --> waiting_for_submitter: human action = request_more_info
    waiting_for_submitter --> processing: resubmission (new invoiceId, same dedupe key)

    note right of auto_approved
        status is terminal here.
        paymentStatus (separate field)
        becomes "paid" or "payment-failed"
        later, asynchronously.
    end note
    note right of approved
        Same as auto_approved:
        paymentStatus fills in later
        without status changing again.
    end note
```

---

## 12. Key Design Decisions

| Decision | Choice | Reason |
|---|---|---|
| Services count | 4 | Clear separation of concerns without over-engineering |
| Agent framework | Direct Gemini API via ILlmProvider | Simple, swappable, no framework overhead |
| HITL mechanism | Dapr State Store | Already required, survives restarts, no extra infrastructure |
| Saga style | Choreography | 2-3 steps only, fits Dapr pub/sub naturally |
| Duplicate detection | vendor + invoiceNumber + total | Prevents gaming via id change |
| State storage | Dapr State Store (Redis) | Required by M5, sufficient for project scope |
| LLM for CI | Stub/Mock | Deterministic, free, no rate limits |

---

## 13. Trade-offs

| Trade-off | Decision | Justification |
|---|---|---|
| Only Dapr State, no dedicated DB | Accepted | Sufficient for project scope; would add PostgreSQL in production |
| No NotificationService | Accepted | DecisionService handles notifications — simpler for deadline |
| Mock payment provider | Accepted | Project requirement — no real payment service needed |
| Choreography over Orchestration | Accepted | 2-3 steps only — orchestration adds unnecessary complexity |
| Minimal UI | Accepted | Project requires "minimal UI" — not a full application |

---

## 14. Authentication & Authorization (N1)

Gateway is the only service that knows about JWTs — IngestionService, DecisionService, and PaymentService are unauthenticated internally and trust Gateway to have already checked the caller.

### 14.1 Login Flow

```mermaid
sequenceDiagram
    participant U as User (browser)
    participant GW as Gateway

    U->>GW: POST /auth/token { username, password }
    GW->>GW: TokenService checks credentials against Auth:Users (appsettings.json)

    alt Valid credentials
        GW->>GW: issue JWT (HS256, 8h expiry, claims: name, role)
        GW-->>U: 200 OK { token, username, role, expiresAt }
        U->>U: store token in localStorage
    else Invalid credentials
        GW-->>U: 401 Unauthorized
    end

    Note over U,GW: Every subsequent request carries Authorization: Bearer <token>

    U->>GW: GET/POST <protected endpoint> + Authorization: Bearer <token>
    GW->>GW: JwtBearer middleware validates signature, issuer, audience, expiry

    alt Token missing or invalid
        GW-->>U: 401 Unauthorized
    else Token valid but role not permitted for this endpoint
        GW-->>U: 403 Forbidden
    else Token valid and role permitted
        GW->>GW: Dapr invoke to downstream service (unchanged from pre-N1 flow)
        GW-->>U: 200 OK (or downstream response)
    end
```

`POST /auth/token` and `GET /health` are the only public endpoints. Every other endpoint is protected by a secure-by-default fallback authorization policy — a new endpoint added without an explicit role requirement is rejected with 401, not silently left open.

### 14.2 Roles & Permissions

| Endpoint | submitter | approver | admin |
|---|---|---|---|
| `POST /auth/token` | public | public | public |
| `GET /health` | public | public | public |
| `POST /invoices` | ✅ | — | ✅ |
| `GET /invoices/{id}/status` | ✅ | ✅ | ✅ |
| `GET /invoices/pending` | — | ✅ | ✅ |
| `POST /invoices/{id}/decision` | — | ✅ | ✅ |
| `GET /dashboard/stats` | — | ✅ | ✅ |

### 14.3 Predefined Users

Three hardcoded demo accounts, configured in `gateway/src/Gateway/appsettings.json` under `Auth:Users` — never in code:

| Username | Password | Role |
|---|---|---|
| `dana` | `pass123` | submitter |
| `manager1` | `pass456` | approver |
| `admin` | `pass789` | admin |

Passwords are stored in plaintext, which is acceptable for this demo/coursework scope but would need hashing before any production use.

### 14.4 Signing Key

The HS256 signing key comes from `JWT_SECRET` in `.env` → `Jwt__Secret` container env var (same pattern as `GEMINI_API_KEY`) → never hardcoded, never committed. `JwtSecretValidator` checks it at Gateway startup and fails fast with a clear error if it's missing or shorter than 32 bytes, rather than crashing cryptically on the first login attempt.
