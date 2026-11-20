using CrmFollowup.Core;

public class PipelineTests
{
    static (Pipeline p, string tmp) NewPipeline()
    {
        var tmp = Directory.CreateTempSubdirectory().FullName;
        var data = Path.Combine(tmp, "data");
        Directory.CreateDirectory(data);
        foreach (var file in Directory.GetFiles(Repo.Data))
            File.Copy(file, Path.Combine(data, Path.GetFileName(file)));
        var outDir = Path.Combine(tmp, "out");
        var p = new Pipeline(new PipelineOptions(data, outDir), new TemplateWriter(),
            new FileOutboxEmailSender(Path.Combine(outDir, "outbox")));
        return (p, tmp);
    }

    static readonly DateOnly Opened = new(2026, 11, 3);

    [Fact]
    public void Seed_schedules_cadences_and_skips_won_and_lost()
    {
        var (p, tmp) = NewPipeline();
        p.Seed();
        Assert.Equal(6, p.State.Leads.Count);
        Assert.Equal([0, 2, 5, 10], Offsets("LEAD-001"));
        Assert.Equal([3, 7], Offsets("LEAD-002"));
        Assert.Equal([2, 6, 12], Offsets("LEAD-004"));
        Assert.Empty(p.State.Tasks.Where(t => t.LeadId is "LEAD-005" or "LEAD-006"));
        p.Seed();
        Assert.Equal(6, p.State.Leads.Count); // idempotent
        Directory.Delete(tmp, true);

        int[] Offsets(string leadId) => p.State.Tasks
            .Where(t => t.LeadId == leadId)
            .Select(t => t.DueOn.DayNumber - Opened.DayNumber)
            .ToArray();
    }

    [Fact]
    public async Task ProcessDue_drafts_only_steps_that_are_due()
    {
        var (p, tmp) = NewPipeline();
        p.Seed();
        var day0 = await p.ProcessDueAsync(Opened);
        Assert.Equal(2, day0.Count);
        Assert.All(day0, t => Assert.Equal("Intro", t.StepLabel));
        Assert.Equal(["LEAD-001", "LEAD-003"], day0.Select(t => t.LeadId).OrderBy(x => x).ToArray());
        Assert.All(day0, t => Assert.Equal("mock-llm", t.DraftedBy));
        Assert.StartsWith("Hi Sarah,", p.State.Tasks.Single(t => t.LeadId == "LEAD-001" && t.StepKey == "new-intro").Draft);

        var still = await p.ProcessDueAsync(Opened);
        Assert.Empty(still); // already drafted
        Directory.Delete(tmp, true);
    }

    [Fact]
    public async Task Approval_is_required_before_mail_hits_the_outbox()
    {
        var (p, tmp) = NewPipeline();
        p.Seed();
        await p.ProcessDueAsync(Opened);
        Assert.Equal(2, p.Pending().Count);
        Assert.False(Directory.Exists(Path.Combine(tmp, "out", "outbox")));
        var sent = await p.ApproveAsync();
        Assert.Equal(2, sent.Count);
        Assert.Empty(p.Pending());
        var emls = Directory.GetFiles(Path.Combine(tmp, "out", "outbox"), "*.eml");
        Assert.Equal(2, emls.Length);
        Assert.All(emls, path =>
        {
            var text = File.ReadAllText(path);
            Assert.Contains("To: ", text);
            Assert.Contains("Hi ", text);
        });
        Assert.All(p.State.Tasks.Where(t => t.Status == FollowUpStatus.Sent), t => Assert.NotNull(t.SentOn));
        Directory.Delete(tmp, true);
    }

    [Fact]
    public async Task Approve_can_use_edited_text()
    {
        var (p, tmp) = NewPipeline();
        p.Seed();
        await p.ProcessDueAsync(Opened);
        var task = p.State.Tasks.Single(t => t.LeadId == "LEAD-001" && t.Status == FollowUpStatus.Ready);
        p.ApproveWithEdit(task.Id, "Hi Sarah, edited by a person.");
        var sent = await p.ApproveAsync(["--unused"]); // empty wanted set would approve all if we pass empty list...
        // ApproveAsync(null) approves all. Pass the task id.
        // The previous call with ["--unused"] should have approved nothing.
        Assert.Empty(sent);
        sent = await p.ApproveAsync([task.Id]);
        Assert.Single(sent);
        Assert.Equal("Hi Sarah, edited by a person.", sent[0].Body);
        Assert.Contains("Hi Sarah, edited by a person.", File.ReadAllText(sent[0].EmlPath!));
        Directory.Delete(tmp, true);
    }

