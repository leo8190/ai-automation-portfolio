using QuoteInvoice.Core;

var root = FindRepoRoot();
var data = Path.Combine(root, "sample-data");
var output = Path.Combine(root, "out");
var argsList = args.ToList();
var cmd = argsList.Count > 0 ? argsList[0].ToLowerInvariant() : "demo";

var pipeline = new Pipeline(
    new PipelineOptions(data, output),
    new FileOutboxEmailSender(Path.Combine(output, "outbox"), "quotes@harborline-demo.example"));

switch (cmd)
{
    case "demo":
        await RunDemo(pipeline, autoApprove: !argsList.Contains("--no-approve"));
        break;
    case "intake":
        PrintIntake(pipeline.RunIntake());
        break;
    case "quote":
        PrintIntake(pipeline.RunIntake());
        foreach (var q in pipeline.IssueReadyQuotes(ParseDate(argsList, "--issue-date") ?? new DateOnly(2026, 11, 3)))
            Console.WriteLine($"Quoted {q.Quote.Number} for {q.Quote.ClientName}: ${q.Quote.Total:N2} -> {q.PdfPath}");
        break;
    case "queue":
        PrintQueue(pipeline.Pending());
        break;
    case "approve":
        var ids = AfterFlag(argsList, "--id");
        var sent = await pipeline.ApproveAsync(argsList.Contains("--all") || ids.Count == 0 ? null : ids);
        Console.WriteLine($"Approved and wrote {sent.Count} email(s) to out/outbox.");
        break;
    case "followup":
        var asOf = ParseDate(argsList, "--as-of") ?? new DateOnly(2026, 11, 8);
        var reminded = pipeline.RunQuoteFollowUps(asOf);
        Console.WriteLine(reminded.Count == 0
            ? $"No quote follow-ups on {asOf:yyyy-MM-dd} (already sent, accepted, or not yet due)."
            : string.Join("\n", reminded.Select(r => $"Queued one follow-up for {r.Quote.Number}.")));
        break;
    case "accept":
        var number = argsList.ElementAtOrDefault(1) ?? throw new ArgumentException("Usage: accept Q-2026-003 [--on 2026-11-05]");
        var on = ParseDate(argsList, "--on") ?? new DateOnly(2026, 11, 5);
        var inv = pipeline.Accept(number, on);
        Console.WriteLine($"Accepted {number} -> {inv.Invoice.Number} for ${inv.Invoice.Total:N2}, due {inv.Invoice.DueDate:yyyy-MM-dd}.");
        break;
    case "remind":
        var when = ParseDate(argsList, "--as-of") ?? DateOnly.FromDateTime(DateTime.Today);
        var reminders = pipeline.RunInvoiceReminders(when);
        Console.WriteLine(reminders.Count == 0
            ? $"No invoice reminders on {when:yyyy-MM-dd}."
            : string.Join("\n", reminders.Select(r => $"{r.rec.Invoice.Number}: {r.stage}")));
        break;
    case "status":
        PrintStatus(pipeline.Load());
        break;
    case "reset":
        pipeline.Reset();
        Console.WriteLine("Cleared out/.");
        break;
    default:
        Console.WriteLine("Commands: demo | intake | quote | queue | approve [--all|--id MSG-001] | followup --as-of YYYY-MM-DD | accept Q-… --on YYYY-MM-DD | remind --as-of YYYY-MM-DD | status | reset");
        return 1;
}
return 0;

