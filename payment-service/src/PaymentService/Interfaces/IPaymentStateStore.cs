namespace PaymentService;

public interface IPaymentStateStore
{
    Task<PaymentReservation?> GetAsync(string invoiceId);
    Task SaveAsync(string invoiceId, PaymentReservation reservation);
}
