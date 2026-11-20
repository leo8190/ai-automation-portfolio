using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace CrmFollowup.Core;

public interface IFollowUpWriter
{
    string Name { get; }
    Task<string> DraftAsync(Lead lead, SequenceStep step, CancellationToken ct = default);
}

/// <summary>Offline, deterministic writer. Used by the demo and by tests; no API key.</summary>
public sealed class TemplateWriter : IFollowUpWriter
{
    public string Name => "mock-llm";

    public Task<string> DraftAsync(Lead lead, SequenceStep step, CancellationToken ct = default)
        => Task.FromResult(Render(lead, step));

    public static string Render(Lead lead, SequenceStep step)
    {
        var first = lead.FirstName;
        var need = string.IsNullOrWhiteSpace(lead.Notes) ? "your project" : lead.Notes.TrimEnd('.');
        var body = step.Key switch
        {
            "new-intro" =>
                $"Thanks for reaching out about {need}. I build automations like this for small teams so they stop losing hours to manual work. Would a quick 15-minute call this week work to map out the first version?",
            "new-bump" =>
                $"Just bubbling this up in case it got buried. Happy to share a rough plan for {lead.CompanyName} before we even talk, if that's easier.",
            "new-value" =>
                $"Quick example: a client with a similar setup cut their admin time by about 8 hours a week after we automated the same workflow. I think {lead.CompanyName} could see a similar win. Want me to sketch it out?",
            "new-breakup" =>
                $"I don't want to clutter your inbox, so I'll close this out for now. If {need} becomes a priority again, just reply and we'll pick up where we left off.",
            "contacted-checkin" =>
                $"Great talking with you. As a next step, I can send a short scope and timeline for {lead.CompanyName}. Does a 2-week phase 1 sound right?",
            "contacted-value" =>
                $"I put together a quick idea for {need}: start with the highest-volume task, automate it end to end, and measure hours saved in the first month. Want the one-page version?",
            "proposal-checkin" =>
                $"Did you get a chance to review the proposal for {lead.CompanyName}? Happy to walk through it on a quick call or tweak the scope.",
            "proposal-objections" =>
                $"If budget or timing is the blocker, we can start with a smaller phase 1 (about 40% of the full scope) and expand once you see results.",
            "proposal-lastcall" =>
                $"Quick heads-up: I can hold the quoted price and start date for {lead.CompanyName} until the end of next week. Should I pencil you in?",
            _ => $"Following up on {need}. Let me know how you'd like to proceed.",
        };
        return $"Hi {first},\n\n{body}\n\nBest,\nLeo";
    }

    public static string Subject(Lead lead, SequenceStep step) => step.Key switch
    {
        "new-intro" => $"Quick intro — {lead.CompanyName}",
        "new-bump" => $"Following up — {lead.CompanyName}",
        "new-value" => $"A relevant example for {lead.CompanyName}",
        "new-breakup" => $"Closing the file — {lead.CompanyName}",
        "contacted-checkin" => $"Next step for {lead.CompanyName}",
        "contacted-value" => $"A short idea for {lead.CompanyName}",
        "proposal-checkin" => $"Checking in on the {lead.CompanyName} proposal",
        "proposal-objections" => $"A smaller first phase for {lead.CompanyName}",
        "proposal-lastcall" => $"Holding the quoted price for {lead.CompanyName}",
        _ => $"Following up — {lead.CompanyName}",
    };
}

/// <summary>Optional real-provider adapter. Falls back to <see cref="TemplateWriter"/> on any error.</summary>
public sealed class OpenAiWriter : IFollowUpWriter
{
    readonly string _apiKey;
    readonly string _model;
    readonly IFollowUpWriter _fallback;
    readonly HttpClient _http;

    public string Name => $"openai:{_model}";

    /// <summary>Test hook: replace the HTTP call so fallback can be exercised without a real key.</summary>
    public Func<Lead, SequenceStep, CancellationToken, Task<string>>? CallOverride { get; set; }

    public OpenAiWriter(string apiKey, string? model = null, IFollowUpWriter? fallback = null, HttpClient? http = null)
    {
        _apiKey = apiKey;
        _model = model ?? Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-4o-mini";
        _fallback = fallback ?? new TemplateWriter();
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    public async Task<string> DraftAsync(Lead lead, SequenceStep step, CancellationToken ct = default)
    {
        try
        {
            if (CallOverride is not null) return (await CallOverride(lead, step, ct)).Trim();
            return (await CallAsync(lead, step, ct)).Trim();
        }
        catch
        {
            return await _fallback.DraftAsync(lead, step, ct);
        }
    }

    async Task<string> CallAsync(Lead lead, SequenceStep step, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        var payload = JsonSerializer.Serialize(new
        {
            model = _model,
            temperature = 0.4,
            messages = new object[]
            {
                new { role = "system", content = "You write short, warm, specific B2B follow-up emails for a freelance automation developer named Leo. Max 90 words. Plain text, start with 'Hi <first name>,' and sign 'Best,\\nLeo'. No subject line." },
                new { role = "user", content = $"Lead: {lead.Name} at {lead.CompanyName}. Pipeline stage: {lead.Stage}. Context: {lead.Notes}. Goal of this email ({step.Label}): {step.Goal}." },
            },
        });
        req.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()
            ?? throw new InvalidOperationException("empty model reply");
    }
}

public static class WriterFactory
{
    public static IFollowUpWriter Create(string? engine = null)
    {
        var name = (engine ?? Environment.GetEnvironmentVariable("LLM_ENGINE") ?? "mock").Trim().ToLowerInvariant();
        if (name is "openai" or "real")
        {
            var key = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            if (string.IsNullOrWhiteSpace(key))
                throw new InvalidOperationException("OPENAI_API_KEY is required for --engine openai.");
            return new OpenAiWriter(key);
        }
        return new TemplateWriter();
    }
}
