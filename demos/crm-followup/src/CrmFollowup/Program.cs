using CrmFollowup.Core;

var root = FindRepoRoot();
var data = Path.Combine(root, "sample-data");
var output = Path.Combine(root, "out");
var argsList = args.ToList();
var cmd = argsList.Count > 0 ? argsList[0].ToLowerInvariant() : "demo";
var writer = WriterFactory.Create(Flag(argsList, "--engine"));

var pipeline = new Pipeline(
    new PipelineOptions(data, output),
    writer,
    new FileOutboxEmailSender(Path.Combine(output, "outbox")));

switch (cmd)
{
    case "demo":
        await RunDemo(pipeline, autoApprove: !argsList.Contains("--no-approve"));
        break;
    case "reset":
        pipeline.Reset();
        Console.WriteLine("Reset out/.");
        break;
    case "seed":
        PrintBoard(pipeline.Seed());
        break;
    case "due":
    case "remind":
        pipeline.Seed();
        var asOf = ParseDate(argsList, "--as-of") ?? new DateOnly(2026, 11, 3);
        var drafted = await pipeline.ProcessDueAsync(asOf);
        Console.WriteLine(drafted.Count == 0
            ? $"No follow-ups due on {asOf:yyyy-MM-dd} (already drafted, cancelled, or not yet due)."
            : string.Join("\n", drafted.Select(t => $"  Queued {t.Id}  {t.StepLabel,-22}  {pipeline.RequireLead(t.LeadId).Name}  due {t.DueOn:yyyy-MM-dd}")));
        break;
    case "queue":
        PrintQueue(pipeline.Pending());
        break;
    case "approve":
        var ids = AfterFlag(argsList, "--id");
        var sent = await pipeline.ApproveAsync(argsList.Contains("--all") || ids.Count == 0 ? null : ids);
        Console.WriteLine($"Approved and wrote {sent.Count} email(s) to out/outbox.");
        break;
    case "skip":
        var skipId = argsList.ElementAtOrDefault(1) ?? throw new ArgumentException("Usage: skip T-001");
        var skipped = pipeline.Skip(skipId);
        Console.WriteLine($"Skipped {skipped.Id} ({skipped.StepLabel}).");
        break;
    case "reply":
        var replyId = argsList.ElementAtOrDefault(1) ?? throw new ArgumentException("Usage: reply LEAD-002 [--on 2026-11-06]");
        var replied = pipeline.MarkReplied(replyId, ParseDate(argsList, "--on") ?? new DateOnly(2026, 11, 6));
        Console.WriteLine($"{replied.Name} marked as replied. Open follow-ups cancelled.");
        break;
    case "stage":
        var leadId = argsList.ElementAtOrDefault(1) ?? throw new ArgumentException("Usage: stage LEAD-001 Proposal [--on 2026-11-06]");
        var stage = argsList.ElementAtOrDefault(2) ?? throw new ArgumentException("Usage: stage LEAD-001 Proposal [--on 2026-11-06]");
        var moved = pipeline.MoveStage(leadId, stage, ParseDate(argsList, "--on") ?? new DateOnly(2026, 11, 6));
        Console.WriteLine($"{moved.Name} is now in {moved.Stage}.");
        break;
    case "status":
        pipeline.Seed();
        PrintBoard(pipeline.State);
        PrintQueue(pipeline.Pending());
        break;
    default:
        Console.WriteLine($"Unknown command '{cmd}'. Try: demo, reset, seed, due, queue, approve, skip, reply, stage, status.");
        Environment.ExitCode = 1;
        break;
}

