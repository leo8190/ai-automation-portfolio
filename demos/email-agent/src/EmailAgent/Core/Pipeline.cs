using System.Text.Json;

namespace EmailAgent.Core;

/// <summary>Triage + human approval queue. Drafts are not written to the outbox until approved.</summary>
public sealed class MailboxPipeline
{
    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    readonly PipelineOptions _opts;
    readonly ILlm _llm;
    readonly IEmailSender _sender;
    MailboxState _state;

    public MailboxPipeline(PipelineOptions options, ILlm? llm = null, IEmailSender? sender = null)
    {
        _opts = options;
        _llm = llm ?? new MockLlm();
        _sender = sender ?? new FileOutboxEmailSender(Path.Combine(options.OutDir, "outbox"));
        _state = Load();
    }

    public ILlm Llm => _llm;
    public MailboxState State => _state;

    string StatePath => Path.Combine(_opts.OutDir, "state.json");

    public MailboxState Load()
    {
        if (!File.Exists(StatePath)) return new MailboxState();
        return JsonSerializer.Deserialize<MailboxState>(File.ReadAllText(StatePath), Json) ?? new MailboxState();
    }

    public void Save()
    {
        Directory.CreateDirectory(_opts.OutDir);
        File.WriteAllText(StatePath, JsonSerializer.Serialize(_state, Json));
    }

    public void Reset()
    {
        if (Directory.Exists(_opts.OutDir)) Directory.Delete(_opts.OutDir, true);
        Directory.CreateDirectory(_opts.OutDir);
        _state = new MailboxState();
        Save();
    }

    public async Task<IReadOnlyList<TriageResult>> TriageAsync(string? inbox = null, ICrmSink? crm = null, CancellationToken ct = default)
    {
        var emails = InboxLoader.Load(inbox ?? _opts.DataDir);
        var results = await TriageAgent.ProcessAsync(emails, _llm, crm, ct);
        await TriageAgent.WriteOutputsAsync(results, _opts.OutDir, ct);

        foreach (var r in results)
        {
            if (string.IsNullOrEmpty(r.Draft)) continue;
            if (_state.Outbox.Any(m => m.EmailId == r.Email.Id)) continue;
            _state.MailSeq++;
            _state.Outbox.Add(new OutboundMessage
            {
                Id = $"M-{_state.MailSeq:000}",
                EmailId = r.Email.Id,
                To = r.Email.SenderEmail,
                Subject = r.Email.Subject.StartsWith("Re:", StringComparison.OrdinalIgnoreCase)
                    ? r.Email.Subject
                    : $"Re: {r.Email.Subject}",
                Body = r.Draft,
                Status = "PendingApproval",
            });
        }
        Save();
        return results;
    }

    public List<OutboundMessage> Pending() =>
        _state.Outbox.Where(m => m.Status == "PendingApproval").ToList();

    public async Task<List<OutboundMessage>> ApproveAsync(IEnumerable<string>? ids = null, CancellationToken ct = default)
    {
        var wanted = ids?.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sent = new List<OutboundMessage>();
        foreach (var mail in Pending())
        {
            if (wanted is { Count: > 0 } && !wanted.Contains(mail.Id) && !wanted.Contains(mail.EmailId))
                continue;
            mail.EmlPath = await _sender.SendAsync(mail, _opts.FromEmail, ct);
            mail.Status = "Sent";
            sent.Add(mail);
        }
        Save();
        return sent;
    }
}
