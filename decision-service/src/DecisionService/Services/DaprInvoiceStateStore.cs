using ApprovalFlow.Contracts;
using Dapr.Client;

namespace DecisionService;

public class DaprInvoiceStateStore(DaprClient daprClient) : IInvoiceStateStore
{
    private const string StoreName = "statestore";
    private const string PendingQueueKey = "pending-queue";
    private const int MaxConcurrencyRetries = 5;

    public Task<InvoiceState?> GetAsync(string key) =>
        daprClient.GetStateAsync<InvoiceState?>(StoreName, key);

    public Task SaveAsync(string key, InvoiceState state) =>
        daprClient.SaveStateAsync(StoreName, key, state);

    public async Task<List<string>> GetPendingIdsAsync()
    {
        var (ids, _) = await daprClient.GetStateAndETagAsync<List<string>?>(StoreName, PendingQueueKey);
        return ids ?? [];
    }

    public Task AddPendingIdAsync(string invoiceId) =>
        UpdatePendingQueueAsync(ids =>
        {
            if (!ids.Contains(invoiceId))
                ids.Add(invoiceId);
        });

    public Task RemovePendingIdAsync(string invoiceId) =>
        UpdatePendingQueueAsync(ids => ids.Remove(invoiceId));

    // Multiple invoices can escalate/resolve concurrently, so the index is updated with
    // optimistic concurrency (ETag) and a bounded retry loop rather than a plain read-modify-write.
    private async Task UpdatePendingQueueAsync(Action<List<string>> mutate)
    {
        for (var attempt = 0; attempt < MaxConcurrencyRetries; attempt++)
        {
            var (ids, etag) = await daprClient.GetStateAndETagAsync<List<string>?>(StoreName, PendingQueueKey);
            var list = ids ?? [];
            mutate(list);

            if (await daprClient.TrySaveStateAsync(StoreName, PendingQueueKey, list, etag))
                return;
        }

        throw new InvalidOperationException(
            $"Failed to update pending-queue index after {MaxConcurrencyRetries} attempts due to concurrent writes.");
    }
}
