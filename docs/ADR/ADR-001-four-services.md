# ADR-001: Four Microservices Architecture

## Context

The assignment required at least 3 microservices. We needed to decide how many services to build and what the responsibility of each would be.

Options considered:
- **3 services** (minimum): Gateway → AppService (everything) → PaymentService
- **4 services**: Gateway → IngestionService → DecisionService → PaymentService
- **5+ services**: adding NotificationService, PolicyService, etc.

## Decision

We chose **4 services**:

- **Gateway** — single external entry point, rate limiting, no business logic
- **IngestionService** — receives invoice, returns trackingId immediately, publishes to pub/sub
- **DecisionService** — all decision logic (Layer 1 + Layer 2 + Layer 3), HITL, state management
- **PaymentService** — mock payment execution, saga, compensation

## Consequences

**Positive:**
- Each service has one clear responsibility (Single Responsibility Principle)
- IngestionService is intentionally tiny (~50 lines) — separates intake from processing
- Clear boundary between business logic (Decision) and payment execution (Payment)


**Negative**
- IngestionService adds one more Dockerfile and compose entry for a very small service
- NotificationService was considered but merged into DecisionService to save time — this is a known trade-off

**Why not 3:** merging Ingestion into Decision means the service that runs the agent and manages HITL is also responsible for receiving requests and returning immediate responses — violates Single Responsibility and makes the service harder to test independently.

**Why not 5+:** NotificationService would add complexity not justified by the project scope.
