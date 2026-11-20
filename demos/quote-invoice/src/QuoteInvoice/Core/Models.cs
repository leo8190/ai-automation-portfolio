namespace QuoteInvoice.Core;

public record Company(
    string Name,
    string Email,
    string Phone,
    string Address,
    decimal TaxRate,
    int QuoteValidDays,
    int InvoiceTermsDays,
    int QuoteFollowupAfterDays);

public record PriceItem(string Sku, string Service, string Unit, decimal UnitPriceUsd, string Notes);

public record IncomingRequest(
    string Id,
    DateOnly ReceivedOn,
    string FromName,
    string FromEmail,
    string Subject,
    string Body);

public enum IntakeStatus { ReadyToQuote, NeedsInfo, UnknownService }

public record ExtractedLine(string Kind, string? Sku, decimal? Quantity, string Unit, string Raw, string? Flag = null);

public record IntakeResult(
    string RequestId,
    IntakeStatus Status,
    string ContactName,
    string Email,
    string? Phone,
    string? Address,
    IReadOnlyList<ExtractedLine> Lines,
    IReadOnlyList<string> MissingFields,
    IReadOnlyList<string> UnknownServices,
    string Summary);

public record QuoteLine(string Sku, string Description, decimal Quantity, string Unit, decimal UnitPrice, decimal Amount);

public record Quote(
    string Number,
    string RequestId,
    string ClientName,
    string ClientEmail,
    string? Phone,
    string Address,
    DateOnly IssueDate,
    DateOnly ValidUntil,
    IReadOnlyList<QuoteLine> Lines,
    decimal Subtotal,
    decimal TaxRate,
    decimal Tax,
    decimal Total);

public enum QuoteStatus { Draft, Sent, Reminded, Accepted, Declined, Expired }

public record Invoice(
    string Number,
    string QuoteNumber,
    string ClientName,
    string ClientEmail,
    string Address,
    DateOnly IssueDate,
    DateOnly DueDate,
    IReadOnlyList<QuoteLine> Lines,
    decimal Subtotal,
    decimal TaxRate,
    decimal Tax,
    decimal Total);

public enum InvoiceReminderStage { None = 0, Upcoming = 1, DueToday = 2, Overdue = 3 }

public enum OutboundKind { NeedsInfo, UnknownService, Quote, QuoteReminder, Invoice, InvoiceReminder }

public enum OutboundStatus { PendingApproval, Sent, Cancelled }

public class OutboundMessage
{
    public required string Id { get; init; }
    public required OutboundKind Kind { get; init; }
    public required string To { get; init; }
    public required string Subject { get; init; }
    public required string Body { get; init; }
    public string? AttachmentPath { get; set; }
    public OutboundStatus Status { get; set; } = OutboundStatus.PendingApproval;
    public string? RelatedNumber { get; init; }
}

public class QuoteRecord
{
    public required Quote Quote { get; init; }
    public QuoteStatus Status { get; set; } = QuoteStatus.Draft;
    public string PdfPath { get; set; } = "";
    public bool ReminderSent { get; set; }
    public List<string> History { get; set; } = new();
}

public class InvoiceRecord
{
    public required Invoice Invoice { get; init; }
    public string PdfPath { get; set; } = "";
    public bool Paid { get; set; }
    public InvoiceReminderStage LastReminderSent { get; set; } = InvoiceReminderStage.None;
    public List<string> History { get; set; } = new();
}

public record Payment(string InvoiceNumber, DateOnly PaidOn, decimal AmountUsd);

public class AppState
{
    public List<IntakeResult> Intakes { get; set; } = new();
    public List<QuoteRecord> Quotes { get; set; } = new();
    public List<InvoiceRecord> Invoices { get; set; } = new();
    public List<OutboundMessage> Outbox { get; set; } = new();
}

public record OutboundEmail(string To, string Subject, string Body, string? AttachmentPath = null);
