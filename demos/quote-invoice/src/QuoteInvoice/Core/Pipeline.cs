using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuoteInvoice.Core;

public record PipelineOptions(string DataDir, string OutDir);

/// <summary>
/// Request intake → quotes from the price sheet → one follow-up → accepted quotes
/// become invoices → payment reminders that stop when paid. Outbound mail is queued
/// for approval and is not written to the outbox until a person approves it.
/// </summary>
public class Pipeline
{
    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    readonly PipelineOptions _opt;
    readonly IEmailSender _email;
    readonly Company _company;
    readonly List<PriceItem> _sheet;

    public Pipeline(PipelineOptions opt, IEmailSender email, Company? company = null, IReadOnlyList<PriceItem>? sheet = null)
    {
        _opt = opt;
        _email = email;
        _company = company ?? JsonSerializer.Deserialize<Company>(
            File.ReadAllText(Path.Combine(opt.DataDir, "company.json")), Json)
            ?? throw new InvalidOperationException("company.json is missing or invalid.");
        _sheet = sheet?.ToList() ?? Csv.ReadPriceSheet(File.ReadAllText(Path.Combine(opt.DataDir, "price-sheet.csv")));
    }

    public Company Company => _company;
    public IReadOnlyList<PriceItem> PriceSheet => _sheet;
    string StatePath => Path.Combine(_opt.OutDir, "state.json");
    public string QuoteDir => Path.Combine(_opt.OutDir, "quotes");
    public string InvoiceDir => Path.Combine(_opt.OutDir, "invoices");

    public AppState Load() =>
        File.Exists(StatePath)
            ? JsonSerializer.Deserialize<AppState>(File.ReadAllText(StatePath), Json) ?? new()
            : new();

    void Save(AppState state)
    {
        Directory.CreateDirectory(_opt.OutDir);
        File.WriteAllText(StatePath, JsonSerializer.Serialize(state, Json));
    }

    public void Reset()
    {
        if (Directory.Exists(_opt.OutDir)) Directory.Delete(_opt.OutDir, true);
    }

    public List<IncomingRequest> LoadRequests()
    {
        var path = Path.Combine(_opt.DataDir, "requests.json");
        return JsonSerializer.Deserialize<List<IncomingRequest>>(File.ReadAllText(path), Json) ?? new();
    }

    public AppState RunIntake()
    {
        var state = Load();
        foreach (var req in LoadRequests())
        {
            if (state.Intakes.Any(i => i.RequestId == req.Id)) continue;
            var result = Intake.Parse(req);
            state.Intakes.Add(result);
            if (result.Status == IntakeStatus.NeedsInfo)
                Queue(state, OutboundKind.NeedsInfo, result.RequestId, Copy.NeedsInfo(result, _company));
            else if (result.Status == IntakeStatus.UnknownService)
                Queue(state, OutboundKind.UnknownService, result.RequestId, Copy.UnknownService(result, _company));
        }
        Save(state);
        return state;
    }

    public QuoteRecord IssueQuote(string requestId, DateOnly issueDate)
    {
        var state = Load();
        var existing = state.Quotes.FirstOrDefault(q => q.Quote.RequestId == requestId);
        if (existing is not null) return existing;

        var intake = state.Intakes.FirstOrDefault(i => i.RequestId == requestId)
            ?? throw new InvalidOperationException($"No intake for {requestId}. Run intake first.");
        var priced = Pricer.Price(intake, _sheet, _company.TaxRate);
        var quote = Documents.BuildQuote(intake, priced, _company, issueDate, state.Quotes.Count + 1);
        Directory.CreateDirectory(QuoteDir);
        var pdf = Path.Combine(QuoteDir, $"{quote.Number}.pdf");
        File.WriteAllBytes(pdf, Pdf.RenderQuote(quote, _company));
        var rec = new QuoteRecord { Quote = quote, PdfPath = pdf, Status = QuoteStatus.Draft };
        rec.History.Add($"{issueDate:yyyy-MM-dd} quote drafted");
        state.Quotes.Add(rec);
        Queue(state, OutboundKind.Quote, quote.Number, Copy.QuoteEmail(quote, _company, pdf));
        Save(state);
        return rec;
    }

    public List<QuoteRecord> IssueReadyQuotes(DateOnly issueDate)
    {
        var state = Load();
        var issued = new List<QuoteRecord>();
        foreach (var intake in state.Intakes.Where(i => i.Status == IntakeStatus.ReadyToQuote))
            issued.Add(IssueQuote(intake.RequestId, issueDate));
        return issued;
    }

    public List<OutboundMessage> Pending() => Load().Outbox.Where(m => m.Status == OutboundStatus.PendingApproval).ToList();

