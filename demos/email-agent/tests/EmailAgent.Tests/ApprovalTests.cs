using EmailAgent.Core;

namespace EmailAgent.Tests;

using static Fixtures;

public class ApprovalTests
{
    static (MailboxPipeline p, string tmp) NewPipeline()
    {
        var tmp = Directory.CreateTempSubdirectory().FullName;
        var outDir = Path.Combine(tmp, "out");
        var p = new MailboxPipeline(
            new PipelineOptions(Samples, outDir, "hello@northwind-demo.example"),
            new MockLlm(),
            new FileOutboxEmailSender(Path.Combine(outDir, "outbox")));
        return (p, tmp);
    }

    [Fact]
    public async Task Approval_is_required_before_mail_hits_the_outbox()
    {
        var (p, tmp) = NewPipeline();
        var results = await p.TriageAsync();
        Assert.Equal(8, results.Count);
        Assert.Equal(7, p.Pending().Count);
        Assert.False(Directory.Exists(Path.Combine(tmp, "out", "outbox")));
        var sent = await p.ApproveAsync();
        Assert.Equal(7, sent.Count);
        Assert.Empty(p.Pending());
        var emls = Directory.GetFiles(Path.Combine(tmp, "out", "outbox"), "*.eml");
        Assert.Equal(7, emls.Length);
        Assert.All(emls, path =>
        {
            var text = File.ReadAllText(path);
            Assert.Contains("To: ", text);
            Assert.Contains("Hi ", text);
        });
        Directory.Delete(tmp, true);
    }

    [Fact]
    public async Task Spam_is_never_queued_or_sent()
    {
        var (p, tmp) = NewPipeline();
        var results = await p.TriageAsync();
        var spam = results.Single(r => r.Triage.Category == "spam");
        Assert.Null(spam.Draft);
        Assert.Empty(spam.Tasks);
        Assert.DoesNotContain(p.Pending(), m => m.EmailId == spam.Email.Id);
        await p.ApproveAsync();
        Assert.DoesNotContain(
            Directory.GetFiles(Path.Combine(tmp, "out", "outbox"), "*.eml"),
            path => File.ReadAllText(path).Contains("seo-rank-booster", StringComparison.OrdinalIgnoreCase));
        Directory.Delete(tmp, true);
    }

    [Fact]
    public async Task Second_triage_does_not_duplicate_drafts()
    {
        var (p, tmp) = NewPipeline();
        await p.TriageAsync();
        Assert.Equal(7, p.Pending().Count);
        await p.TriageAsync();
        Assert.Equal(7, p.Pending().Count);
        Assert.Equal(7, p.State.Outbox.Count);
        Directory.Delete(tmp, true);
    }

    [Fact]
    public async Task Complaint_needs_a_human_and_still_has_a_draft_and_a_task()
    {
        var (p, tmp) = NewPipeline();
        var results = await p.TriageAsync();
        var complaint = results.Single(r => r.Triage.Category == "complaint");
        Assert.True(complaint.NeedsHuman);
        Assert.Contains("Needs-Human", complaint.Labels);
        Assert.NotNull(complaint.Draft);
        Assert.StartsWith("Hi Sarah,", complaint.Draft);
        Assert.Single(complaint.Tasks);
        Assert.Contains(p.Pending(), m => m.EmailId == complaint.Email.Id);
        Directory.Delete(tmp, true);
    }

    [Fact]
    public async Task Approve_one_id_leaves_the_rest_pending()
    {
        var (p, tmp) = NewPipeline();
        await p.TriageAsync();
        var first = p.Pending()[0];
        var sent = await p.ApproveAsync([first.Id]);
        Assert.Single(sent);
        Assert.Equal(first.Id, sent[0].Id);
        Assert.Equal(6, p.Pending().Count);
        Assert.True(File.Exists(sent[0].EmlPath));
        Directory.Delete(tmp, true);
    }

    [Fact]
    public async Task End_to_end_writes_report_tasks_drafts_and_outbox()
    {
        var (p, tmp) = NewPipeline();
        var crm = new JsonCrm(Path.Combine(tmp, "out", "crm.json"));
        var results = await p.TriageAsync(crm: crm);
        Assert.Equal(8, results.Count);
        Assert.Equal(7, results.SelectMany(r => r.Tasks).Count());
        Assert.Contains("Processed **8** emails", await File.ReadAllTextAsync(Path.Combine(tmp, "out", "triage-report.md")));
        Assert.Contains("Prepare a quote and timeline for Sunset Bakery", await File.ReadAllTextAsync(Path.Combine(tmp, "out", "tasks.md")));
        Assert.Equal(7, Directory.GetFiles(Path.Combine(tmp, "out", "drafts"), "*.txt").Length);

        var sent = await p.ApproveAsync();
        Assert.Equal(7, sent.Count);
        var again = await p.TriageAsync();
        Assert.Empty(p.Pending());
        Assert.Equal(8, again.Count);
        Directory.Delete(tmp, true);
    }

    [Fact]
    public void Low_confidence_is_escalated_to_a_human()
    {
        var email = Mail("Hello", "Just saying hi, nothing specific.");
        var triage = new Triage("support", "normal", "neutral", 0.2, "unclear", new ExtractedEntities(), "mock-llm");
        var (_, _, human) = Router.Route(email, triage);
        Assert.True(human);
        Assert.True(0.2 < Router.ConfidenceFloor);
    }
}
