# ADR-005: Dapr State Store Only (No PostgreSQL)

## Context

The system needs to support audit trail (F9), dashboard queries (F8), and duplicate detection (F3). The assignment mentioned the system should serve "millions of users," raising the question of whether Dapr State Store (Redis-backed) is sufficient or whether a relational database (PostgreSQL) should be added.

This decision was also discussed with the course mentor (Mila), who confirmed both options are acceptable as long as the choice is justified and documented.

## Decision

We use **Dapr State Store (Redis-backed) exclusively** — no PostgreSQL.

- **Audit trail (F9):** every invoice's full history is saved under its invoiceId key — deterministic result, agent reasoning, human decision, payment outcome, all linked by correlationId
- **Dashboard (F8):** DecisionService maintains an `all-invoices-queue` index in Dapr State, used to compute counts and sums by status
- **Duplicate detection (F3):** secondary index pattern — same data written under a second key (`vendor_invoiceNumber_total`)

## Consequences

**Positive:**
- Simple — no new infrastructure, no ORM, no migrations
- Dapr State Store is already required by M5
- Sufficient for project scope and demo

**Negative / Trade-offs:**
- Not suitable for true production scale — Redis has no complex query support and no long-term archival strategy
- KEYS-based scanning does not scale to millions of records
- In a real production system serving millions of users, we would add PostgreSQL for the audit/reporting layer, keeping Dapr State only for hot/active invoice state

**This is a deliberate, time-constrained trade-off** — not an oversight. The architecture supports adding PostgreSQL later without changing the service interfaces.
