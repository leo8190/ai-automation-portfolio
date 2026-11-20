using CrmFollowup.Core;

public class WriterTests
{
    static Lead Sarah() => new()
    {
        Id = "LEAD-001",
        Name = "Sarah Kim",
        CompanyName = "Acme Dental Group",
        Email = "sarah@acmedental.example",
        Stage = Stages.New,
        Notes = "online booking and SMS reminders for 3 clinics",
    };

    [Fact]
    public async Task Intro_draft_uses_the_lead_name_and_notes()
    {
        var step = Sequences.For(Stages.New)[0];
        var draft = await new TemplateWriter().DraftAsync(Sarah(), step);
        Assert.StartsWith("Hi Sarah,", draft);
        Assert.Contains("online booking and SMS reminders for 3 clinics", draft);
        Assert.Contains("15-minute call", draft);
        Assert.EndsWith("Best,\nLeo", draft);
    }

    [Fact]
    public async Task Bump_and_value_drafts_name_the_company()
    {
        var writer = new TemplateWriter();
        var lead = Sarah();
        var bump = await writer.DraftAsync(lead, Sequences.For(Stages.New)[1]);
        var value = await writer.DraftAsync(lead, Sequences.For(Stages.New)[2]);
        Assert.Contains("Acme Dental Group", bump);
        Assert.Contains("Acme Dental Group", value);
        Assert.Contains("8 hours", value);
    }

    [Fact]
    public void Writer_is_deterministic()
    {
        var step = Sequences.For(Stages.Proposal)[0];
        var lead = new Lead
        {
            Id = "LEAD-004",
            Name = "Tom Becker",
            CompanyName = "Becker HVAC",
            Email = "tom@beckerhvac.example",
            Stage = Stages.Proposal,
            Notes = "automatic quote follow-ups for your technicians",
        };
        var a = TemplateWriter.Render(lead, step);
        var b = TemplateWriter.Render(lead, step);
        Assert.Equal(a, b);
        Assert.Contains("Becker HVAC", a);
    }

    [Fact]
    public void Subjects_are_specific_to_the_step()
    {
        var lead = Sarah();
        Assert.Equal("Quick intro — Acme Dental Group", TemplateWriter.Subject(lead, Sequences.For(Stages.New)[0]));
        Assert.Equal("Holding the quoted price for Acme Dental Group", TemplateWriter.Subject(lead, Sequences.For(Stages.Proposal)[2]));
    }

    [Fact]
    public async Task OpenAI_failure_falls_back_to_the_mock_writer()
    {
        var writer = new OpenAiWriter("sk-invalid-test-key")
        {
            CallOverride = (_, _, _) => throw new InvalidOperationException("network down"),
        };
        var draft = await writer.DraftAsync(Sarah(), Sequences.For(Stages.New)[0]);
        Assert.StartsWith("Hi Sarah,", draft);
        Assert.Contains("online booking and SMS reminders for 3 clinics", draft);
    }

    [Fact]
    public async Task Last_call_draft_holds_the_quoted_price()
    {
        var lead = Sarah();
        lead.Stage = Stages.Proposal;
        var draft = await new TemplateWriter().DraftAsync(lead, Sequences.For(Stages.Proposal)[2]);
        Assert.Contains("hold the quoted price", draft);
        Assert.Contains("Acme Dental Group", draft);
        Assert.Contains("end of next week", draft);
    }
}
