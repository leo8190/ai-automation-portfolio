namespace EmailAgent.Core;

/// <summary>Deterministic task extraction from a classified email. No model call.</summary>
public static class TaskExtractor
{
    public static IReadOnlyList<ExtractedTask> Extract(IncomingEmail email, Triage triage)
    {
        var ent = triage.Entities;
        var company = string.IsNullOrWhiteSpace(ent.Company) ? email.SenderName : ent.Company!;
        return triage.Category switch
        {
            "sales_lead" =>
            [
                new ExtractedTask(
                    $"Prepare a quote and timeline for {company}{Budget(ent)}",
                    null,
                    "quote"),
            ],
            "support" =>
            [
                new ExtractedTask(
                    $"Investigate: {email.Subject}",
                    triage.Priority == "high" ? "today" : null,
                    "support"),
            ],
            "complaint" =>
            [
                new ExtractedTask(
                    $"Investigate incident and send a written summary: {email.Subject}",
                    "today",
                    "incident"),
            ],
            "billing" =>
            [
                new ExtractedTask(
                    $"Send W-9 and confirm ACH for {Invoices(ent)}{Budget(ent)}",
                    null,
                    "billing"),
            ],
            "scheduling" =>
            [
                new ExtractedTask(
                    $"Schedule a call with {email.SenderName}{When(ent)}",
                    ent.Days is { Count: > 0 } ? string.Join(" or ", ent.Days) : null,
                    "meeting"),
            ],
            "partnership" =>
            [
                new ExtractedTask(
                    $"Review referral partnership with {company}",
                    null,
                    "partnership"),
            ],
            _ => [],
        };
    }

    static string Budget(ExtractedEntities ent) =>
        ent.Amounts is { Count: > 0 } ? $" ({ent.Amounts[0]})" : "";

    static string Invoices(ExtractedEntities ent) =>
        ent.Invoices is { Count: > 0 } ? string.Join(", ", ent.Invoices) : "the invoice";

    static string When(ExtractedEntities ent) =>
        ent.Days is { Count: > 0 } ? $" ({string.Join(" or ", ent.Days)})" : "";
}
