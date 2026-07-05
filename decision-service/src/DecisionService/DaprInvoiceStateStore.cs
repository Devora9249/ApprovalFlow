using ApprovalFlow.Contracts;
using Dapr.Client;

namespace DecisionService;

public class DaprInvoiceStateStore(DaprClient daprClient) : IInvoiceStateStore
{
    private const string StoreName = "statestore";

    public Task<InvoiceState?> GetAsync(string key) =>
        daprClient.GetStateAsync<InvoiceState?>(StoreName, key);

    public Task SaveAsync(string key, InvoiceState state) =>
        daprClient.SaveStateAsync(StoreName, key, state);
}
