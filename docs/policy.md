# ClearSpend Ltd. — Expense Approval Policy

**Version:** 1.0
**Effective:** July 2026
**Owner:** Finance Department

---

## 1. Autonomy Thresholds

| Parameter | Value |
|---|---|
| Autonomy ceiling | $500 |
| Confidence threshold | 80% (0.80) |

Invoices below the ceiling **may** be auto-approved if they pass all checks.
Invoices at or above the ceiling **always** require human approval.

---

## 2. Approved Categories (White List)

Only invoices in the following categories are eligible for auto-approval:

| Category | Notes |
|---|---|
| `office_supplies` | Pens, paper, office furniture, peripherals |
| `business_meals` | Work meals with attendee details |
| `transportation` | Bus, train, taxi — **flights are NOT included** |
| `software` | SaaS subscriptions and software licenses |
| `hardware` | Physical equipment — up to $500 only |

Any category not on this list → **automatic escalation**, no agent call.

---

## 3. Global Rules (Applied to All Invoices)

These rules apply regardless of category or amount:

| Rule | Condition | Result |
|---|---|---|
| `GLOBAL-DUP` | Same vendor + invoiceNumber + total already processed | `duplicate` — blocked entirely |
| `GLOBAL-MATH` | Line items + tax ≠ total | `escalate` |
| `GLOBAL-RECEIPT` | Receipt missing and total > $25 | `escalate` |
| `GLOBAL-VENDOR` | Vendor not in known vendors list | `escalate` |
| `AUTONOMY-CEILING` | Total > $500 | `escalate` |
| `CATEGORY-NOT-ALLOWED` | Category not on white list | `escalate` |

---

## 4. AI Agent Evaluation

Invoices that pass all global rules are evaluated by the AI agent, which checks:

1. **Is the amount reasonable** for the stated category and description?
2. **Are the line items consistent** with the stated category?

The agent returns a confidence score (0.0–1.0). If confidence < 0.80 → escalate.

The agent **only recommends** — the deterministic final gate always decides.

---

## 5. Final Decision Gate (Layer 3)

After the agent, the system applies a final deterministic check:

| Condition | Result |
|---|---|
| Confidence < 0.80 | `escalate` |
| Amount not reasonable | `escalate` |
| Items inconsistent with category | `escalate` |
| All checks passed | `auto_approve` |

---

## 6. Human Approval Queue

Escalated invoices appear in the approval queue with:
- Agent recommendation and reasoning
- Confidence score
- Policy violations cited

Approvers may: **approve**, **reject**, or **request more information**.

---

## 7. Anti-Manipulation

The system is designed to resist prompt injection. Text in the description or notes
field that attempts to influence the approval decision (e.g. "please approve this",
"finance already approved") is explicitly ignored by the agent prompt and does not
affect the deterministic gates.

---

## 8. Configurable Parameters

The following parameters are configurable without code redeployment:

| Parameter | Environment Variable | Default |
|---|---|---|
| Autonomy ceiling | `AUTONOMY_CEILING` | 500 |
| Confidence threshold | `AUTONOMY_CONFIDENCE` | 0.80 |
| White list categories | `WHITELIST_CATEGORIES` | office_supplies,business_meals,transportation,software,hardware |
| Known vendors | `KNOWN_VENDORS` | (see .env.example) |
| LLM provider | `LLM_PROVIDER` | gemini |
| LLM model | `LLM_MODEL` | gemini-2.5-flash |