    [Fact]
    public async Task Skip_does_not_write_to_the_outbox()
    {
        var (p, tmp) = NewPipeline();
        p.Seed();
        await p.ProcessDueAsync(Opened);
        var task = p.State.Tasks.Single(t => t.LeadId == "LEAD-003" && t.Status == FollowUpStatus.Ready);
        p.Skip(task.Id);
        var sent = await p.ApproveAsync();
        Assert.Single(sent); // only Sarah's intro
        Assert.Equal(FollowUpStatus.Skipped, p.State.Tasks.Single(t => t.Id == task.Id).Status);
        Assert.Equal("Skipped", p.State.Outbox.Single(m => m.TaskId == task.Id).Status);
        Directory.Delete(tmp, true);
    }

    [Fact]
    public async Task Reply_cancels_open_followups_but_leaves_sent_alone()
    {
        var (p, tmp) = NewPipeline();
        p.Seed();
        await p.ProcessDueAsync(Opened);
        await p.ApproveAsync();
        p.MarkReplied("LEAD-001", Opened);
        Assert.True(p.RequireLead("LEAD-001").Replied);
        var tasks = p.State.Tasks.Where(t => t.LeadId == "LEAD-001").ToList();
        Assert.Equal(FollowUpStatus.Sent, tasks.Single(t => t.StepKey == "new-intro").Status);
        Assert.All(tasks.Where(t => t.StepKey != "new-intro"), t => Assert.Equal(FollowUpStatus.Cancelled, t.Status));
        var later = await p.ProcessDueAsync(Opened.AddDays(30));
        Assert.DoesNotContain(later, x => x.LeadId == "LEAD-001");
        Directory.Delete(tmp, true);
    }

    [Fact]
    public async Task Ready_draft_is_cancelled_when_the_lead_replies()
    {
        var (p, tmp) = NewPipeline();
        p.Seed();
        await p.ProcessDueAsync(Opened);
        p.MarkReplied("LEAD-001", Opened);
        var intro = p.State.Tasks.Single(t => t.LeadId == "LEAD-001" && t.StepKey == "new-intro");
        Assert.Equal(FollowUpStatus.Cancelled, intro.Status);
        Assert.Equal("Cancelled", p.State.Outbox.Single(m => m.TaskId == intro.Id).Status);
        Assert.DoesNotContain(p.Pending(), m => m.LeadId == "LEAD-001");
        Assert.Contains(p.Pending(), m => m.LeadId == "LEAD-003"); // Jenna's intro is still waiting
        Directory.Delete(tmp, true);
    }

    [Fact]
    public void Stage_change_swaps_the_sequence_and_won_has_none()
    {
        var (p, tmp) = NewPipeline();
        p.Seed();
        p.MoveStage("LEAD-001", Stages.Proposal, Opened);
        var open = p.State.Tasks.Where(t => t.LeadId == "LEAD-001" && t.Status == FollowUpStatus.Pending).ToList();
        Assert.Equal(3, open.Count);
        Assert.All(open, t => Assert.StartsWith("proposal-", t.StepKey));
        // leftover New steps were cancelled
        Assert.Equal(4, p.State.Tasks.Count(t => t.LeadId == "LEAD-001" && t.Status == FollowUpStatus.Cancelled));

        p.MoveStage("LEAD-001", Stages.Won, Opened);
        Assert.DoesNotContain(p.State.Tasks, t => t.LeadId == "LEAD-001" && t.Status == FollowUpStatus.Pending);
        Directory.Delete(tmp, true);
    }

    [Fact]
    public void Unknown_stage_is_rejected()
    {
        var (p, tmp) = NewPipeline();
        p.Seed();
        Assert.Throws<ArgumentException>(() => p.MoveStage("LEAD-001", "Waiting", Opened));
        Directory.Delete(tmp, true);
    }

    [Fact]
    public void Same_stage_move_is_a_noop()
    {
        var (p, tmp) = NewPipeline();
        p.Seed();
        var before = p.State.Tasks.Count;
        p.MoveStage("LEAD-001", Stages.New, Opened);
        Assert.Equal(before, p.State.Tasks.Count);
        Directory.Delete(tmp, true);
    }

