using CrmFollowup.Core;

public class SequenceTests
{
    [Fact]
    public void New_stage_has_four_steps_on_days_0_2_5_10()
    {
        Assert.Equal([0, 2, 5, 10], Sequences.For(Stages.New).Select(s => s.DayOffset).ToArray());
        Assert.Equal(["new-intro", "new-bump", "new-value", "new-breakup"], Sequences.For(Stages.New).Select(s => s.Key).ToArray());
    }

    [Fact]
    public void Contacted_and_proposal_have_their_own_cadences()
    {
        Assert.Equal([3, 7], Sequences.For(Stages.Contacted).Select(s => s.DayOffset).ToArray());
        Assert.Equal([2, 6, 12], Sequences.For(Stages.Proposal).Select(s => s.DayOffset).ToArray());
    }

    [Fact]
    public void Won_and_lost_have_no_sequence()
    {
        Assert.Empty(Sequences.For(Stages.Won));
        Assert.Empty(Sequences.For(Stages.Lost));
        Assert.False(Stages.IsActive(Stages.Won));
        Assert.False(Stages.IsActive(Stages.Lost));
        Assert.True(Stages.IsActive(Stages.New));
    }

    [Fact]
    public void Unknown_stage_has_no_steps() => Assert.Empty(Sequences.For("Waiting"));
}
