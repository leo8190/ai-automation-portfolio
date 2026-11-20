namespace QuoteInvoice.Core;

public static class QuoteFollowUp
{
    /// <summary>Exactly one reminder, only while the quote is still valid and unsigned.</summary>
    public static bool ShouldRemind(QuoteRecord rec, DateOnly asOf, int followupAfterDays)
    {
        if (rec.Status is not (QuoteStatus.Sent or QuoteStatus.Reminded)) return false;
        if (rec.ReminderSent) return false;
        if (rec.Status == QuoteStatus.Accepted) return false;
        if (asOf > rec.Quote.ValidUntil) return false;
        return asOf >= rec.Quote.IssueDate.AddDays(followupAfterDays);
    }
}

public static class InvoiceReminders
{
    public static InvoiceReminderStage StageFor(DateOnly due, DateOnly asOf)
    {
        if (asOf < due.AddDays(-3)) return InvoiceReminderStage.None;
        if (asOf < due) return InvoiceReminderStage.Upcoming;
        if (asOf < due.AddDays(7)) return InvoiceReminderStage.DueToday;
        return InvoiceReminderStage.Overdue;
    }

    public static InvoiceReminderStage? NextToSend(InvoiceRecord rec, DateOnly asOf)
    {
        if (rec.Paid) return null;
        var stage = StageFor(rec.Invoice.DueDate, asOf);
        if (stage == InvoiceReminderStage.None) return null;
        return stage > rec.LastReminderSent ? stage : null;
    }
}
