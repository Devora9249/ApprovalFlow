# ADR-004: $500 Autonomy Ceiling and Whitelist Approach

## Context

The assignment required choosing an autonomy posture: how much money and which categories the agent may approve autonomously. The provided `policy.md` suggested $250 as a conservative default with detailed per-category rules.



## Decision

We chose:
- **Autonomy ceiling: $500** (changed from policy.md default of $250)
- **Confidence threshold: 0.80**
- **Whitelist of 5 categories**: office_supplies, business_meals, transportation (no flights), software, hardware

All global rules from policy.md were implemented:
- `GLOBAL-DUP` — duplicate detection
- `GLOBAL-MATH` — math mismatch
- `GLOBAL-RECEIPT` — missing receipt
- `GLOBAL-VENDOR` — unknown vendor

Per-category sub-limits (MEAL-01, TRAVEL-02, SAAS-01, HW-02) were replaced with the simplified whitelist approach.

## Consequences

**Positive:**
- $500 covers the "boring 80%" of routine business expenses
- Simple whitelist is easy to audit and prove (supports M12)
- All global rules from policy.md are implemented
- Per-category rules are easy to add later without changing the architecture

**Negative**
- Edge cases exist: alcohol-only meals, flights under $500, expensive SaaS could pass Layer 1
- Mitigated by: AI agent judgment + confidence threshold + Layer 3 deterministic gate
- Per-category sub-limits are business-specific — every real client would bring their own


