namespace PaymentService;

public class PaymentReservation
{
    public required string InvoiceId { get; set; }
    public required string Status { get; set; } // reserved | paid | payment-failed
    public decimal Amount { get; set; }
    public DateTime ReservedAt { get; set; }
    public DateTime? PaidAt { get; set; }
}
