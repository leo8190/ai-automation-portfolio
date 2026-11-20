using EmailAgent;
using EmailAgent.Core;

var root = FindRepoRoot();
var dataDir = Environment.GetEnvironmentVariable("DATA_DIR") ?? Path.Combine(root, "sample-data");
var outDir = Environment.GetEnvironmentVariable("OUT_DIR") ?? Path.Combine(root, "out");

if (args.Length > 0 && args[0] is "demo" or "triage" or "queue" or "approve" or "reset" or "tasks" or "status")
{
    Environment.ExitCode = await RunCliAsync(args, root, dataDir, outDir);
    return;
}

var engineName = LlmFactory.Create(Flag(args, "--engine")).Name;
var state = new TriageState();

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://localhost:5088");
builder.Services.AddSingleton(state);
var app = builder.Build();

app.MapGet("/", (TriageState s) => Results.Content(Dashboard.Html(s.Last, s.Engine ?? engineName), "text/html"));
app.MapGet("/api/health", (TriageState s) => new
{
    ok = true,
    engine = s.Engine ?? engineName,
    lastRun = s.Last?.Count ?? 0
});
app.MapPost("/api/triage", async (HttpRequest req, TriageState s, CancellationToken ct) =>
{
    var results = await RunPipelineAsync(req, root, dataDir, outDir, ct);
    s.Last = results.Results;
    s.Engine = results.Engine;
    s.ReportPath = results.ReportPath;
    return Results.Ok(new
    {
        engine = results.Engine,
        count = results.Results.Count,
        drafts = results.Results.Count(r => r.Draft is not null),
        needsHuman = results.Results.Count(r => r.NeedsHuman),
        report = Rel(root, results.ReportPath),
        results = results.Results.Select(TriageAgent.ToDict)
    });
});
app.MapGet("/api/results", (TriageState s) =>
    s.Last is null ? Results.NotFound(new { error = "No triage has been run yet. POST /api/triage first." })
                   : Results.Ok(s.Last.Select(TriageAgent.ToDict)));
app.MapGet("/api/report", async (TriageState s, CancellationToken ct) =>
{
    if (s.ReportPath is null || !File.Exists(s.ReportPath))
        return Results.NotFound(new { error = "No report yet. POST /api/triage first." });
    return Results.Text(await File.ReadAllTextAsync(s.ReportPath, ct), "text/markdown");
});
app.MapGet("/api/drafts/{id}", (string id, TriageState s) =>
{
    var hit = s.Last?.FirstOrDefault(r => string.Equals(r.Email.Id, id, StringComparison.OrdinalIgnoreCase));
    if (hit?.Draft is null) return Results.NotFound();
    return Results.Text($"To: {hit.Email.SenderEmail}\nSubject: Re: {hit.Email.Subject}\nX-Status: DRAFT (not sent - awaiting approval)\n\n{hit.Draft}\n", "text/plain");
});

await app.RunAsync();

static async Task<int> RunCliAsync(string[] args, string root, string dataDir, string outDir)
{
    var cmd = args[0].ToLowerInvariant();
    var engine = LlmFactory.Create(Flag(args, "--engine"));
    var pipeline = new MailboxPipeline(
        new PipelineOptions(dataDir, outDir, "hello@northwind-demo.example"),
        engine,
        new FileOutboxEmailSender(Path.Combine(outDir, "outbox")));

    switch (cmd)
    {
        case "demo":
            await RunDemo(pipeline, root, outDir, args);
            return 0;
        case "reset":
            pipeline.Reset();
            Console.WriteLine("Reset out/.");
            return 0;
        case "triage":
        {
            var showDrafts = args.Contains("--show-drafts");
            var crmUrl = Flag(args, "--crm-url") ?? Environment.GetEnvironmentVariable("CRM_URL");
            var inbox = PositionalInbox(args) ?? dataDir;
            var crmPath = Path.Combine(outDir, "crm.json");
            ICrmSink crm = string.IsNullOrWhiteSpace(crmUrl) ? new JsonCrm(crmPath) : new HttpCrm(crmPath, crmUrl);
            var results = await pipeline.TriageAsync(inbox, crm);
            Console.WriteLine($"Triage agent · engine={engine.Name} · {results.Count} emails\n");
            Print(results, showDrafts);
            Console.WriteLine($"\nReport: {Rel(root, Path.Combine(outDir, "triage-report.md"))}");
            Console.WriteLine($"Tasks:  {Rel(root, Path.Combine(outDir, "tasks.md"))}");
            Console.WriteLine($"Drafts: {Rel(root, Path.Combine(outDir, "drafts"))} (queued for approval, not sent)");
            Console.WriteLine($"Queue:  {pipeline.Pending().Count} draft(s) waiting. Use: approve --all");
            Console.WriteLine($"CRM:    {Rel(root, crmPath)}" + (string.IsNullOrWhiteSpace(crmUrl) ? "" : $" + {crmUrl}"));
            return 0;
        }
        case "queue":
            PrintQueue(pipeline.Pending());
            return 0;
        case "approve":
        {
            var id = Flag(args, "--id");
            var sent = await pipeline.ApproveAsync(args.Contains("--all") || id is null ? null : [id]);
            Console.WriteLine($"Approved and wrote {sent.Count} email(s) to out/outbox.");
            return 0;
        }
        case "tasks":
        case "status":
        {
            var results = await pipeline.TriageAsync();
            Print(results, showDrafts: false);
            Console.WriteLine();
            PrintTasks(results);
            Console.WriteLine();
            PrintQueue(pipeline.Pending());
            return 0;
        }
        default:
            Console.WriteLine($"Unknown command '{cmd}'.");
            return 1;
    }
}

