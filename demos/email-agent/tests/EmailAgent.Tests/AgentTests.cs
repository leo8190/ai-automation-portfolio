using EmailAgent.Core;

namespace EmailAgent.Tests;

using static Fixtures;

public class LoaderTests
{
    [Fact]
    public void Loads_eml_and_json()
    {
        var emails = InboxLoader.Load(Samples);
        Assert.Equal(8, emails.Count);
        var rachel = emails.Single(e => e.SenderEmail == "rachel@sunsetbakery.example");
        Assert.Equal("Rachel Green", rachel.SenderName);
        Assert.Contains("Square", rachel.Body);
    }
}

public class ClassificationTests
{
    static readonly Dictionary<string, string> Expected = new()
    {
        ["Quote for online ordering system?"] = "sales_lead",
        ["Dispatch dashboard not loading - drivers can't see routes"] = "support",
        ["Question about invoice INV-2026-10-003"] = "billing",
        ["Can we meet Thursday to review the proposal?"] = "scheduling",
        ["Get #1 on Google GUARANTEED!!! Limited offer"] = "spam",
        ["Very disappointed - patients got duplicate reminders"] = "complaint",
        ["Looking for someone to automate our membership emails"] = "sales_lead",
        ["Re: partnership idea"] = "partnership",
    };

    [Fact]
    public void Sample_inbox_accuracy_is_100_percent()
    {
        var llm = new MockLlm();
        var got = InboxLoader.Load(Samples).ToDictionary(e => e.Subject, e => llm.Classify(e).Category);
        Assert.Equal(Expected, got);
    }

    [Fact]
    public void Urgent_support_is_high_priority()
    {
        var t = new MockLlm().Classify(Mail("Site down", "The site is down with an error, urgent, customers waiting"));
        Assert.Equal("support", t.Category);
        Assert.Equal("high", t.Priority);
    }

    [Fact]
    public void Extracts_entities()
    {
        var ent = EntityExtractor.Extract(Mail(
            "Invoice INV-2026-10-003",
            "Hi,\n\nWe run Peak Fitness. Budget is $3,000-$5,000. Thursday works."));
        Assert.Equal(["INV-2026-10-003"], ent.Invoices);
        Assert.Equal("Peak Fitness", ent.Company);
        Assert.Equal(["Thursday"], ent.Days);
        Assert.StartsWith("$3,000", ent.Amounts![0]);
    }
}

public class RoutingTests
{
    [Fact]
    public void Complaint_escalates_to_human()
    {
        var e = Mail("Very disappointed", "Hi,\n\nThis is frustrating and unprofessional. I complained already.");
        var t = new MockLlm().Classify(e);
        var (labels, _, human) = Router.Route(e, t);
        Assert.True(human);
        Assert.Contains("Escalations", labels);
        Assert.Contains("Needs-Human", labels);
    }

    [Fact]
    public void Spam_gets_no_draft_and_no_crm()
    {
        var e = Mail("WIN!!!", "Dear website owner, click here, limited time, act now. Unsubscribe.");
        var llm = new MockLlm();
        var t = llm.Classify(e);
        Assert.Equal("spam", t.Category);
        Assert.Null(llm.Draft(e, t));
        Assert.Null(Router.Route(e, t).CrmAction);
    }
}

public class PipelineTests
{
    [Fact]
    public async Task End_to_end_outputs()
    {
        var outDir = Path.Combine(Path.GetTempPath(), "email-agent-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outDir);
        try
        {
            var crm = new JsonCrm(Path.Combine(outDir, "crm.json"));
            var results = await TriageAgent.ProcessAsync(InboxLoader.Load(Samples), new MockLlm(), crm);
            var report = await TriageAgent.WriteOutputsAsync(results, outDir);
            Assert.Contains("Processed **8** emails", await File.ReadAllTextAsync(report));
            Assert.Equal(7, Directory.GetFiles(Path.Combine(outDir, "drafts"), "*.txt").Length);
            using var doc = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(outDir, "crm.json")));
            var leads = doc.RootElement.EnumerateArray()
                .Where(r => r.GetProperty("action").GetString() == "create_lead")
                .Select(r => r.GetProperty("email").GetString()!)
                .ToHashSet();
            Assert.Equal(new HashSet<string> { "rachel@sunsetbakery.example", "kevin@peakfitness.example", "maria@greenleafcpa.example" }, leads);
            Assert.All(Directory.GetFiles(Path.Combine(outDir, "drafts"), "*.txt"), p => Assert.Contains("DRAFT", File.ReadAllText(p)));
        }
        finally
        {
            if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
        }
    }

    [Fact]
    public async Task OpenAI_failure_falls_back_to_mock()
    {
        var llm = new OpenAiLlm("sk-invalid-test-key")
        {
            CallOverride = (_, _) => throw new InvalidOperationException("network down")
        };
        var e = InboxLoader.Load(Path.Combine(Samples, "inbox", "01-new-lead.eml"))[0];
        var t = await llm.ClassifyAsync(e);
        Assert.Equal("sales_lead", t.Category);
        Assert.Equal("mock-llm", t.Engine);
        var draft = await llm.DraftAsync(e, t);
        Assert.StartsWith("Hi Rachel", draft);
    }
}

internal static class Fixtures
{
    public static string Samples
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "sample-data"))) dir = dir.Parent;
            if (dir is null) throw new DirectoryNotFoundException("sample-data not found from " + AppContext.BaseDirectory);
            return Path.Combine(dir.FullName, "sample-data");
        }
    }

    public static IncomingEmail Mail(string subject, string body, string sender = "Pat Doe", string addr = "pat@acme.example")
        => new("t1", sender, addr, "me@x.com", subject, "", body);
}
