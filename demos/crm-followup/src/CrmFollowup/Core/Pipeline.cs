using System.Text.Json;

namespace CrmFollowup.Core;

public sealed class Pipeline
{
    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    readonly PipelineOptions _opts;
    readonly IFollowUpWriter _writer;
    readonly IEmailSender _sender;
    readonly Company _company;
    AppState _state;

    public Pipeline(PipelineOptions options, IFollowUpWriter? writer = null, IEmailSender? sender = null)
    {
        _opts = options;
        _writer = writer ?? new TemplateWriter();
        _sender = sender ?? new FileOutboxEmailSender(Path.Combine(options.OutDir, "outbox"));
        _company = JsonSerializer.Deserialize<Company>(
            File.ReadAllText(Path.Combine(options.DataDir, "company.json")), Json)
            ?? throw new InvalidOperationException("company.json is missing or invalid.");
        _state = Load();
    }

    public Company Company => _company;
    public IFollowUpWriter Writer => _writer;
    public AppState Load()
    {
        var path = StatePath;
        if (!File.Exists(path)) return new AppState();
        return JsonSerializer.Deserialize<AppState>(File.ReadAllText(path), Json) ?? new AppState();
    }

    public void Save()
    {
        Directory.CreateDirectory(_opts.OutDir);
        File.WriteAllText(StatePath, JsonSerializer.Serialize(_state, Json));
    }

    string StatePath => Path.Combine(_opts.OutDir, "state.json");

    public void Reset()
    {
        if (Directory.Exists(_opts.OutDir)) Directory.Delete(_opts.OutDir, true);
        Directory.CreateDirectory(_opts.OutDir);
        _state = new AppState();
        Save();
    }

    public AppState Seed()
    {
        if (_state.Leads.Count > 0) return _state;
        var seeds = JsonSerializer.Deserialize<List<SeedLead>>(
            File.ReadAllText(Path.Combine(_opts.DataDir, "leads.json")), Json)
            ?? throw new InvalidOperationException("leads.json is missing or invalid.");

        foreach (var seed in seeds)
        {
            var lead = new Lead
            {
                Id = seed.Id,
                Name = seed.Name,
                CompanyName = seed.Company,
                Email = seed.Email,
                Stage = seed.Stage,
                ValueUsd = seed.ValueUsd,
                Source = seed.Source,
                Notes = seed.Notes,
                OpenedOn = seed.OpenedOn,
            };
            _state.Leads.Add(lead);
            Schedule(lead, lead.Stage, lead.OpenedOn);
        }
        Save();
        return _state;
    }

    public Lead AddLead(SeedLead seed)
    {
        if (_state.Leads.Any(l => l.Id == seed.Id))
            throw new InvalidOperationException($"Lead {seed.Id} already exists.");
        var lead = new Lead
        {
            Id = seed.Id,
            Name = seed.Name,
            CompanyName = seed.Company,
            Email = seed.Email,
            Stage = seed.Stage,
            ValueUsd = seed.ValueUsd,
            Source = seed.Source,
            Notes = seed.Notes,
            OpenedOn = seed.OpenedOn,
        };
        _state.Leads.Add(lead);
        Schedule(lead, lead.Stage, lead.OpenedOn);
        Save();
        return lead;
    }

    void Schedule(Lead lead, string stage, DateOnly from)
    {
        foreach (var step in Sequences.For(stage))
        {
            _state.TaskSeq++;
            _state.Tasks.Add(new FollowUpTask
            {
                Id = $"T-{_state.TaskSeq:000}",
                LeadId = lead.Id,
                StepKey = step.Key,
                StepLabel = step.Label,
                DueOn = from.AddDays(step.DayOffset),
                Status = FollowUpStatus.Pending,
            });
        }
    }

    public Task<List<FollowUpTask>> ProcessDue(DateOnly asOf, CancellationToken ct = default)
        => ProcessDueAsync(asOf, ct);

