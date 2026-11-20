using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EmailAgent.Core;

public interface ILlm
{
    string Name { get; }
    Task<Triage> ClassifyAsync(IncomingEmail email, CancellationToken ct = default);
    Task<string?> DraftAsync(IncomingEmail email, Triage triage, CancellationToken ct = default);
}

public static class EntityExtractor
{
    static readonly Regex Amounts = new(@"\$\s?[\d,]+(?:\.\d{2})?(?:\s?-\s?\$?[\d,]+(?:\.\d{2})?)?", RegexOptions.Compiled);
    static readonly Regex Invoices = new(@"\bINV-[\d-]+\b", RegexOptions.Compiled);
    static readonly Regex Days = new(@"\b(Monday|Tuesday|Wednesday|Thursday|Friday)\b", RegexOptions.Compiled);
    static readonly Regex OwnedBy = new(@"\b(?i:we run|i own|owner of|we are)\s+((?:[A-Z][\w&']*\s?){1,4})", RegexOptions.Compiled);
    static readonly Regex Signature = new(@"(?:Owner|Manager|CEO|Founder|Director)[^\n]*?,?\s*([A-Z][\w&' ]+)$", RegexOptions.Compiled | RegexOptions.Multiline);

    public static ExtractedEntities Extract(IncomingEmail email)
    {
        var text = $"{email.Subject}\n{email.Body}";
        var ent = new ExtractedEntities();

        var amounts = Amounts.Matches(text).Select(m => m.Value).ToList();
        if (amounts.Count > 0) ent.Amounts = amounts;

        var invoices = Invoices.Matches(text).Select(m => m.Value).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
        if (invoices.Count > 0) ent.Invoices = invoices;

        var dayMatches = Days.Matches(text);
        if (dayMatches.Count > 0)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var days = new List<string>();
            foreach (Match m in dayMatches)
            {
                var day = m.Groups[1].Value;
                if (seen.Add(day)) days.Add(day);
            }
            ent.Days = days;
        }

        var own = OwnedBy.Match(email.Body);
        var sig = Signature.Match(email.Body);
        var domainGuess = email.Domain.Split('.')[0].Replace('-', ' ');
        if (domainGuess.Length > 0)
            domainGuess = char.ToUpperInvariant(domainGuess[0]) + domainGuess[1..].ToLowerInvariant();

        ent.Company = (own.Success ? own.Groups[1].Value.Trim() : null)
            ?? (sig.Success ? sig.Groups[1].Value.Trim() : null)
            ?? domainGuess;
        return ent;
    }
}

public sealed class MockLlm : ILlm
{
    public string Name => "mock-llm";

    static readonly Dictionary<string, string[]> Keywords = new()
    {
        ["sales_lead"] = ["quote", "cost", "budget", "build us", "looking for someone", "automate", "how long", "available next", "hire", "project", "proposal for", "pricing", "estimate"],
        ["support"] = ["not loading", "error", "bug", "broken", "blank screen", "can't", "cannot", "down", "500", "crash", "issue"],
        ["complaint"] = ["disappointed", "frustrat", "unprofessional", "unacceptable", "complain", "pausing", "refund", "angry", "terrible"],
        ["billing"] = ["invoice", "payment", "w-9", "ach", "receipt", "paid", "charge", "billing"],
        ["scheduling"] = ["meet", "call", "available", "thursday", "friday", "monday", "tuesday", "wednesday", "calendar", "reschedule"],
        ["partnership"] = ["partnership", "partner", "referral", "collaborat", "agency", "white label"],
        ["spam"] = ["guarantee", "click here", "limited time", "act now", "unsubscribe", "90% discount", "#1 on google", "!!!", "dear website owner"],
    };

    static readonly string[] Urgent = ["urgent", "asap", "immediately", "waiting", "down", "pausing", "today"];
    static readonly string[] Negative = ["disappointed", "frustrat", "unprofessional", "angry", "complain", "unacceptable", "chaos"];
    static readonly string[] Positive = ["thanks", "thank you", "happy", "great", "love"];

    public Task<Triage> ClassifyAsync(IncomingEmail email, CancellationToken ct = default)
        => Task.FromResult(Classify(email));