    public async Task<List<string>> ApproveAsync(IEnumerable<string>? ids = null, CancellationToken ct = default)
    {
        var state = Load();
        var sent = new List<string>();
        var pending = state.Outbox.Where(m => m.Status == OutboundStatus.PendingApproval
            && (ids is null || ids.Contains(m.Id, StringComparer.OrdinalIgnoreCase))).ToList();
        foreach (var msg in pending)
        {
            var path = await _email.SendAsync(new OutboundEmail(msg.To, msg.Subject, msg.Body, msg.AttachmentPath), ct);
            msg.Status = OutboundStatus.Sent;
            sent.Add(path);
            if (msg.Kind == OutboundKind.Quote && msg.RelatedNumber is { } qn)
            {
                var rec = state.Quotes.First(q => q.Quote.Number == qn);
                rec.Status = QuoteStatus.Sent;
                rec.History.Add($"quote emailed -> {Path.GetFileName(path)}");
            }
            if (msg.Kind == OutboundKind.Invoice && msg.RelatedNumber is { } invn)
            {
                var rec = state.Invoices.First(i => i.Invoice.Number == invn);
                rec.History.Add($"invoice emailed -> {Path.GetFileName(path)}");
            }
        }
        Save(state);
        return sent;
    }

    public List<QuoteRecord> RunQuoteFollowUps(DateOnly asOf)
    {
        var state = Load();
        var reminded = new List<QuoteRecord>();
        foreach (var rec in state.Quotes)
        {
            if (!QuoteFollowUp.ShouldRemind(rec, asOf, _company.QuoteFollowupAfterDays)) continue;
            Queue(state, OutboundKind.QuoteReminder, rec.Quote.Number, Copy.QuoteReminder(rec.Quote, _company, rec.PdfPath));
            rec.ReminderSent = true;
            rec.Status = QuoteStatus.Reminded;
            rec.History.Add($"{asOf:yyyy-MM-dd} follow-up queued (only one is ever sent)");
            reminded.Add(rec);
        }
        foreach (var rec in state.Quotes.Where(q => q.Status is QuoteStatus.Sent or QuoteStatus.Reminded && asOf > q.Quote.ValidUntil))
        {
            rec.Status = QuoteStatus.Expired;
            rec.History.Add($"{asOf:yyyy-MM-dd} expired");
        }
        Save(state);
        return reminded;
    }

    public InvoiceRecord Accept(string quoteNumber, DateOnly acceptDate)
    {
        var state = Load();
        var rec = state.Quotes.FirstOrDefault(q => q.Quote.Number.Equals(quoteNumber, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Unknown quote {quoteNumber}.");
        if (rec.Status == QuoteStatus.Accepted && state.Invoices.FirstOrDefault(i => i.Invoice.QuoteNumber == rec.Quote.Number) is { } existing)
            return existing;
        rec.Status = QuoteStatus.Accepted;
        rec.History.Add($"{acceptDate:yyyy-MM-dd} accepted");
        var inv = Documents.BuildInvoice(rec.Quote, _company, acceptDate, state.Invoices.Count + 1);
        Directory.CreateDirectory(InvoiceDir);
        var pdf = Path.Combine(InvoiceDir, $"{inv.Number}.pdf");
        File.WriteAllBytes(pdf, Pdf.RenderInvoice(inv, _company));
        var invRec = new InvoiceRecord { Invoice = inv, PdfPath = pdf };
        invRec.History.Add($"{acceptDate:yyyy-MM-dd} invoice drafted from {rec.Quote.Number}");
        state.Invoices.Add(invRec);
        Queue(state, OutboundKind.Invoice, inv.Number, Copy.InvoiceEmail(inv, _company, pdf));
        Save(state);
        return invRec;
    }

    public List<(InvoiceRecord rec, InvoiceReminderStage stage)> RunInvoiceReminders(DateOnly asOf)
    {
        var state = Load();
        var payPath = Path.Combine(_opt.DataDir, "payments.csv");
        var payments = File.Exists(payPath) ? Csv.ReadPayments(File.ReadAllText(payPath)) : new();
        var sent = new List<(InvoiceRecord, InvoiceReminderStage)>();
        foreach (var rec in state.Invoices)
        {
            var paidAmt = payments.Where(p => p.InvoiceNumber == rec.Invoice.Number && p.PaidOn <= asOf).Sum(p => p.AmountUsd);
            if (!rec.Paid && paidAmt >= rec.Invoice.Total)
            {
                rec.Paid = true;
                rec.History.Add($"{asOf:yyyy-MM-dd} marked PAID (${paidAmt:N2})");
            }
            if (InvoiceReminders.NextToSend(rec, asOf) is { } stage)
            {
                Queue(state, OutboundKind.InvoiceReminder, rec.Invoice.Number, Copy.InvoiceReminder(rec.Invoice, stage, asOf, _company));
                rec.LastReminderSent = stage;
                rec.History.Add($"{asOf:yyyy-MM-dd} reminder {stage} queued");
                sent.Add((rec, stage));
            }
        }
        Save(state);
        return sent;
    }

    void Queue(AppState state, OutboundKind kind, string related, OutboundEmail email)
    {
        var id = $"MSG-{state.Outbox.Count + 1:000}";
        state.Outbox.Add(new OutboundMessage
        {
            Id = id,
            Kind = kind,
            To = email.To,
            Subject = email.Subject,
            Body = email.Body,
            AttachmentPath = email.AttachmentPath,
            RelatedNumber = related
        });
    }
}
