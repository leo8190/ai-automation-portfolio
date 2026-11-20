# Inbox triage agent

> **ALL DATA IS FICTIONAL.** Northwind Studio, the eight inbox messages, senders, companies and invoices in `sample-data/` are invented. No real company or person is involved.

Small working demo of the loop a shared inbox runs every morning: every message is classified, a reply is drafted, actionable tasks are pulled out, and nothing is sent until a person approves it. Complaints and low-confidence cases are flagged for a human. Spam produces no draft and no task.

It shows the rules that keep that loop honest:

- **Every message gets a category, priority and a confidence score.** The eight sample messages classify as sales lead, support, billing, scheduling, partnership, complaint or spam. There is a test that the sample inbox is classified 100% correctly.
- **Tasks are extracted, not guessed from thin air.** A quote request becomes "prepare a quote and timeline"; an urgent outage becomes a due-today investigation; a billing question keeps the invoice number; a meeting request keeps the proposed days. Spam produces no task.
- **Nothing is sent until a person approves it.** Drafts sit in an approval queue. Approving writes a real `.eml` to `out/outbox/`. Running triage twice does not duplicate drafts.
- **Complaints (and low confidence) escalate.** They still get a draft a person can edit, but they are labelled Needs-Human so they don't disappear into the queue.
- **New leads can be pushed to a CRM.** Sales leads and partnerships are written to `out/crm.json` (or posted to the CRM follow-up demo if you pass `--crm-url`).

### What this demo is NOT (being upfront)

- **The default engine is a deterministic mock LLM, not a live model.** Keyword rules stand in for the model step so the demo runs offline with no API key and the tests are repeatable. The same `ILlm` contract is implemented by `OpenAiLlm`, which calls the OpenAI Chat Completions API and falls back to the mock engine on any error. There is a test for that fallback.
- **Not connected to Gmail, Outlook or a production CRM.** `.eml` files plus `inbox.json` and a file outbox stand in for the mailbox. Those are swap-in integrations, not this repo.
- **Storage is a local JSON state file.** Enough for a demo. A real build would use a database and the customer's own mailbox.
- It is not a workflow-tool recipe. The code is a small .NET 8 app you can read and run.

## Run it

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (tested with SDK 10 targeting `net8.0`).

```bash
dotnet test          # 22 tests: classification, entities, tasks, approval, fallback, end-to-end
dotnet run --project src/EmailAgent --no-launch-profile -- demo
# Full walkthrough (resets ./out). Same output as screenshots/demo-output.txt
```

Useful individual commands:

```bash
dotnet run --project src/EmailAgent --no-launch-profile -- reset
dotnet run --project src/EmailAgent --no-launch-profile -- triage
dotnet run --project src/EmailAgent --no-launch-profile -- triage --show-drafts
dotnet run --project src/EmailAgent --no-launch-profile -- queue
dotnet run --project src/EmailAgent --no-launch-profile -- approve --all
dotnet run --project src/EmailAgent --no-launch-profile -- tasks
```

Optional real model (falls back to the mock engine if the call fails):

```bash
export OPENAI_API_KEY=sk-...          # OPENAI_MODEL defaults to gpt-4o-mini
dotnet run --project src/EmailAgent --no-launch-profile -- demo --engine openai
```

Optional web dashboard (this is extra; the CLI is the demo path):

```bash
dotnet run --project src/EmailAgent --no-launch-profile
# http://localhost:5088  ·  POST /api/triage  ·  GET /api/results  ·  GET /api/report
```

To push new leads into the CRM follow-up demo:

```bash
dotnet run --project src/EmailAgent --no-launch-profile -- triage --crm-url http://localhost:5077
```

Outputs land in `out/`: `triage-report.md`, `tasks.md`, `drafts/*.txt` (still marked DRAFT), `outbox/*.eml` after approval, `crm.json` and `state.json`.

## Sample data (`sample-data/`)

| File | What it is |
|---|---|
| `inbox/*.eml` | Six fictional messages (lead, outage, billing, meeting, spam, complaint). |
| `inbox.json` | Two more fictional messages (membership lead, partnership) in JSON form. The loader accepts either format. |

The eight messages in the demo:

| From | Subject | What happens |
|---|---|---|
| Rachel Green, Sunset Bakery | Quote for an online ordering system | **sales_lead**, high priority. Task: prepare a quote ($3,000–$5,000). CRM: create lead. |
| Kevin Lee, Peak Fitness | Automate membership emails | **sales_lead**. Task: prepare a quote. CRM: create lead. |
| Mark Rivera, Brightline Logistics | Dispatch dashboard not loading | **support**, high priority. Task: investigate, due today. |
| Accounts Payable, Copperleaf Realty | Invoice INV-2026-10-003 | **billing**. Task: send W-9 and confirm ACH. |
| Tom Becker, Becker HVAC | Meet Thursday to review the proposal | **scheduling**. Task: schedule a call Thursday or Friday. |
| Maria Lopez, Greenleaf CPA | Partnership idea | **partnership**. Task: review the referral partnership. CRM: create lead. |
| SEO Rankings Pro | Get #1 on Google GUARANTEED | **spam**. No task, no draft, never queued. |
| Dr. Sarah Kim, Acme Dental Group | Patients got duplicate reminders | **complaint**, Needs-Human. Task: incident summary, due today. Draft still waits for approval. |

## Project layout

```
src/EmailAgent/
  Program.cs          CLI (demo, triage, queue, approve, tasks) + optional Minimal API
  Dashboard.cs        small HTML dashboard for the API
  Core/Models.cs      emails, triage, tasks, approval queue
  Core/Llm.cs         ILlm, MockLlm, OpenAiLlm (optional, with fallback), entity extraction
  Core/Tasks.cs       deterministic task extraction from a classified email
  Core/Router.cs      labels, CRM actions, Needs-Human
  Core/Loader.cs      .eml and JSON inbox
  Core/Agent.cs       triage pipeline and report writers
  Core/Pipeline.cs    approval queue, idempotent enqueue, state.json
  Core/Outbox.cs      file outbox (.eml)
tests/EmailAgent.Tests/   classification, entities, routing, tasks, approval, end-to-end
sample-data/              fictional inbox
```

## How a real build would extend this

The same contracts hold if you swap the pieces: an LLM behind `ILlm`, Gmail / Microsoft Graph / IMAP behind `InboxLoader`, SMTP behind `IEmailSender`, and the CRM follow-up demo (or HubSpot / Pipedrive) behind `ICrmSink`. The rules stay the same: classify first, extract tasks, no send without approval, escalate complaints, ignore spam.