static async Task RunDemo(Pipeline pipeline, bool autoApprove)
{
    pipeline.Reset();
    Console.WriteLine("=== Northwind Studio — CRM follow-up demo ===");
    Console.WriteLine("All names, companies and notes are fictional. Drafts come from a deterministic mock LLM.\n");

    Console.WriteLine("Step 1 — Six leads in the pipeline");
    var state = pipeline.Seed();
    PrintBoard(state);
    Console.WriteLine("  New leads get a 4-touch sequence (intro today, bump day 2, case study day 5, break-up day 10).");
    Console.WriteLine("  Contacted and Proposal have their own cadences. Won and Lost get none.");

    Console.WriteLine("\nStep 2 — Due reminders on 2026-11-03 (opening day)");
    var day0 = await pipeline.ProcessDueAsync(new DateOnly(2026, 11, 3));
    foreach (var t in day0)
        Console.WriteLine($"  {t.Id}  {pipeline.RequireLead(t.LeadId).Name,-14}  {t.StepLabel}  (drafted by {t.DraftedBy})");
    Console.WriteLine("  Only the two New leads have a day-0 intro. Mark, Tom, Priya and Dan are not due today.");

    Console.WriteLine("\nStep 3 — Approval queue (nothing is sent until a person approves)");
    PrintQueue(pipeline.Pending());
    if (autoApprove)
    {
        var sent = await pipeline.ApproveAsync();
        Console.WriteLine($"  Approved all {sent.Count} drafts. Emails are in out/outbox/.");
    }

    Console.WriteLine("\nStep 4 — Three days later, the next due reminders are drafted");
    var day3 = await pipeline.ProcessDueAsync(new DateOnly(2026, 11, 6));
    foreach (var t in day3)
        Console.WriteLine($"  {t.Id}  {pipeline.RequireLead(t.LeadId).Name,-14}  {t.StepLabel,-22}  due {t.DueOn:yyyy-MM-dd}");
    if (autoApprove) await pipeline.ApproveAsync();

    Console.WriteLine("\nStep 5 — Mark replies; Sarah moves to Proposal. Sequences stop or switch.");
    pipeline.MarkReplied("LEAD-002", new DateOnly(2026, 11, 6));
    Console.WriteLine("  Mark Rivera replied — his remaining Contacted follow-up is cancelled.");
    pipeline.MoveStage("LEAD-001", Stages.Proposal, new DateOnly(2026, 11, 6));
    Console.WriteLine("  Sarah Kim moves to Proposal — leftover New steps are cancelled, a Proposal cadence starts from today.");
    var again = await pipeline.ProcessDueAsync(new DateOnly(2026, 11, 6));
    Console.WriteLine($"  Re-running due on 2026-11-06 drafts {again.Count} new message(s) (Sarah's proposal check-in is day 2, not yet due).");

    Console.WriteLine("\nStep 6 — Due again on 2026-11-08, then a second pass is a no-op");
    var day5 = await pipeline.ProcessDueAsync(new DateOnly(2026, 11, 8));
    foreach (var t in day5)
        Console.WriteLine($"  {t.Id}  {pipeline.RequireLead(t.LeadId).Name,-14}  {t.StepLabel}");
    Console.WriteLine("  Jenna's case-study email (New, day 5) and Sarah's proposal check-in (day 2 of the new cadence) come due.");
    if (autoApprove) await pipeline.ApproveAsync();
    var repeat = await pipeline.ProcessDueAsync(new DateOnly(2026, 11, 8));
    Console.WriteLine($"  Same day, second pass: {repeat.Count} draft(s). Already-drafted steps are not written again.");

    var priya = pipeline.RequireLead("LEAD-005");
    var dan = pipeline.RequireLead("LEAD-006");
    var priyaOpen = pipeline.State.Tasks.Count(t => t.LeadId == priya.Id && t.Status is not FollowUpStatus.Cancelled);
    var danOpen = pipeline.State.Tasks.Count(t => t.LeadId == dan.Id && t.Status is not FollowUpStatus.Cancelled);
    Console.WriteLine($"  Priya (Won) open tasks: {priyaOpen}. Dan (Lost) open tasks: {danOpen}.");

    Console.WriteLine("\nDone. Approved mail is in out/outbox; history is in out/state.json.");
}

static void PrintBoard(AppState state)
{
    Console.WriteLine($"  {"Id",-10} {"Name",-14} {"Company",-24} {"Stage",-11} {"Value",8}  Source");
    foreach (var lead in state.Leads)
        Console.WriteLine($"  {lead.Id,-10} {lead.Name,-14} {lead.CompanyName,-24} {lead.Stage,-11} {lead.ValueUsd,8:N0}  {lead.Source}");
}

static void PrintQueue(List<OutboundMessage> pending)
{
    if (pending.Count == 0) { Console.WriteLine("  (queue empty)"); return; }
    foreach (var m in pending)
        Console.WriteLine($"  {m.Id}  {m.TaskId}  to {m.To,-32}  {m.Subject}");
}

static DateOnly? ParseDate(List<string> args, string flag)
{
    var i = args.IndexOf(flag);
    return i >= 0 && i + 1 < args.Count ? DateOnly.Parse(args[i + 1]) : null;
}

static List<string> AfterFlag(List<string> args, string flag)
{
    var i = args.IndexOf(flag);
    return i >= 0 && i + 1 < args.Count ? [args[i + 1]] : [];
}

static string? Flag(List<string> args, string flag)
{
    var i = args.IndexOf(flag);
    return i >= 0 && i + 1 < args.Count ? args[i + 1] : null;
}

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "sample-data"))) dir = dir.Parent;
    return dir?.FullName ?? Directory.GetCurrentDirectory();
}
