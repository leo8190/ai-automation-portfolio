using QuoteInvoice.Core;

public class ReminderTests
{
    static QuoteRecord SentQuote(DateOnly issued) => new()
    {
        Quote = new Quote("Q-1", "REQ-1", "Maya", "m@x.com", null, "1 Main", issued, issued.AddDays(14),
            Array.Empty<QuoteLine>(), 100, 0, 0, 100),
        Status = QuoteStatus.Sent
    };

    [Fact]
    public void Quote_follow_up_is_sent_once_and_never_after_accept_or_expiry()
    {
        var issued = new DateOnly(2026, 11, 3);
        var rec = SentQuote(issued);
        Assert.False(QuoteFollowUp.ShouldRemind(rec, issued.AddDays(4), 5));
        Assert.True(QuoteFollowUp.ShouldRemind(rec, issued.AddDays(5), 5));
        rec.ReminderSent = true;
        rec.Status = QuoteStatus.Reminded;
        Assert.False(QuoteFollowUp.ShouldRemind(rec, issued.AddDays(10), 5));
        rec.ReminderSent = false;
        rec.Status = QuoteStatus.Accepted;
        Assert.False(QuoteFollowUp.ShouldRemind(rec, issued.AddDays(5), 5));
        rec.Status = QuoteStatus.Sent;
        Assert.False(QuoteFollowUp.ShouldRemind(rec, issued.AddDays(15), 5)); // expired
    }

    static readonly DateOnly Due = new(2026, 11, 20);

    [Theory]
    [InlineData(-10, InvoiceReminderStage.None)]
    [InlineData(-3, InvoiceReminderStage.Upcoming)]
    [InlineData(0, InvoiceReminderStage.DueToday)]
    [InlineData(6, InvoiceReminderStage.DueToday)]
    [InlineData(7, InvoiceReminderStage.Overdue)]
    public void Invoice_stage_follows_the_schedule(int offset, InvoiceReminderStage expected) =>
        Assert.Equal(expected, InvoiceReminders.StageFor(Due, Due.AddDays(offset)));

    [Fact]
    public void Invoice_reminders_are_sent_once_per_stage_and_never_when_paid()
    {
        var rec = new InvoiceRecord
        {
            Invoice = new Invoice("INV-1", "Q-1", "Elena", "e@x.com", "7 Marina Way", Due.AddDays(-15), Due,
                Array.Empty<QuoteLine>(), 260, 0.0825m, 21.45m, 281.45m)
        };
        Assert.Equal(InvoiceReminderStage.Upcoming, InvoiceReminders.NextToSend(rec, Due.AddDays(-3)));
        rec.LastReminderSent = InvoiceReminderStage.Upcoming;
        Assert.Null(InvoiceReminders.NextToSend(rec, Due.AddDays(-2)));
        Assert.Equal(InvoiceReminderStage.DueToday, InvoiceReminders.NextToSend(rec, Due));
        rec.Paid = true;
        Assert.Null(InvoiceReminders.NextToSend(rec, Due.AddDays(30)));
    }

    [Fact]
    public void Overdue_copy_mentions_days_late_and_amount()
    {
        var inv = new Invoice("INV-1", "Q-1", "Elena Vasquez", "e@x.com", "7 Marina Way", Due.AddDays(-15), Due,
            Array.Empty<QuoteLine>(), 260, 0.0825m, 21.45m, 281.45m);
        var mail = Copy.InvoiceReminder(inv, InvoiceReminderStage.Overdue, Due.AddDays(7), Repo.Company());
        Assert.Contains("7 days past due", mail.Subject);
        Assert.Contains("$281.45", mail.Body);
        Assert.Equal("e@x.com", mail.To);
    }
}
