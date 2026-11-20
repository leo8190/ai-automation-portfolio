using EmailAgent.Core;

namespace EmailAgent.Tests;

using static Fixtures;

public class TaskTests
{
    static (IncomingEmail email, Triage triage) Sample(string subjectPart)
    {
        var email = InboxLoader.Load(Samples).Single(e => e.Subject.Contains(subjectPart, StringComparison.OrdinalIgnoreCase));
        var triage = new MockLlm().Classify(email);
        return (email, triage);
    }

    [Fact]
    public void Sales_lead_task_includes_company_and_budget()
    {
        var (email, triage) = Sample("online ordering");
        var task = Assert.Single(TaskExtractor.Extract(email, triage));
        Assert.Equal("quote", task.Kind);
        Assert.Contains("Sunset Bakery", task.Title);
        Assert.Contains("$3,000", task.Title);
        Assert.Null(task.DueHint);
    }

    [Fact]
    public void Membership_lead_task_names_peak_fitness()
    {
        var (email, triage) = Sample("membership emails");
        var task = Assert.Single(TaskExtractor.Extract(email, triage));
        Assert.Contains("Peak Fitness", task.Title);
        Assert.Equal("quote", task.Kind);
    }

    [Fact]
    public void Urgent_support_task_is_due_today()
    {
        var (email, triage) = Sample("Dispatch dashboard");
        Assert.Equal("high", triage.Priority);
        var task = Assert.Single(TaskExtractor.Extract(email, triage));
        Assert.Equal("support", task.Kind);
        Assert.Equal("today", task.DueHint);
        Assert.Contains("Dispatch dashboard", task.Title);
    }

    [Fact]
    public void Billing_task_mentions_the_invoice_and_ach()
    {
        var (email, triage) = Sample("INV-2026-10-003");
        var task = Assert.Single(TaskExtractor.Extract(email, triage));
        Assert.Equal("billing", task.Kind);
        Assert.Contains("INV-2026-10-003", task.Title);
        Assert.Contains("W-9", task.Title);
        Assert.Contains("ACH", task.Title);
        Assert.Contains("$1,097.10", task.Title);
    }

    [Fact]
    public void Scheduling_task_lists_the_proposed_days()
    {
        var (email, triage) = Sample("meet Thursday");
        var task = Assert.Single(TaskExtractor.Extract(email, triage));
        Assert.Equal("meeting", task.Kind);
        Assert.Contains("Tom Becker", task.Title);
        Assert.Contains("Thursday", task.Title);
        Assert.Contains("Friday", task.Title);
        Assert.Equal("Thursday or Friday", task.DueHint);
    }

    [Fact]
    public void Complaint_task_is_an_incident_due_today()
    {
        var (email, triage) = Sample("duplicate reminders");
        var task = Assert.Single(TaskExtractor.Extract(email, triage));
        Assert.Equal("incident", task.Kind);
        Assert.Equal("today", task.DueHint);
        Assert.Contains("written summary", task.Title);
    }

    [Fact]
    public void Partnership_task_is_extracted_and_spam_is_not()
    {
        var partner = Sample("partnership idea");
        var partnerTask = Assert.Single(TaskExtractor.Extract(partner.email, partner.triage));
        Assert.Equal("partnership", partnerTask.Kind);
        Assert.Contains("Greenleaf CPA", partnerTask.Title);

        var spam = Sample("GUARANTEED");
        Assert.Equal("spam", spam.triage.Category);
        Assert.Empty(TaskExtractor.Extract(spam.email, spam.triage));
    }
}
