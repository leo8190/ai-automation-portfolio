using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EmailAgent.Core;

public static class TriageAgent
{
    public static async Task<IReadOnlyList<TriageResult>> ProcessAsync(
        IEnumerable<IncomingEmail> emails, ILlm llm, ICrmSink? crm = null, CancellationToken ct = default)
    {
        var results = new List<TriageResult>();
        foreach (var email in emails)
        {
            var triage = await llm.ClassifyAsync(email, ct);
            var (labels, crmAction, needsHuman) = Router.Route(email, triage);
            var draft = await llm.DraftAsync(email, triage, ct);
            var tasks = TaskExtractor.Extract(email, triage);
            if (crm is not null && crmAction is not null)
                await crm.PushAsync(crmAction, email, triage, ct);
            results.Add(new TriageResult(email, triage, labels, draft, crmAction, needsHuman, tasks));
        }
        if (crm is not null) await crm.FlushAsync(ct);
        return results;
    }

    public static async Task<string> WriteOutputsAsync(IReadOnlyList<TriageResult> results, string outDir, CancellationToken ct = default)
    {
        Directory.CreateDirectory(outDir);
        var draftsDir = Path.Combine(outDir, "drafts");
        Directory.CreateDirectory(draftsDir);

        foreach (var r in results)
        {
            if (string.IsNullOrEmpty(r.Draft)) continue;
            var safe = new string(r.Email.Id.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
            if (safe.Length > 50) safe = safe[..50];
            var text =
                $"To: {r.Email.SenderEmail}\nSubject: Re: {r.Email.Subject}\nX-Status: DRAFT (not sent - awaiting approval)\n\n{r.Draft}\n";
            await File.WriteAllTextAsync(Path.Combine(draftsDir, $"{safe}.txt"), text, ct);
        }

        var jsonOpts = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
        var payload = results.Select(ToDict).ToList();
        await File.WriteAllTextAsync(Path.Combine(outDir, "triage.json"), JsonSerializer.Serialize(payload, jsonOpts), ct);

        var cats = results.GroupBy(r => r.Triage.Category).OrderByDescending(g => g.Count()).ThenBy(g => g.Key);
        var md = new StringBuilder();
        md.AppendLine("# Inbox triage report").AppendLine();
        var engine = results.Count > 0 ? results[0].Triage.Engine : "-";
        var drafts = results.Count(r => r.Draft is not null);
        var human = results.Count(r => r.NeedsHuman);
        md.AppendLine($"Processed **{results.Count}** emails · engine: `{engine}` · drafts: **{drafts}** · needs human: **{human}**")
          .AppendLine()
          .AppendLine("| Pri | Category | From | Subject | Labels | CRM |")
          .AppendLine("|---|---|---|---|---|---|");
        var order = new Dictionary<string, int> { ["high"] = 0, ["normal"] = 1, ["low"] = 2 };
        foreach (var r in results.OrderBy(r => order.GetValueOrDefault(r.Triage.Priority, 1)))
        {
            md.AppendLine($"| {r.Triage.Priority} | {r.Triage.Category} ({r.Triage.Confidence.ToString("0%", CultureInfo.InvariantCulture)}) | {r.Email.SenderName} | {r.Email.Subject} | {string.Join(", ", r.Labels)} | {r.CrmAction ?? "-"} |");
        }
        md.AppendLine().AppendLine("Category mix: " + string.Join(", ", cats.Select(g => $"{g.Key}: {g.Count()}")));
        var allTasks = results.SelectMany(r => r.Tasks.Select(t => (r, t))).ToList();
        md.AppendLine().AppendLine("## Tasks").AppendLine();
        if (allTasks.Count == 0) md.AppendLine("None extracted.");
        else
        {
            md.AppendLine("| From | Kind | Due | Task |").AppendLine("|---|---|---|---|");
            foreach (var (r, task) in allTasks)
                md.AppendLine($"| {r.Email.SenderName} | {task.Kind} | {task.DueHint ?? "—"} | {task.Title} |");
        }
        var report = Path.Combine(outDir, "triage-report.md");
        await File.WriteAllTextAsync(report, md.ToString(), ct);
        var tasksMd = new StringBuilder().AppendLine("# Extracted tasks").AppendLine();
        if (allTasks.Count == 0) tasksMd.AppendLine("None extracted.");
        else
        {
            tasksMd.AppendLine("| From | Kind | Due | Task |").AppendLine("|---|---|---|---|");
            foreach (var (r, task) in allTasks)
                tasksMd.AppendLine($"| {r.Email.SenderName} | {task.Kind} | {task.DueHint ?? "—"} | {task.Title} |");
        }
        await File.WriteAllTextAsync(Path.Combine(outDir, "tasks.md"), tasksMd.ToString(), ct);
        return report;
    }

    public static Dictionary<string, object?> ToDict(TriageResult r) => new()
    {
        ["email"] = new Dictionary<string, object?>
        {
            ["id"] = r.Email.Id,
            ["senderName"] = r.Email.SenderName,
            ["senderEmail"] = r.Email.SenderEmail,
            ["to"] = r.Email.To,
            ["subject"] = r.Email.Subject,
            ["date"] = r.Email.Date,
        },
        ["triage"] = new Dictionary<string, object?>
        {
            ["category"] = r.Triage.Category,
            ["priority"] = r.Triage.Priority,
            ["sentiment"] = r.Triage.Sentiment,
            ["confidence"] = r.Triage.Confidence,
            ["summary"] = r.Triage.Summary,
            ["entities"] = r.Triage.Entities,
            ["engine"] = r.Triage.Engine,
        },
        ["labels"] = r.Labels,
        ["draft"] = r.Draft,
        ["crmAction"] = r.CrmAction,
        ["needsHuman"] = r.NeedsHuman,
        ["tasks"] = r.Tasks,
    };
}
