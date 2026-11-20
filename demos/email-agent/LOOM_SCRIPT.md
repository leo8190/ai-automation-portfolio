# Loom script (~2 min) — inbox triage agent (FICTIONAL data)

Setup: VS Code with `demos/email-agent` open, terminal on the right, `README.md` showing. Font size large. Use `--no-launch-profile` so the web host does not start. Run `dotnet run --project src/EmailAgent --no-launch-profile -- reset` before recording, or just start with `demo` (it resets `out/` itself).

**0:00–0:15 — What this is**
"Hi, I'm Leonardo. This is a small demo of an inbox agent for a shared mailbox. Every email is classified, a reply is drafted, tasks are pulled out, and nothing is sent until a person approves it. All the data is fictional, and the default engine is a mock model — a real one plugs in behind the same rules."

**0:15–0:45 — Classification**
Run `dotnet run --project src/EmailAgent --no-launch-profile -- demo`, then scroll to Step 1.
"Eight messages come in. Rachel wants a quote for an online ordering system — that's a high-priority sales lead. Mark's dispatch dashboard is down, so support is flagged urgent. There's a billing question, a meeting request, a partnership offer, a complaint from a dentist, and a typical SEO spam pitch, which gets no draft at all."

**0:45–1:15 — Tasks and approval**
Point at Steps 2 and 3, then open `out/tasks.md` or one `.eml` in `out/outbox/`.
"The agent also extracts the work: prepare a quote for Sunset Bakery in the $3,000 to $5,000 range, investigate the dashboard outage today, send a W-9 for invoice INV-2026-10-003, schedule Tom on Thursday or Friday. The seven real messages sit in the approval queue. I approve them, and the replies land in the file outbox. The spam never gets queued."

**1:15–1:40 — Escalate, and don't duplicate**
Scroll to Step 4.
"Sarah's complaint about duplicate appointment reminders is labelled Needs-Human. It still has a draft I can edit, but it doesn't disappear into the pile. Running triage a second time does not create a second draft for the same email."

**1:40–2:00 — Close**
Run `dotnet test`, which shows 22 passing.
"That's 22 tests, including an end-to-end run with a file outbox. In your build this would sit on your shared inbox, with you approving every reply. Happy to walk through the code."

Rules for the recording: don't claim production use, a live mailbox, or a model call unless you actually ran `--engine openai`. Don't show real customer data. Don't mention workflow tools by name. The local mp4 under `videos/email-agent.mp4` is the older Python CLI — record this .NET walkthrough instead.
