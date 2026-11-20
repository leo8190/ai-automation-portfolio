namespace EmailAgent.Core;

public static class Categories
{
    public static readonly string[] All =
    [
        "sales_lead", "support", "complaint", "billing", "scheduling", "partnership", "spam"
    ];
}

public sealed record IncomingEmail(
    string Id,
    string SenderName,
    string SenderEmail,
    string To,
    string Subject,
    string Date,
    string Body)
{
    public string Domain
    {
        get
        {
            var at = SenderEmail.LastIndexOf('@');
            return at >= 0 ? SenderEmail[(at + 1)..].ToLowerInvariant() : "";
        }
    }
}

public sealed class ExtractedEntities
{
    public List<string>? Amounts { get; set; }
    public List<string>? Invoices { get; set; }
    public List<string>? Days { get; set; }
    public string? Company { get; set; }
}

public sealed record Triage(
    string Category,
    string Priority,
    string Sentiment,
    double Confidence,
    string Summary,
    ExtractedEntities Entities,
    string Engine);

public sealed record ExtractedTask(string Title, string? DueHint, string Kind);

public sealed record TriageResult(
    IncomingEmail Email,
    Triage Triage,
    IReadOnlyList<string> Labels,
    string? Draft,
    string? CrmAction,
    bool NeedsHuman,
    IReadOnlyList<ExtractedTask> Tasks);

public sealed class OutboundMessage
{
    public string Id { get; set; } = "";
    public string EmailId { get; set; } = "";
    public string To { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public string Status { get; set; } = "PendingApproval";
    public string? EmlPath { get; set; }
}

public sealed class MailboxState
{
    public List<OutboundMessage> Outbox { get; set; } = [];
    public int MailSeq { get; set; }
}

public sealed record PipelineOptions(string DataDir, string OutDir, string FromEmail);
