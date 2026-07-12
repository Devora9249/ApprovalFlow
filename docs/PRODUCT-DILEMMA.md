# PRODUCT-DILEMMA.md — Autonomy Posture Decision

## The Dilemma

How much autonomy should the system have?
How much money and which categories may the agent approve without human involvement?

The assignment provided a sample `policy.md` with conservative defaults ($250 ceiling)
and detailed per-category rules. We had to choose our own posture and defend it.

---

## Our Decision

| Parameter | Value | policy.md default |
|---|---|---|
| Autonomy ceiling | **$500** | $250 |
| Confidence threshold | **0.80** | 0.80 |
| White list | **5 categories** | per-category sub-limits |

### White list categories:
- `office_supplies`
- `business_meals`
- `transportation` (bus, train, taxi — **no flights**)
- `software`
- `hardware` (up to $500)

---

## Why $500 and not $250?

The assignment explicitly states the system should automatically approve
**"the simple, low-risk majority (the boring 80%)"**.

A $250 ceiling does not cover the boring 80%. Standard everyday business expenses
routinely exceed $250:

- A business lunch for 3-4 people: $150–$300
- A basic office keyboard + mouse: $150–$250
- A monthly SaaS subscription (Jira, Slack): $99–$200
- A local taxi for a day trip: $80–$200

With a $250 ceiling, nearly every routine expense would require human review —
defeating the purpose of the system.

Furthermore, the fixtures provided in the assignment itself confirm this expectation:
INV-1001 ($42), INV-1016 ($48), INV-1017 ($180) are all designed to auto-approve —
and none of them would be blocked by a $500 ceiling. The assignment's own test cases
implicitly validate our choice.

**$500 allows the system to handle routine daily expenses automatically,
while escalating anything significant to a human.**

---

## Why a simplified whitelist instead of per-category sub-limits?

We implemented all **global rules** from policy.md:
- `GLOBAL-DUP` — duplicate detection ✅
- `GLOBAL-MATH` — math mismatch check ✅
- `GLOBAL-RECEIPT` — missing receipt check ✅
- `GLOBAL-VENDOR` — unknown vendor check ✅

For **per-category sub-limits** (MEAL-01's $75/attendee, TRAVEL-02's $1,500 cap,
SAAS-01's $200/month, HW-02's capital expense threshold) — we chose a simplified
whitelist approach instead.

Our reasoning:

We prioritized building a **complete, end-to-end system** over implementing every
per-category rule. Once we verified that the system correctly enforces all global rules
and that the deterministic gate reliably prevents auto-approval above the ceiling —
we were confident the architectural foundation was sound.

Per-category sub-limits are **business-specific policy details**, not architectural
decisions. In a real deployment, every company would bring their own policy rules —
ClearSpend Ltd.'s specific numbers would be replaced by the actual client's thresholds.
The system is designed to make those rules easy to add (they slot directly into Layer 1's
deterministic gate) without changing the architecture.

**The whitelist proves the core principle: if the category isn't trusted, escalate.
If the amount exceeds the ceiling, escalate. The agent only judges what's left.**

---

## Risks We Accept

We acknowledge that a simplified policy introduces edge cases:

- An alcohol-only meal submitted as "business meals" could pass Layer 1
- A flight submitted as "transportation" under $500 could pass Layer 1
- A $450/month SaaS that exceeds policy.md's $200 sub-limit could pass Layer 1

However, we have three layers of protection against these cases:

**Layer 1 — Deterministic gate:**
Catches clear violations before the agent is ever called.

**Layer 2 — AI Agent:**
Checks whether the description and line items are consistent with the category
and amount. In practice, the agent correctly flagged inconsistencies —
for example, a PlayStation 5 submitted as "office supplies" (INV-1018)
and a $1,820 client dinner (INV-1003).

**Layer 3 — Confidence threshold:**
If the agent is uncertain (confidence < 0.80), it escalates automatically —
regardless of its recommendation. Borderline cases always go to a human.

The system **fails safely**: when in doubt, it escalates rather than approves.

---

## How We Prove the System Can Never Auto-Approve Above $500 (M12)

The $500 ceiling is enforced in **Layer 1 — deterministic code**,
before the AI agent is ever called. This is not a prompt instruction to the agent —
it is a hard coded check in `PolicyGate.cs`:

```csharp
if (invoice.Total > settings.CeilingAmount)
    return PolicyGateResult.Escalate("AUTONOMY-CEILING");
```

Since this check runs **before** the agent, the agent cannot influence it.
Even if the agent were compromised, injected with a malicious prompt, or simply wrong —
it never sees invoices above $500. The decision is made entirely in deterministic code
with no AI component.

This is verified by:
- Unit tests confirming any invoice above $500 returns `AUTONOMY-CEILING` (never `auto_approved`)
- The verification script (`verify.ps1`) which runs this as part of the anti-cheese guards
- `FinalDecisionGate` (Layer 3) which also cannot auto-approve above the ceiling —
  it can only escalate or approve what Layer 1 already passed

---

## Trade-offs Accepted

| Trade-off | Decision | Reason |
|---|---|---|
| No per-category sub-limits | Accepted | Business-specific details; easy to add later |
| No GLOBAL-FRAUD detection | Accepted | Agent judgment + confidence threshold mitigates |
| No GLOBAL-FX (foreign currency) | Accepted | Out of scope for this implementation |
| No department budget tracking | Accepted | Requires additional infrastructure |
| Dapr Secrets vs env vars | Accepted | Env vars sufficient for project scope; architecture supports swap |
| WhiteListCategories not hot-reloadable without restart | Accepted | Restart is acceptable for policy changes in this scope |

---

## Summary

We chose a **simple, provable, and extensible** autonomy posture:

- **$500 ceiling** — covers the boring 80% of routine expenses
- **5-category whitelist** — clear, auditable, easy to extend
- **Three-layer protection** — deterministic → AI → deterministic
- **Fails safely** — uncertainty always escalates, never approves

The goal was to prove that the architectural foundation works correctly —
not to replicate every business rule of one specific company's policy.
In production, Layer 1's deterministic gate would be extended with
the real client's specific rules, without changing the architecture.
