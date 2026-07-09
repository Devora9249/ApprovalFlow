using ApprovalFlow.Contracts;
using DecisionService;

namespace ApprovalFlow.Tests;

internal class FakeInvoiceStateStore : IInvoiceStateStore
{
    private readonly Dictionary<string, InvoiceState> _store = [];
    private readonly List<string> _pendingIds = [];

    public Task<InvoiceState?> GetAsync(string key) =>
        Task.FromResult(_store.TryGetValue(key, out var state) ? state : null);

    public Task SaveAsync(string key, InvoiceState state)
    {
        _store[key] = state;
        return Task.CompletedTask;
    }

    public Task<List<string>> GetPendingIdsAsync() => Task.FromResult(_pendingIds.ToList());

    public Task AddPendingIdAsync(string invoiceId)
    {
        if (!_pendingIds.Contains(invoiceId))
            _pendingIds.Add(invoiceId);
        return Task.CompletedTask;
    }

    public Task RemovePendingIdAsync(string invoiceId)
    {
        _pendingIds.Remove(invoiceId);
        return Task.CompletedTask;
    }
}
