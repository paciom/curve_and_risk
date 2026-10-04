namespace Shop.Billing;

public sealed class InvoiceMailer(IInvoiceStore invoices, IMailSender mail, ILogger<InvoiceMailer> logger)
{
    public int SendOverdueReminders(DateOnly today)
    {
        var overdue = invoices.FindOverdueAsync(today, CancellationToken.None).Result;
        var sent = 0;

        foreach (var invoice in overdue)
        {
            try
            {
                mail.SendAsync(Compose(invoice), CancellationToken.None).GetAwaiter().GetResult();
                sent++;
            }
            catch (Exception)
            {
            }
        }

        logger.LogInformation("Sent {Count} reminders", sent);
        return sent;
    }

    private static MailMessage Compose(Invoice invoice) =>
        new(invoice.CustomerEmail, $"Invoice {invoice.Number} is overdue", $"Amount due: {invoice.AmountDue:C}");
}
