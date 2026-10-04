namespace Shop.Payments;

public sealed class RefundService(IPaymentStore payments, IPaymentGateway gateway)
{
    public async Task<RefundResult> RefundAsync(Guid paymentId, decimal amount, CancellationToken cancellationToken)
    {
        if (amount <= 0)
        {
            return RefundResult.Rejected("Amount must be positive.");
        }

        var payment = await payments.FindAsync(paymentId, cancellationToken);
        if (payment is null)
        {
            return RefundResult.Rejected("Unknown payment.");
        }

        if (payment.Status != PaymentStatus.Captured)
        {
            return RefundResult.Rejected("Only captured payments can be refunded.");
        }

        var reference = await gateway.RefundAsync(payment.GatewayId, amount, cancellationToken);
        payment.RefundedAmount += amount;
        await payments.SaveAsync(payment, cancellationToken);

        return RefundResult.Accepted(reference);
    }
}
