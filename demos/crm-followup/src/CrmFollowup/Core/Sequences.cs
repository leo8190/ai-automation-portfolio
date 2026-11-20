namespace CrmFollowup.Core;

/// <summary>Follow-up cadences per pipeline stage. Edit here without touching the engine.</summary>
public static class Sequences
{
    public static readonly IReadOnlyDictionary<string, SequenceStep[]> ByStage = new Dictionary<string, SequenceStep[]>
    {
        [Stages.New] =
        [
            new("new-intro", "Intro", 0, "introduce yourself and ask for a 15-minute call"),
            new("new-bump", "Bump", 2, "short friendly bump on the previous email"),
            new("new-value", "Value / case study", 5, "share a relevant result from a similar client"),
            new("new-breakup", "Break-up", 10, "polite last touch, offer to close the file"),
        ],
        [Stages.Contacted] =
        [
            new("contacted-checkin", "Check-in", 3, "check in after the first conversation and propose next step"),
            new("contacted-value", "Value", 7, "send a short idea or mini-plan tailored to their problem"),
        ],
        [Stages.Proposal] =
        [
            new("proposal-checkin", "Proposal check-in", 2, "ask if they had a chance to review the proposal"),
            new("proposal-objections", "Handle objections", 6, "address common concerns (budget, timeline) and offer a smaller phase 1"),
            new("proposal-lastcall", "Last call", 12, "final nudge with a clear deadline for the quoted price"),
        ],
    };

    public static SequenceStep[] For(string stage) =>
        ByStage.TryGetValue(stage, out var steps) ? steps : [];
}
