using ApprovalFlow.Contracts;

namespace DecisionService;

public interface IInvoiceStateStore
{
    Task<InvoiceState?> GetAsync(string key);
    Task SaveAsync(string key, InvoiceState state);
}