    public async Task<List<FollowUpTask>> ProcessDueAsync(DateOnly asOf, CancellationToken ct = default)
    {
        var drafted = new List<FollowUpTask>();
        foreach (var task in _state.Tasks.Where(t => t.Status == FollowUpStatus.Pending && t.DueOn <= asOf).ToList())
        {
            var lead = RequireLead(task.LeadId);
            if (!Stages.IsActive(lead.Stage) || lead.Replied)
            {
                task.Status = FollowUpStatus.Cancelled;
                continue;
            }

            var step = Sequences.For(lead.Stage).FirstOrDefault(s => s.Key == task.StepKey)
                ?? new SequenceStep(task.StepKey, task.StepLabel, 0, "follow up");
            var draft = await _writer.DraftAsync(lead, step, ct);
            var subject = TemplateWriter.Subject(lead, step);

            _state.MailSeq++;
            var outbound = new OutboundMessage
            {
                Id = $"M-{_state.MailSeq:000}",
                LeadId = lead.Id,
                TaskId = task.Id,
                To = lead.Email,
                Subject = subject,
                Body = draft,
                Status = "PendingApproval",
            };
            _state.Outbox.Add(outbound);

            task.Status = FollowUpStatus.Ready;
            task.Draft = draft;
            task.Subject = subject;
            task.DraftedBy = _writer.Name;
            task.OutboundId = outbound.Id;
            drafted.Add(task);
        }
        Save();
        return drafted;
    }

    public List<OutboundMessage> Pending() =>
        _state.Outbox.Where(m => m.Status == "PendingApproval").ToList();

    public async Task<List<OutboundMessage>> ApproveAsync(IEnumerable<string>? ids = null, CancellationToken ct = default)
    {
        var wanted = ids?.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sent = new List<OutboundMessage>();
        foreach (var mail in Pending())
        {
            if (wanted is { Count: > 0 } && !wanted.Contains(mail.Id) && !wanted.Contains(mail.TaskId))
                continue;
            mail.EmlPath = await _sender.SendAsync(mail, _company.FromEmail, ct);
            mail.Status = "Sent";
            var task = _state.Tasks.Single(t => t.Id == mail.TaskId);
            task.Status = FollowUpStatus.Sent;
            task.SentOn = DateOnly.FromDateTime(DateTime.UtcNow);
            sent.Add(mail);
        }
        Save();
        return sent;
    }

    public FollowUpTask Skip(string taskId)
    {
        var task = _state.Tasks.SingleOrDefault(t => t.Id == taskId)
            ?? throw new KeyNotFoundException($"Unknown task {taskId}.");
        if (task.Status is not (FollowUpStatus.Pending or FollowUpStatus.Ready))
            throw new InvalidOperationException($"Task {taskId} is {task.Status} and cannot be skipped.");
        task.Status = FollowUpStatus.Skipped;
        if (task.OutboundId is { } oid)
        {
            var mail = _state.Outbox.Single(m => m.Id == oid);
            mail.Status = "Skipped";
        }
        Save();
        return task;
    }

    public FollowUpTask ApproveWithEdit(string taskId, string editedBody)
    {
        var task = _state.Tasks.SingleOrDefault(t => t.Id == taskId)
            ?? throw new KeyNotFoundException($"Unknown task {taskId}.");
        if (task.Status != FollowUpStatus.Ready || task.OutboundId is null)
            throw new InvalidOperationException($"Task {taskId} is {task.Status} and is not waiting for approval.");
        var mail = _state.Outbox.Single(m => m.Id == task.OutboundId);
        mail.Body = editedBody;
        task.Draft = editedBody;
        Save();
        return task;
    }

    public Lead MarkReplied(string leadId, DateOnly asOf)
    {
        var lead = RequireLead(leadId);
        lead.Replied = true;
        lead.RepliedOn = asOf;
        CancelOpen(lead.Id);
        Save();
        return lead;
    }

    public Lead MoveStage(string leadId, string stage, DateOnly asOf)
    {
        if (!Stages.All.Contains(stage)) throw new ArgumentException($"Unknown stage {stage}.", nameof(stage));
        var lead = RequireLead(leadId);
        if (lead.Stage == stage) return lead;
        CancelOpen(lead.Id);
        lead.Stage = stage;
        Schedule(lead, stage, asOf);
        Save();
        return lead;
    }

    void CancelOpen(string leadId)
    {
        foreach (var task in _state.Tasks.Where(t => t.LeadId == leadId && t.Status is FollowUpStatus.Pending or FollowUpStatus.Ready))
        {
            task.Status = FollowUpStatus.Cancelled;
            if (task.OutboundId is { } oid)
            {
                var mail = _state.Outbox.Single(m => m.Id == oid);
                if (mail.Status == "PendingApproval") mail.Status = "Cancelled";
            }
        }
    }

    public Lead RequireLead(string leadId) =>
        _state.Leads.SingleOrDefault(l => l.Id == leadId) ?? throw new KeyNotFoundException($"Unknown lead {leadId}.");

    public AppState State => _state;
}