    [Fact]
    public async Task End_to_end_due_reminders_approval_reply_and_stage_switch()
    {
        var (p, tmp) = NewPipeline();
        p.Seed();

        var day0 = await p.ProcessDueAsync(Opened);
        Assert.Equal(2, day0.Count);
        var sent0 = await p.ApproveAsync();
        Assert.Equal(2, sent0.Count);
        Assert.All(sent0, m => Assert.True(new FileInfo(m.EmlPath!).Length > 80));

        var day3 = await p.ProcessDueAsync(new DateOnly(2026, 11, 6));
        // Sarah bump (day 2), Jenna bump (day 2), Mark check-in (day 3), Tom proposal check-in (day 2)
        Assert.Equal(4, day3.Count);
        Assert.Contains(day3, t => t.LeadId == "LEAD-001" && t.StepKey == "new-bump");
        Assert.Contains(day3, t => t.LeadId == "LEAD-002" && t.StepKey == "contacted-checkin");
        Assert.Contains(day3, t => t.LeadId == "LEAD-003" && t.StepKey == "new-bump");
        Assert.Contains(day3, t => t.LeadId == "LEAD-004" && t.StepKey == "proposal-checkin");
        await p.ApproveAsync();

        p.MarkReplied("LEAD-002", new DateOnly(2026, 11, 6));
        Assert.Equal(FollowUpStatus.Cancelled, p.State.Tasks.Single(t => t.LeadId == "LEAD-002" && t.StepKey == "contacted-value").Status);
        Assert.Equal(FollowUpStatus.Sent, p.State.Tasks.Single(t => t.LeadId == "LEAD-002" && t.StepKey == "contacted-checkin").Status);

        p.MoveStage("LEAD-001", Stages.Proposal, new DateOnly(2026, 11, 6));
        Assert.Equal(FollowUpStatus.Cancelled, p.State.Tasks.Single(t => t.LeadId == "LEAD-001" && t.StepKey == "new-value").Status);
        Assert.Equal(FollowUpStatus.Sent, p.State.Tasks.Single(t => t.LeadId == "LEAD-001" && t.StepKey == "new-intro").Status);

        var sameDay = await p.ProcessDueAsync(new DateOnly(2026, 11, 6));
        Assert.Empty(sameDay);

        var day5 = await p.ProcessDueAsync(new DateOnly(2026, 11, 8));
        Assert.Equal(2, day5.Count);
        Assert.Contains(day5, t => t.LeadId == "LEAD-001" && t.StepKey == "proposal-checkin");
        Assert.Contains(day5, t => t.LeadId == "LEAD-003" && t.StepKey == "new-value");
        await p.ApproveAsync();

        var repeat = await p.ProcessDueAsync(new DateOnly(2026, 11, 8));
        Assert.Empty(repeat);

        Assert.Empty(p.State.Tasks.Where(t => t.LeadId is "LEAD-005" or "LEAD-006"));
        var eml = Directory.GetFiles(Path.Combine(tmp, "out", "outbox"), "*.eml");
        Assert.True(eml.Length >= 7, $"expected intros, bumps, check-ins and Sarah's proposal mail, got {eml.Length}");
        Directory.Delete(tmp, true);
    }

    [Fact]
    public async Task Adding_a_lead_later_starts_its_own_sequence()
    {
        var (p, tmp) = NewPipeline();
        p.Seed();
        var lead = p.AddLead(new SeedLead
        {
            Id = "LEAD-007",
            Name = "Elena Vasquez",
            Company = "Harborline Demo LLC",
            Email = "elena@harborline-demo.example",
            Stage = Stages.New,
            ValueUsd = 2000,
            Source = "Referral",
            Notes = "irrigation scheduling for two sites",
            OpenedOn = new DateOnly(2026, 11, 10),
        });
        Assert.Equal("Elena", lead.FirstName);
        var drafted = await p.ProcessDueAsync(new DateOnly(2026, 11, 10));
        Assert.Contains(drafted, t => t.LeadId == "LEAD-007" && t.StepKey == "new-intro");
        Assert.Contains("irrigation scheduling for two sites", drafted.Single(t => t.LeadId == "LEAD-007").Draft);
        Directory.Delete(tmp, true);
    }

    [Fact]
    public async Task Skip_rejects_a_task_that_was_already_sent()
    {
        var (p, tmp) = NewPipeline();
        p.Seed();
        await p.ProcessDueAsync(Opened);
        var task = p.State.Tasks.Single(t => t.LeadId == "LEAD-001" && t.Status == FollowUpStatus.Ready);
        await p.ApproveAsync([task.Id]);
        Assert.Throws<InvalidOperationException>(() => p.Skip(task.Id));
        Directory.Delete(tmp, true);
    }
}

internal static class Repo
{
    public static string Root
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "sample-data"))) dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("sample-data not found");
        }
    }

    public static string Data => Path.Combine(Root, "sample-data");
}
