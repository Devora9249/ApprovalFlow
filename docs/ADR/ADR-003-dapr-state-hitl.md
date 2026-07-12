# ADR-003: Dapr State Store for HITL Pause/Resume

## Context

When an invoice is escalated for human review, the system must pause and wait for a human decision. The challenge: if the service restarts between escalation and the human's decision, the waiting state must survive (M11).

Options considered:
- **Dapr State Store** — save waiting state externally in Redis via Dapr
- **MAF Workflow with `request_info`** — built-in pause/resume mechanism
- **Dedicated database** — a table with a `pending_approval` status column

## Decision

We chose **Dapr State Store** for HITL pause/resume.

When an invoice is escalated:
- Status `waiting_for_human` is saved to Dapr State Store under the invoiceId
- The service returns — it does not block or hold anything in memory
- When the human submits a decision, DecisionService reads the saved state and resumes

## Consequences

**Positive:**
- Dapr State Store is already required by M5 — no new infrastructure needed
- Survives service restarts — state is external, not in memory
- Simple to implement — read/write key-value
- Consistent with how all other invoice state is managed in the system

**Negative / Trade-offs:**
- Pause/resume logic must be written manually (~30 lines) rather than being framework-provided
- In a system with many complex multi-step workflows, MAF Workflow would be more appropriate

**Why not MAF Workflow:** adds a new framework dependency and learning curve for a single pause point. Dapr State Store already provides everything needed.

**Why not dedicated database:** adds an entire database just for this purpose when Dapr State Store already provides the same capability.
