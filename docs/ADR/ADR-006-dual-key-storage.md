# ADR-006: Dual-Key Storage Pattern in Dapr State Store

## Context

The system needs two different lookup patterns for the same invoice data:

1. **Lookup by `invoiceId`** — required by status checks (F2) and the approval queue. The invoiceId (returned as trackingId) is the only identifier ever exposed to the submitter.

2. **Lookup by `vendor + invoiceNumber + total`** — required by duplicate detection (F3). A resubmitted invoice arrives as a new request with no reference to the original invoiceId.

Dapr State Store is a plain key-value store — no secondary indexes, no query-by-field.

## Decision

We store each invoice's state under **two keys**, both pointing to the same `InvoiceState` object:

```
decision-service||{invoiceId}                           → full invoice state
decision-service||{vendor}_{invoiceNumber}_{total:F2}   → same invoice state (dedupe index)
```

All writes go through a single `UpdateInvoiceState` method that updates both keys, preventing any code path from updating one while forgetting the other.

**Special case:** when status is `waiting_for_submitter` (after `request_more_info`), a resubmission with the same business key is NOT treated as a duplicate — the duplicate check reads the status of the existing record before blocking.

## Consequences

**Positive:**
- Both required lookup patterns are satisfied without a second database
- No new infrastructure — reuses Dapr State Store already required by M5
- Centralizing writes through `UpdateInvoiceState` prevents the two copies from drifting out of sync

**Negative**
- Data is duplicated in storage (2x writes, 2x space) for every invoice
- Any new code path that mutates invoice state must go through `UpdateInvoiceState` — never write directly to a single key
- In a relational database, this would be a single record with a unique index — the dual-key pattern is a Redis-specific workaround


