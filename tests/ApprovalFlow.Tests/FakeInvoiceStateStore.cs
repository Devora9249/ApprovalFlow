using ApprovalFlow.Contracts;
using DecisionService;

namespace ApprovalFlow.Tests;

internal class FakeInvoiceStateStore : IInvoiceStateStore
{
    private readonly Dictionary<string, InvoiceState> _store = [];

    public Task<InvoiceState?> GetAsync(string key) =>
        Task.FromResult(_store.TryGetValue(key, out var state) ? state : null);

    public Task SaveAsync(string key, InvoiceState state)
    {
        _store[key] = state;
        return Task.CompletedTask;
    }
}