static async Task RunDemo(MailboxPipeline pipeline, string root, string outDir, string[] args)
{
    pipeline.Reset();
    var crm = new JsonCrm(Path.Combine(outDir, "crm.json"));

    Console.WriteLine("=== Northwind Studio — inbox triage demo ===");
    Console.WriteLine("All senders, companies and invoices are fictional. Classification and drafts use a deterministic mock LLM.\n");

    Console.WriteLine("Step 1 — Eight messages hit the inbox");
    var results = await pipeline.TriageAsync(crm: crm);
    Print(results, showDrafts: args.Contains("--show-drafts"));

    Console.WriteLine("\nStep 2 — Tasks extracted from the mail");
    PrintTasks(results);
    Console.WriteLine("  The SEO pitch is spam, so it produces no task and no draft.");

    Console.WriteLine("\nStep 3 — Approval queue (nothing is sent until a person approves)");
    PrintQueue(pipeline.Pending());
    var sent = await pipeline.ApproveAsync();
    Console.WriteLine($"  Approved all {sent.Count} drafts. Replies are in out/outbox/. Spam was never queued.");

    var again = await pipeline.TriageAsync(crm: new JsonCrm(Path.Combine(outDir, "crm.json")));
    Console.WriteLine($"\nStep 4 — Running triage again does not duplicate drafts ({pipeline.Pending().Count} new items in the queue).");
    var complaint = again.Single(r => r.Triage.Category == "complaint");
    Console.WriteLine($"  {complaint.Email.SenderName}'s complaint is flagged Needs-Human and still has a draft a person can approve or rewrite.");

    Console.WriteLine("\nDone. Report: out/triage-report.md. Tasks: out/tasks.md. Approved mail: out/outbox/. History: out/state.json.");
}

static void PrintQueue(List<OutboundMessage> pending)
{
    if (pending.Count == 0) { Console.WriteLine("  (queue empty)"); return; }
    foreach (var m in pending)
        Console.WriteLine($"  {m.Id}  to {m.To,-36}  {m.Subject}");
}

static void PrintTasks(IReadOnlyList<TriageResult> results)
{
    var tasks = results.SelectMany(r => r.Tasks.Select(t => (r, t))).ToList();
    if (tasks.Count == 0) { Console.WriteLine("  (no tasks)"); return; }
    foreach (var (r, t) in tasks)
        Console.WriteLine($"  {t.Kind,-12}  {(t.DueHint ?? "—"),-16}  {t.Title}");
}

static async Task<(IReadOnlyList<TriageResult> Results, string Engine, string ReportPath)> RunPipelineAsync(
    HttpRequest req, string root, string dataDir, string outDir, CancellationToken ct)
{
    string? engineFlag = req.Query.TryGetValue("engine", out var ev) ? ev.ToString() : null;
    string? crmUrl = req.Query.TryGetValue("crmUrl", out var cv) && !string.IsNullOrWhiteSpace(cv)
        ? cv.ToString()
        : Environment.GetEnvironmentVariable("CRM_URL");
    var inbox = req.Query.TryGetValue("inbox", out var iv) && !string.IsNullOrWhiteSpace(iv) ? iv.ToString() : dataDir;

    var engine = LlmFactory.Create(engineFlag);
    var crmPath = Path.Combine(outDir, "crm.json");
    ICrmSink crm = string.IsNullOrWhiteSpace(crmUrl) ? new JsonCrm(crmPath) : new HttpCrm(crmPath, crmUrl);
    var emails = InboxLoader.Load(inbox);
    var results = await TriageAgent.ProcessAsync(emails, engine, crm, ct);
    var report = await TriageAgent.WriteOutputsAsync(results, outDir, ct);
    return (results, engine.Name, report);
}

static void Print(IReadOnlyList<TriageResult> results, bool showDrafts)
{
    var icon = new Dictionary<string, string> { ["high"] = "🔴", ["normal"] = "🟡", ["low"] = "⚪" };
    foreach (var r in results)
    {
        var t = r.Triage;
        var flag = r.NeedsHuman ? "  ⚠ needs human" : "";
        var ico = icon.GetValueOrDefault(t.Priority, " ");
        var name = r.Email.SenderName.Length <= 22 ? r.Email.SenderName : r.Email.SenderName[..22];
        var subject = r.Email.Subject.Length <= 52 ? r.Email.Subject : r.Email.Subject[..52];
        Console.WriteLine($"{ico} {t.Category,-12} {t.Confidence,4:0%}  {name,-22} {subject}");
        Console.WriteLine($"   labels: {string.Join(", ", r.Labels)} | crm: {r.CrmAction ?? "-"} | draft: {(r.Draft is null ? "no" : "yes")}{flag}");
        if (showDrafts && r.Draft is not null)
            Console.WriteLine("   " + r.Draft.Replace("\n", "\n   ") + "\n");
    }
}

static string? Flag(string[] args, string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

static string? PositionalInbox(string[] args)
{
    for (var i = 1; i < args.Length; i++)
    {
        if (args[i].StartsWith("--", StringComparison.Ordinal))
        {
            if (args[i] is not "--show-drafts" && i + 1 < args.Length) i++;
            continue;
        }
        return args[i];
    }
    return null;
}

static string Rel(string root, string path)
{
    try { return Path.GetRelativePath(root, path); }
    catch { return path; }
}

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "sample-data"))) dir = dir.Parent;
    return dir?.FullName ?? Directory.GetCurrentDirectory();
}

sealed class TriageState
{
    public IReadOnlyList<TriageResult>? Last { get; set; }
    public string? Engine { get; set; }
    public string? ReportPath { get; set; }
}
