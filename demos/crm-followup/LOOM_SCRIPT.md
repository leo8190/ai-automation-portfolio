# Loom script (~2 min) — CRM follow-up (FICTIONAL data)

Setup: VS Code with `demos/crm-followup` open, terminal on the right, `README.md` showing. Font size large. Run `dotnet run --project src/CrmFollowup -- reset` before recording, or just start with `demo` (it resets `out/` itself).

**0:00–0:15 — What this is**
"Hi, I'm Leonardo. This is a small demo of a CRM that follows up for you. Every pipeline stage has its own cadence, due reminders are drafted from the lead's notes, and nothing is sent until a person approves it. All the data is fictional, and the default writer is a mock model — a real one plugs in behind the same rules."

**0:15–0:40 — The board and the cadences**
Run `dotnet run --project src/CrmFollowup -- demo`, then scroll to Step 1.
"Six fictional leads. Sarah and Jenna are New, so they get a four-touch sequence: intro today, a bump on day two, a case study on day five, a polite break-up on day ten. Mark is already Contacted, Tom has a proposal out. Priya is Won and Dan is Lost, so they get no follow-ups at all."

**0:40–1:05 — Due reminders and approval**
Point at Steps 2 and 3, then open `out/outbox/M-001-Quick-intro---Acme-Dental-Group.eml`.
"On opening day only the two intros are due. Sarah's draft mentions online booking and SMS reminders for three clinics — that's her notes, not a generic template dump. Both sit in the approval queue. Nothing hits the outbox until I approve them."

**1:05–1:30 — Time passes, the next reminders appear**
Scroll to Step 4.
"Fast-forward three days. Sarah and Jenna get their bump, Mark gets his check-in, Tom gets a proposal check-in. Nobody had to remember any of this. The same job on a day with nothing due would have drafted nothing."

**1:30–1:50 — A reply stops the sequence; a stage change switches it**
Scroll to Steps 5 and 6.
"Mark replies, so his remaining Contacted email is cancelled — we don't send a 'just checking in' to someone who already answered. Sarah moves to Proposal: leftover New steps are cancelled, and a Proposal cadence starts from that day. Two days later her proposal check-in comes due, alongside Jenna's case study. Running the job again the same day is a no-op."

**1:50–2:00 — Close**
Run `dotnet test`, which shows 23 passing.
"That's 23 tests, including an end-to-end run with a file outbox. In your build this would sit on your pipeline, with you approving every message. Happy to walk through the code."

Rules for the recording: don't claim production use, a live mailbox, HubSpot, or a model call unless you actually ran `--engine openai`. Don't show real customer data. Don't mention workflow tools by name.
