using Dapr.Client;

namespace PaymentService;

public class DaprPaymentStateStore(DaprClient daprClient) : IPaymentStateStore
{
    private const string StoreName = "statestore";

    public Task<PaymentReservation?> GetAsync(string invoiceId) =>
        daprClient.GetStateAsync<PaymentReservation?>(StoreName, invoiceId);

    public Task SaveAsync(string invoiceId, PaymentReservation reservation) =>
        daprClient.SaveStateAsync(StoreName, invoiceId, reservation);
}
