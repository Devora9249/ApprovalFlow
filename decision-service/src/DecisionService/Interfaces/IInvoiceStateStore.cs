using ApprovalFlow.Contracts;

namespace DecisionService;

public interface IInvoiceStateStore
{
    Task<InvoiceState?> GetAsync(string key);
    Task SaveAsync(string key, InvoiceState state);

    // Secondary index of invoiceIds currently in status "waiting_for_human" — the plain Redis
    // state store has no query API, so GET /invoices/pending needs an explicit index rather
    // than a scan.
    Task<List<string>> GetPendingIdsAsync();
    Task AddPendingIdAsync(string invoiceId);
    Task RemovePendingIdAsync(string invoiceId);

    // Secondary index of every invoiceId ever submitted (never removed, unlike the pending
    // index above) — backs GET /invoices, for the same reason: the plain Redis state store
    // has no query API to list all keys of a given shape.
    Task<List<string>> GetAllInvoiceIdsAsync();
    Task AddInvoiceIdAsync(string invoiceId);
}
