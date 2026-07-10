using PaymentService;

namespace ApprovalFlow.Tests;

internal class FakePaymentStateStore : IPaymentStateStore
{
    private readonly Dictionary<string, PaymentReservation> _store = [];

    public Task<PaymentReservation?> GetAsync(string invoiceId) =>
        Task.FromResult(_store.TryGetValue(invoiceId, out var reservation) ? reservation : null);

    public Task SaveAsync(string invoiceId, PaymentReservation reservation)
    {
        _store[invoiceId] = reservation;
        return Task.CompletedTask;
    }
}