    public Task<string?> DraftAsync(IncomingEmail email, Triage triage, CancellationToken ct = default)
        => Task.FromResult(Draft(email, triage));

    public Triage Classify(IncomingEmail email)
    {
        var text = $"{email.Subject}\n{email.Body}".ToLowerInvariant();
        var scores = Keywords.ToDictionary(kv => kv.Key, kv => Count(text, kv.Value));
        if (scores["complaint"] >= 2) scores["complaint"] += 3;
        if (scores["spam"] >= 2) scores["spam"] += 5;

        var best = scores.MaxBy(kv => kv.Value).Key;
        var total = Math.Max(1, scores.Values.Sum());
        var confidence = scores[best] > 0 ? Math.Round(scores[best] / (double)total, 2) : 0.2;

        var neg = Count(text, Negative);
        var pos = Count(text, Positive);
        var sentiment = neg > pos ? "negative" : pos > neg ? "positive" : "neutral";
        var urgent = Count(text, Urgent);
        var priority = best == "spam" ? "low"
            : best == "complaint" || urgent >= 2 || (best == "support" && urgent > 0) ? "high"
            : best == "sales_lead" && text.Contains('$') ? "high"
            : "normal";

        var first = CountOccurrences(email.Body, "\n\n") >= 1
            ? email.Body.Split(["\n\n"], StringSplitOptions.None)[1]
            : email.Body;
        var summary = Regex.Replace(first, @"\s+", " ").Trim();
        if (summary.Length > 160) summary = summary[..160];

        return new Triage(best, priority, sentiment, confidence, summary, EntityExtractor.Extract(email), Name);
    }

    public string? Draft(IncomingEmail email, Triage triage)
    {
        var first = string.IsNullOrWhiteSpace(email.SenderName)
            ? "there"
            : email.SenderName.Replace("Dr. ", "").Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        if (email.SenderName.StartsWith("Accounts", StringComparison.Ordinal) &&
            email.Body.TrimEnd().Split('\n').Length > 0)
            first = "there";

        var ent = triage.Entities;
        var company = string.IsNullOrWhiteSpace(ent.Company) ? "your team" : ent.Company;
        var invoiceBit = ent.Invoices is { Count: > 0 } ? $" ({string.Join(", ", ent.Invoices)})" : "";
        var when = ent.Days is { Count: > 0 } ? $"{ent.Days[0]} at 2pm CT" : "Thursday at 2pm CT";

        var body = triage.Category switch
        {
            "sales_lead" =>
                $"Thanks for reaching out! This is exactly the kind of workflow I automate for small businesses. To give you an accurate quote and timeline, could we do a quick 20-minute call? I'll come prepared with a rough plan for {company}. Here's my calendar: [booking link].",
            "support" =>
                "Thanks for flagging this — I'm on it now. I'll investigate the error and send you an update within 30 minutes, including a workaround if the fix takes longer.",
            "complaint" =>
                "I'm sorry about this — you're right, that shouldn't have happened, and I understand how it reflects on your practice. I'm investigating the root cause right now and will send you a short incident summary today with what happened and the safeguard I'm adding so it doesn't repeat.",
            "billing" =>
                $"Thanks for the quick note. I've attached our W-9, and yes, we accept ACH — banking details are on the invoice{invoiceBit}. Let me know if your AP team needs anything else.",
            "scheduling" =>
                $"Happy to walk through it together. {when} works on my side — I'll send a calendar invite with a video link. If another time is better, just pick one here: [booking link].",
            "partnership" =>
                "Great to hear from you, and thanks for following up! I'm definitely open to a referral partnership. Could we grab 30 minutes next week to talk about the kinds of clients you see and how we'd hand off projects?",
            _ => null
        };
        return body is null ? null : $"Hi {first},\n\n{body}\n\nBest,\nLeo";
    }

    static int Count(string text, IEnumerable<string> words)
    {
        var n = 0;
        foreach (var w in words) n += CountOccurrences(text, w);
        return n;
    }

    static int CountOccurrences(string text, string word)
    {
        var n = 0;
        var i = 0;
        while ((i = text.IndexOf(word, i, StringComparison.Ordinal)) >= 0)
        {
            n++;
            i += word.Length;
        }
        return n;
    }
}

