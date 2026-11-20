using System.Text.Json.Serialization;

namespace CrmFollowup.Core;

public static class Stages
{
    public const string New = "New", Contacted = "Contacted", Proposal = "Proposal", Won = "Won", Lost = "Lost";
    public static readonly string[] All = [New, Contacted, Proposal, Won, Lost];
    public static bool IsActive(string stage) => stage is New or Contacted or Proposal;
}

public static class FollowUpStatus
{
    public const string Pending = "Pending", Ready = "Ready", Sent = "Sent", Cancelled = "Cancelled", Skipped = "Skipped";
}

public sealed record Company(string Name, string FromEmail, string Signer);

public sealed record SequenceStep(string Key, string Label, int DayOffset, string Goal);

public sealed class SeedLead
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Company { get; set; } = "";
    public string Email { get; set; } = "";
    public string Stage { get; set; } = Stages.New;
    public decimal ValueUsd { get; set; }
    public string Source { get; set; } = "";
    public string Notes { get; set; } = "";
    public DateOnly OpenedOn { get; set; }
}

public sealed class Lead
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string CompanyName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Stage { get; set; } = Stages.New;
    public decimal ValueUsd { get; set; }
    public string Source { get; set; } = "";
    public string Notes { get; set; } = "";
    public DateOnly OpenedOn { get; set; }
    public bool Replied { get; set; }
    public DateOnly? RepliedOn { get; set; }

    [JsonIgnore]
    public string FirstName => Name.Split(' ', StringSplitOptions.RemoveEmptyEntries) is { Length: > 0 } parts ? parts[0] : Name;
}

public sealed class FollowUpTask
{
    public string Id { get; set; } = "";
    public string LeadId { get; set; } = "";
    public string StepKey { get; set; } = "";
    public string StepLabel { get; set; } = "";
    public DateOnly DueOn { get; set; }
    public string Status { get; set; } = FollowUpStatus.Pending;
    public string? Draft { get; set; }
    public string? Subject { get; set; }
    public string? DraftedBy { get; set; }
    public DateOnly? SentOn { get; set; }
    public string? OutboundId { get; set; }
}

public sealed class OutboundMessage
{
    public string Id { get; set; } = "";
    public string LeadId { get; set; } = "";
    public string TaskId { get; set; } = "";
    public string To { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public string Status { get; set; } = "PendingApproval";
    public string? EmlPath { get; set; }
}

public sealed class AppState
{
    public List<Lead> Leads { get; set; } = [];
    public List<FollowUpTask> Tasks { get; set; } = [];
    public List<OutboundMessage> Outbox { get; set; } = [];
    public int TaskSeq { get; set; }
    public int MailSeq { get; set; }
}

public sealed record PipelineOptions(string DataDir, string OutDir);
