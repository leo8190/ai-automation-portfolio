using QuoteInvoice.Core;

public class PipelineTests
{
    static (Pipeline p, string tmp) NewPipeline()
    {
        var tmp = Directory.CreateTempSubdirectory().FullName;
        var data = Path.Combine(tmp, "data");
        Directory.CreateDirectory(data);
        foreach (var file in Directory.GetFiles(Repo.Data))
            File.Copy(file, Path.Combine(data, Path.GetFileName(file)));
        var outDir = Path.Combine(tmp, "out");
        var p = new Pipeline(new PipelineOptions(data, outDir), new FileOutboxEmailSender(Path.Combine(outDir, "outbox"), "quotes@harborline-demo.example"));
        return (p, tmp);
    }

    [Fact]
    public async Task Approval_is_required_before_mail_hits_the_outbox()
    {
        var (p, tmp) = NewPipeline();
        p.RunIntake();
        Assert.NotEmpty(p.Pending());
        Assert.False(Directory.Exists(Path.Combine(tmp, "out", "outbox")));
        var sent = await p.ApproveAsync();
        Assert.True(sent.Count >= 2); // needs-info + unknown-service at least
        Assert.Empty(p.Pending());
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(tmp, "out", "outbox"), "*.eml"));
        Directory.Delete(tmp, true);
    }

    [Fact]
    public void Issuing_the_same_request_twice_is_idempotent()
    {
        var (p, tmp) = NewPipeline();
        p.RunIntake();
        var a = p.IssueQuote("REQ-001", new DateOnly(2026, 11, 3));
        var b = p.IssueQuote("REQ-001", new DateOnly(2026, 11, 3));
        Assert.Equal(a.Quote.Number, b.Quote.Number);
        Assert.Single(p.Load().Quotes.Where(q => q.Quote.RequestId == "REQ-001"));
        Directory.Delete(tmp, true);
    }

    [Fact]
    public async Task End_to_end_quotes_invoices_one_follow_up_and_stop_after_payment()
    {
        var (p, tmp) = NewPipeline();
        p.RunIntake();
        var quotes = p.IssueReadyQuotes(new DateOnly(2026, 11, 3));
        Assert.Equal(3, quotes.Count);
        Assert.All(quotes, q => Assert.True(new FileInfo(q.PdfPath).Length > 1000));
        Assert.StartsWith("%PDF", File.ReadAllText(quotes[0].PdfPath)[..4]);

        await p.ApproveAsync();
        Assert.All(p.Load().Quotes, q => Assert.Equal(QuoteStatus.Sent, q.Status));

        var inv = p.Accept("Q-2026-003", new DateOnly(2026, 11, 5));
        Assert.Equal("INV-2026-001", inv.Invoice.Number);
        Assert.Equal(new DateOnly(2026, 11, 20), inv.Invoice.DueDate);
        Assert.Equal(281.45m, inv.Invoice.Total);
        Assert.True(new FileInfo(inv.PdfPath).Length > 1000);
        await p.ApproveAsync();

        var first = p.RunQuoteFollowUps(new DateOnly(2026, 11, 8));
        Assert.Equal(2, first.Count);
        Assert.DoesNotContain(first, r => r.Quote.Number == "Q-2026-003");
        await p.ApproveAsync();
        var second = p.RunQuoteFollowUps(new DateOnly(2026, 11, 12));
        Assert.Empty(second);

        var upcoming = p.RunInvoiceReminders(new DateOnly(2026, 11, 17));
        Assert.Single(upcoming);
        Assert.Equal(InvoiceReminderStage.Upcoming, upcoming[0].stage);
        await p.ApproveAsync();

        var afterPay = p.RunInvoiceReminders(new DateOnly(2026, 11, 20));
        Assert.Empty(afterPay);
        var rec = p.Load().Invoices.Single();
        Assert.True(rec.Paid);
        Assert.Equal(InvoiceReminderStage.Upcoming, rec.LastReminderSent);

        var eml = Directory.GetFiles(Path.Combine(tmp, "out", "outbox"), "*.eml");
        Assert.True(eml.Length >= 7, $"expected quote mails, follow-ups, invoice and a reminder, got {eml.Length}");
        Directory.Delete(tmp, true);
    }

    [Fact]
    public void Needs_info_and_unknown_drafts_do_not_invent_prices()
    {
        var (p, tmp) = NewPipeline();
        var state = p.RunIntake();
        var needs = state.Outbox.Single(m => m.Kind == OutboundKind.NeedsInfo);
        Assert.Contains("won't guess a number", needs.Body);
        Assert.Equal("jordan.blake@example.com", needs.To);
        var unknown = state.Outbox.Single(m => m.Kind == OutboundKind.UnknownService);
        Assert.Contains("haven't issued a quote or made up a price", unknown.Body);
        Assert.DoesNotMatch(@"\$\d", unknown.Body);
        Assert.DoesNotMatch(@"\$\d", unknown.Subject);
        Directory.Delete(tmp, true);
    }
}

internal static class Repo
{
    public static string Root
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "sample-data"))) dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("sample-data not found");
        }
    }

    public static string Data => Path.Combine(Root, "sample-data");

    public static IncomingRequest Request(string id) =>
        System.Text.Json.JsonSerializer.Deserialize<List<IncomingRequest>>(
            File.ReadAllText(Path.Combine(Data, "requests.json")),
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!
        .Single(r => r.Id == id);

    public static Company Company() =>
        System.Text.Json.JsonSerializer.Deserialize<Company>(
            File.ReadAllText(Path.Combine(Data, "company.json")),
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
}
