using ApprovalFlow.Contracts;
using Dapr.Client;

namespace DecisionService;

public class DaprInvoiceStateStore(DaprClient daprClient) : IInvoiceStateStore
{
    private const string StoreName = "statestore";
    private const string PendingQueueKey = "pending-queue";
    private const string AllInvoicesKey = "all-invoices-queue";
    private const int MaxConcurrencyRetries = 5;

    public Task<InvoiceState?> GetAsync(string key) =>
        daprClient.GetStateAsync<InvoiceState?>(StoreName, key);

    public Task SaveAsync(string key, InvoiceState state) =>
        daprClient.SaveStateAsync(StoreName, key, state);

    public Task<List<string>> GetPendingIdsAsync() => GetIndexAsync(PendingQueueKey);

    public Task AddPendingIdAsync(string invoiceId) =>
        UpdateIndexAsync(PendingQueueKey, ids =>
        {
            if (!ids.Contains(invoiceId))
                ids.Add(invoiceId);
        });

    public Task RemovePendingIdAsync(string invoiceId) =>
        UpdateIndexAsync(PendingQueueKey, ids => ids.Remove(invoiceId));

    public Task<List<string>> GetAllInvoiceIdsAsync() => GetIndexAsync(AllInvoicesKey);

    public Task AddInvoiceIdAsync(string invoiceId) =>
        UpdateIndexAsync(AllInvoicesKey, ids =>
        {
            if (!ids.Contains(invoiceId))
                ids.Add(invoiceId);
        });

    private async Task<List<string>> GetIndexAsync(string indexKey)
    {
        var (ids, _) = await daprClient.GetStateAndETagAsync<List<string>?>(StoreName, indexKey);
        return ids ?? [];
    }

    // Multiple invoices can escalate/resolve (or be submitted) concurrently, so each index is
    // updated with optimistic concurrency (ETag) and a bounded retry loop rather than a plain
    // read-modify-write.
    private async Task UpdateIndexAsync(string indexKey, Action<List<string>> mutate)
    {
        for (var attempt = 0; attempt < MaxConcurrencyRetries; attempt++)
        {
            var (ids, etag) = await daprClient.GetStateAndETagAsync<List<string>?>(StoreName, indexKey);
            var list = ids ?? [];
            mutate(list);

            if (await daprClient.TrySaveStateAsync(StoreName, indexKey, list, etag))
                return;
        }

        throw new InvalidOperationException(
            $"Failed to update {indexKey} index after {MaxConcurrencyRetries} attempts due to concurrent writes.");
    }
}