static async Task RunDemo(Pipeline pipeline, bool autoApprove)
{
    pipeline.Reset();
    Console.WriteLine("=== Harborline Property Services — quote & invoice demo ===");
    Console.WriteLine("All names, addresses and jobs are fictional. Prices come only from sample-data/price-sheet.csv.\n");

    Console.WriteLine("Step 1 — Intake five requests");
    var state = pipeline.RunIntake();
    PrintIntake(state);

    Console.WriteLine("\nStep 2 — Ready requests become itemized quotes (PDF)");
    var quotes = pipeline.IssueReadyQuotes(new DateOnly(2026, 11, 3));
    foreach (var q in quotes)
        Console.WriteLine($"  {q.Quote.Number}  {q.Quote.ClientName,-16}  {q.Quote.Address,-42}  ${q.Quote.Total,8:N2}  valid until {q.Quote.ValidUntil:yyyy-MM-dd}");

    Console.WriteLine("\nStep 3 — Approval queue (nothing is sent until a person approves)");
    PrintQueue(pipeline.Pending());
    if (autoApprove)
    {
        var sent = await pipeline.ApproveAsync();
        Console.WriteLine($"  Approved all {sent.Count} drafts. Emails (with PDFs where relevant) are in out/outbox/.");
    }

    Console.WriteLine("\nStep 4 — Elena accepts Q-2026-003; it becomes an invoice");
    var inv = pipeline.Accept("Q-2026-003", new DateOnly(2026, 11, 5));
    Console.WriteLine($"  {inv.Invoice.Number} for ${inv.Invoice.Total:N2}, due {inv.Invoice.DueDate:yyyy-MM-dd}, PDF at {inv.PdfPath}");
    if (autoApprove) await pipeline.ApproveAsync();

    Console.WriteLine("\nStep 5 — One follow-up on the unsigned quotes, then it stops");
    var first = pipeline.RunQuoteFollowUps(new DateOnly(2026, 11, 8));
    Console.WriteLine($"  On 2026-11-08: {first.Count} follow-up(s) queued ({string.Join(", ", first.Select(r => r.Quote.Number))}). Elena already accepted, so she is skipped.");
    if (autoApprove) await pipeline.ApproveAsync();
    var second = pipeline.RunQuoteFollowUps(new DateOnly(2026, 11, 12));
    Console.WriteLine($"  On 2026-11-12: {second.Count} follow-up(s). The rule is one reminder, then stop.");

    Console.WriteLine("\nStep 6 — Invoice reminders stop when payment is recorded");
    var upcoming = pipeline.RunInvoiceReminders(new DateOnly(2026, 11, 17));
    Console.WriteLine($"  On 2026-11-17 (3 days before due): {Describe(upcoming)}");
    if (autoApprove) await pipeline.ApproveAsync();
    var afterPay = pipeline.RunInvoiceReminders(new DateOnly(2026, 11, 20));
    Console.WriteLine($"  On 2026-11-20 (due date, payment was recorded on 11-18): {Describe(afterPay)}");
    var paid = pipeline.Load().Invoices.Single();
    Console.WriteLine($"  {paid.Invoice.Number} paid={paid.Paid}, last reminder={paid.LastReminderSent}.");

    Console.WriteLine("\nDone. PDFs are in out/quotes and out/invoices; approved mail is in out/outbox; history is in out/state.json.");
}

static string Describe(List<(InvoiceRecord rec, InvoiceReminderStage stage)> items) =>
    items.Count == 0 ? "no reminder sent" : string.Join(", ", items.Select(i => $"{i.rec.Invoice.Number} {i.stage}"));

static void PrintIntake(AppState state)
{
    Console.WriteLine($"  {"Id",-8} {"Client",-16} {"Status",-16} Detail");
    foreach (var i in state.Intakes)
        Console.WriteLine($"  {i.RequestId,-8} {i.ContactName,-16} {i.Status,-16} {i.Summary}");
}

static void PrintQueue(List<OutboundMessage> pending)
{
    if (pending.Count == 0) { Console.WriteLine("  (queue empty)"); return; }
    foreach (var m in pending)
        Console.WriteLine($"  {m.Id}  {m.Kind,-16}  to {m.To,-32}  {m.Subject}");
}

static void PrintStatus(AppState state)
{
    PrintIntake(state);
    Console.WriteLine();
    foreach (var q in state.Quotes)
        Console.WriteLine($"  {q.Quote.Number} {q.Status,-10} ${q.Quote.Total:N2} reminderSent={q.ReminderSent}");
    foreach (var i in state.Invoices)
        Console.WriteLine($"  {i.Invoice.Number} from {i.Invoice.QuoteNumber} paid={i.Paid} last={i.LastReminderSent} ${i.Invoice.Total:N2}");
    PrintQueue(state.Outbox.Where(m => m.Status == OutboundStatus.PendingApproval).ToList());
}

static DateOnly? ParseDate(List<string> args, string flag)
{
    var i = args.IndexOf(flag);
    return i >= 0 && i + 1 < args.Count ? DateOnly.Parse(args[i + 1]) : null;
}

static List<string> AfterFlag(List<string> args, string flag)
{
    var i = args.IndexOf(flag);
    return i >= 0 && i + 1 < args.Count ? new List<string> { args[i + 1] } : new();
}

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "sample-data"))) dir = dir.Parent;
    return dir?.FullName ?? Directory.GetCurrentDirectory();
}
