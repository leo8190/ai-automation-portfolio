using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EmailAgent.Core;

public static class Router
{
    public const double ConfidenceFloor = 0.45;

    static readonly Dictionary<string, (string[] Labels, string? Crm)> Routes = new()
    {
        ["sales_lead"] = (["Leads/New"], "create_lead"),
        ["support"] = (["Support"], "log_activity"),
        ["complaint"] = (["Support", "Escalations"], "log_activity"),
        ["billing"] = (["Billing"], null),
        ["scheduling"] = (["Meetings"], "log_activity"),
        ["partnership"] = (["Partners"], "create_lead"),
        ["spam"] = (["Spam/Auto-archived"], null),
    };

    public static (List<string> Labels, string? CrmAction, bool NeedsHuman) Route(IncomingEmail email, Triage triage)
    {
        var rule = Routes[triage.Category];
        var labels = new List<string>(rule.Labels);
        if (triage.Priority == "high") labels.Add("Priority/High");
        var needsHuman = triage.Category == "complaint" || triage.Confidence < ConfidenceFloor;
        if (needsHuman) labels.Add("Needs-Human");
        return (labels, rule.Crm, needsHuman);
    }
}

public interface ICrmSink
{
    Task PushAsync(string action, IncomingEmail email, Triage triage, CancellationToken ct = default);
    Task FlushAsync(CancellationToken ct = default);
}

public class JsonCrm : ICrmSink
{
    public string Path { get; }
    protected readonly List<Dictionary<string, object?>> Records = [];

    public JsonCrm(string path) => Path = path;

    public virtual Task PushAsync(string action, IncomingEmail email, Triage triage, CancellationToken ct = default)
    {
        Records.Add(new Dictionary<string, object?>
        {
            ["action"] = action,
            ["name"] = email.SenderName,
            ["email"] = email.SenderEmail,
            ["company"] = triage.Entities.Company,
            ["category"] = triage.Category,
            ["summary"] = triage.Summary,
            ["subject"] = email.Subject,
        });
        return Task.CompletedTask;
    }

    public virtual async Task FlushAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var json = JsonSerializer.Serialize(Records, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path, json, ct);
    }
}

public sealed class HttpCrm : JsonCrm
{
    readonly string _base;
    readonly HttpClient _http;

    public HttpCrm(string path, string baseUrl, HttpClient? http = null) : base(path)
    {
        _base = baseUrl.TrimEnd('/');
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    }

    public override async Task PushAsync(string action, IncomingEmail email, Triage triage, CancellationToken ct = default)
    {
        await base.PushAsync(action, email, triage, ct);
        if (action != "create_lead") return;

        var value = 0;
        foreach (var amount in triage.Entities.Amounts ?? [])
        {
            foreach (Match m in Regex.Matches(amount, @"[\d,]+"))
            {
                var digits = m.Value.Replace(",", "");
                if (int.TryParse(digits, out var n) && n > value) value = n;
            }
        }

        var body = new
        {
            name = email.SenderName,
            company = triage.Entities.Company ?? "",
            email = email.SenderEmail,
            stage = "New",
            valueUsd = value,
            source = "Email agent",
            notes = triage.Summary
        };

        try
        {
            using var res = await _http.PostAsJsonAsync($"{_base}/api/leads", body, ct);
            res.EnsureSuccessStatusCode();
            var payload = await res.Content.ReadFromJsonAsync<JsonElement>(ct);
            Records[^1]["crm_response"] = payload;
        }
        catch (Exception ex)
        {
            Records[^1]["crm_error"] = ex.Message;
        }
    }
}
