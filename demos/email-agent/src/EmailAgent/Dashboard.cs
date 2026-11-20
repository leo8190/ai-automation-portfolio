using System.Net;
using System.Text;
using EmailAgent.Core;

namespace EmailAgent;

static class Dashboard
{
    public static string Html(IReadOnlyList<TriageResult>? results, string engine)
    {
        var rows = new StringBuilder();
        if (results is null || results.Count == 0)
        {
            rows.Append("<tr><td colspan='6' class='empty'>No triage yet. Click “Run triage” or POST /api/triage.</td></tr>");
        }
        else
        {
            var order = new Dictionary<string, int> { ["high"] = 0, ["normal"] = 1, ["low"] = 2 };
            foreach (var r in results.OrderBy(r => order.GetValueOrDefault(r.Triage.Priority, 1)))
            {
                var flag = r.NeedsHuman ? "<span class='tag warn'>needs human</span>" : "";
                var draft = r.Draft is null
                    ? "<span class='muted'>none</span>"
                    : $"<details><summary>draft</summary><pre>{Enc(r.Draft)}</pre></details>";
                rows.Append($"""
                <tr class="pri-{Enc(r.Triage.Priority)}">
                  <td><span class="pri">{Enc(r.Triage.Priority)}</span></td>
                  <td>{Enc(r.Triage.Category)} <span class="muted">{r.Triage.Confidence:0%}</span></td>
                  <td>{Enc(r.Email.SenderName)}<br><span class="muted">{Enc(r.Email.SenderEmail)}</span></td>
                  <td>{Enc(r.Email.Subject)} {flag}</td>
                  <td>{Enc(string.Join(", ", r.Labels))}</td>
                  <td>{draft}</td>
                </tr>
                """);
            }
        }

        var count = results?.Count ?? 0;
        var drafts = results?.Count(r => r.Draft is not null) ?? 0;
        var human = results?.Count(r => r.NeedsHuman) ?? 0;

        return $$"""
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <title>Inbox Triage Agent</title>
          <style>
            :root { --bg:#f6f3ee; --ink:#1c1916; --muted:#6b635b; --line:#e4ddd4; --high:#b42318; --ok:#17663a; --card:#fff; }
            * { box-sizing: border-box; }
            body { margin:0; font: 15px/1.45 "IBM Plex Sans", "Segoe UI", sans-serif; background:var(--bg); color:var(--ink); }
            header { padding:28px 32px 16px; }
            h1 { margin:0 0 6px; font-size:28px; letter-spacing:-0.03em; }
            p.lead { margin:0; color:var(--muted); max-width:44rem; }
            .bar { display:flex; gap:12px; align-items:center; flex-wrap:wrap; padding:0 32px 20px; }
            button { background:var(--ink); color:#fff; border:0; border-radius:8px; padding:10px 16px; font:inherit; cursor:pointer; }
            button:hover { opacity:.9; }
            .stat { background:var(--card); border:1px solid var(--line); border-radius:8px; padding:8px 12px; color:var(--muted); }
            table { width:calc(100% - 64px); margin:0 32px 40px; border-collapse:collapse; background:var(--card); border:1px solid var(--line); border-radius:12px; overflow:hidden; }
            th, td { text-align:left; padding:12px 14px; border-bottom:1px solid var(--line); vertical-align:top; }
            th { font-size:12px; text-transform:uppercase; letter-spacing:.06em; color:var(--muted); background:#faf8f5; }
            .pri { font-weight:600; }
            .pri-high .pri { color:var(--high); }
            .pri-low .pri { color:var(--muted); }
            .muted { color:var(--muted); font-size:13px; }
            .tag.warn { display:inline-block; background:#fde8e6; color:var(--high); border-radius:999px; padding:1px 8px; font-size:12px; margin-left:6px; }
            .empty { color:var(--muted); text-align:center; padding:32px; }
            details pre { white-space:pre-wrap; background:#faf8f5; padding:10px; border-radius:8px; font-size:13px; }
            footer { padding:0 32px 32px; color:var(--muted); font-size:13px; }
            code { font-family: ui-monospace, SFMono-Regular, Menlo, monospace; font-size:12px; }
          </style>
        </head>
        <body>
          <header>
            <h1>Inbox Triage Agent</h1>
            <p class="lead">Every incoming email is classified, labeled, and given a reply draft. Nothing is sent until a person approves it. Engine: <code>{{Enc(engine)}}</code>.</p>
          </header>
          <div class="bar">
            <button onclick="run()">Run triage</button>
            <span class="stat">{{count}} emails</span>
            <span class="stat">{{drafts}} drafts</span>
            <span class="stat">{{human}} need a human</span>
          </div>
          <table>
            <thead><tr><th>Pri</th><th>Category</th><th>From</th><th>Subject</th><th>Labels</th><th>Reply</th></tr></thead>
            <tbody>{{rows}}</tbody>
          </table>
          <footer>
            API: <code>GET /api/health</code> · <code>POST /api/triage</code> · <code>GET /api/results</code> · <code>GET /api/report</code> · <code>GET /api/drafts/{id}</code><br>
            Drafts are written to <code>out/drafts</code> and marked <code>X-Status: DRAFT</code>. Optional: set <code>CRM_URL</code> to push new leads into the CRM follow-up demo.
          </footer>
          <script>
            async function run(){
              const btn = document.querySelector('button');
              btn.disabled = true; btn.textContent = 'Running…';
              await fetch('/api/triage', { method:'POST' });
              location.reload();
            }
          </script>
        </body>
        </html>
        """;
    }

    static string Enc(string? s) => WebUtility.HtmlEncode(s ?? "");
}
