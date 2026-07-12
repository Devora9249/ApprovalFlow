# ADR-002: Choreography over Orchestration for Payment Saga

## Context

The payment flow requires a saga pattern — multiple steps across services with compensation on failure (M9). We needed to choose between two saga implementation styles:

- **Choreography**: each service reacts to events independently via pub/sub, no central coordinator
- **Orchestration**: a central coordinator drives each step explicitly

## Decision

We chose **Choreography** via Dapr pub/sub.

The payment flow consists of 2-3 steps:
1. Reserve budget (PaymentService)
2. Execute payment (PaymentService)
3. On failure: release reservation and publish `payment.failed`

## Consequences

**Positive:**
- Fits naturally with Dapr pub/sub already required by M5 — no new infrastructure
- Loosely coupled — PaymentService does not call DecisionService directly
- Fewer moving parts — no orchestrator class or coordinator service needed
- Simple to implement for a 2-3 step flow

**Negative / Trade-offs:**
- Flow is distributed across services — harder to trace end-to-end without good logging
- Must rely on correlation id (= invoiceId) to follow a single invoice across events
- In a real production system with 10+ saga steps, orchestration would be preferred for visibility

**Why not Orchestration:** adds significant complexity (orchestrator class, step management, rollback tracking) that is not justified for a 2-3 step payment flow. Choreography with structured logs and correlation id provides sufficient traceability for this scope.