public sealed class OpenAiLlm : ILlm
{
    readonly string _apiKey;
    readonly string _model;
    readonly MockLlm _fallback;
    readonly Dictionary<string, OpenAiReply> _cache = new();
    readonly HttpClient _http;

    public string Name => $"openai:{_model}";

    /// <summary>Test hook: replace the HTTP call so fallback can be exercised without a real key.</summary>
    public Func<IncomingEmail, CancellationToken, Task<OpenAiReply>>? CallOverride { get; set; }

    public OpenAiLlm(string apiKey, string? model = null, MockLlm? fallback = null, HttpClient? http = null)
    {
        _apiKey = apiKey;
        _model = model ?? Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-4o-mini";
        _fallback = fallback ?? new MockLlm();
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    public async Task<Triage> ClassifyAsync(IncomingEmail email, CancellationToken ct = default)
    {
        try
        {
            var d = await CallAsync(email, ct);
            return new Triage(
                d.Category,
                string.IsNullOrWhiteSpace(d.Priority) ? "normal" : d.Priority,
                string.IsNullOrWhiteSpace(d.Sentiment) ? "neutral" : d.Sentiment,
                d.Confidence ?? 0.8,
                d.Summary ?? "",
                EntityExtractor.Extract(email),
                Name);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ! OpenAI failed ({ex.Message}); using mock-llm for {email.Id}");
            return _fallback.Classify(email);
        }
    }

    public async Task<string?> DraftAsync(IncomingEmail email, Triage triage, CancellationToken ct = default)
    {
        if (triage.Engine != Name)
            return _fallback.Draft(email, triage);
        if (_cache.TryGetValue(email.Id, out var d) && !string.IsNullOrWhiteSpace(d.Reply))
            return d.Reply;
        return _fallback.Draft(email, triage);
    }

    async Task<OpenAiReply> CallAsync(IncomingEmail email, CancellationToken ct)
    {
        if (CallOverride is not null)
        {
            var overridden = await CallOverride(email, ct);
            _cache[email.Id] = overridden;
            return overridden;
        }
        if (_cache.TryGetValue(email.Id, out var cached)) return cached;

        var prompt =
            $"Classify this email for a freelance automation developer (Leo). Categories: {string.Join(", ", Categories.All)}. " +
            "Return JSON with keys: category, priority (high|normal|low), sentiment (positive|neutral|negative), " +
            "confidence (0-1), summary (<=25 words), reply (short professional reply signed 'Best,\\nLeo', or null for spam).\n\n" +
            $"From: {email.SenderName} <{email.SenderEmail}>\nSubject: {email.Subject}\n\n{email.Body}";

        var payload = JsonSerializer.Serialize(new
        {
            model = _model,
            temperature = 0.2,
            response_format = new { type = "json_object" },
            messages = new[] { new { role = "user", content = prompt } }
        });

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        req.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        var content = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()
            ?? throw new InvalidOperationException("empty OpenAI content");
        var reply = JsonSerializer.Deserialize<OpenAiReply>(content, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("could not parse OpenAI JSON");
        if (!Categories.All.Contains(reply.Category))
            throw new InvalidOperationException($"bad category {reply.Category}");
        _cache[email.Id] = reply;
        return reply;
    }
}

public sealed class OpenAiReply
{
    public string Category { get; set; } = "";
    public string? Priority { get; set; }
    public string? Sentiment { get; set; }
    public double? Confidence { get; set; }
    public string? Summary { get; set; }
    public string? Reply { get; set; }
}

public static class LlmFactory
{
    public static ILlm Create(string? engine = null)
    {
        var choice = (engine ?? Environment.GetEnvironmentVariable("LLM_ENGINE") ?? "").Trim().ToLowerInvariant();
        if (choice is "mock" or "mock-llm") return new MockLlm();
        var key = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (choice is "openai" || !string.IsNullOrWhiteSpace(key))
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new InvalidOperationException("OPENAI_API_KEY is not set. Use --engine mock (default) or set the key.");
            return new OpenAiLlm(key);
        }
        return new MockLlm();
    }
}
