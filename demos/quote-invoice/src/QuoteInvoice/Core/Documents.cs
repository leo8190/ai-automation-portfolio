namespace QuoteInvoice.Core;

public static class Documents
{
    public static Quote BuildQuote(IntakeResult intake, PricedQuoteDraft priced, Company company, DateOnly issueDate, int sequence)
    {
        if (priced.Unpriced.Count > 0)
            throw new InvalidOperationException("Refusing to issue a quote with unpriced lines: " + string.Join(", ", priced.Unpriced));
        if (intake.Status != IntakeStatus.ReadyToQuote)
            throw new InvalidOperationException($"Intake {intake.RequestId} is {intake.Status}, not ready to quote.");
        if (string.IsNullOrWhiteSpace(intake.Address))
            throw new InvalidOperationException("A site address is required before a quote is issued.");

        return new Quote(
            Number: $"Q-{issueDate.Year}-{sequence:000}",
            RequestId: intake.RequestId,
            ClientName: intake.ContactName,
            ClientEmail: intake.Email,
            Phone: intake.Phone,
            Address: intake.Address!,
            IssueDate: issueDate,
            ValidUntil: issueDate.AddDays(company.QuoteValidDays),
            Lines: priced.Lines,
            Subtotal: priced.Subtotal,
            TaxRate: company.TaxRate,
            Tax: priced.Tax,
            Total: priced.Total);
    }

    public static Invoice BuildInvoice(Quote quote, Company company, DateOnly issueDate, int sequence) =>
        new(
            Number: $"INV-{issueDate.Year}-{sequence:000}",
            QuoteNumber: quote.Number,
            ClientName: quote.ClientName,
            ClientEmail: quote.ClientEmail,
            Address: quote.Address,
            IssueDate: issueDate,
            DueDate: issueDate.AddDays(company.InvoiceTermsDays),
            Lines: quote.Lines,
            Subtotal: quote.Subtotal,
            TaxRate: quote.TaxRate,
            Tax: quote.Tax,
            Total: quote.Total);
}
