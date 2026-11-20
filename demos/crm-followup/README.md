# CRM with automatic follow-up

> **ALL DATA IS FICTIONAL.** Northwind Studio, the six leads, companies, emails and notes in `sample-data/` are invented. No real company or person is involved.

Small working demo of a sales pipeline that does not go quiet: a lead lands in a stage, that stage has its own follow-up cadence, each due reminder is drafted from the lead's context, and nothing is sent until a person approves it. Sequences pause when the lead replies or the deal is won or lost, and they switch when the lead moves stage.

It shows the rules that keep that loop honest:

- **Every active stage has a cadence, closed stages have none.** New: intro today, bump on day 2, case study on day 5, polite break-up on day 10. Contacted: check-in on day 3, a tailored idea on day 7. Proposal: check-in on day 2, a smaller phase 1 on day 6, a last call on day 12. Won and Lost schedule nothing. There is a test for that.
- **Due reminders are drafted only when they are due.** Running the job early is a no-op. Running it twice on the same day does not write a second draft.
- **Nothing is sent until a person approves it.** Drafts sit in an approval queue. Approving writes a real `.eml` to `out/outbox/`. Skipping leaves no file. A person can edit the body before approving.
- **A reply stops the sequence.** Remaining Pending and Ready steps are cancelled. Already-sent mail is left alone.
- **A stage change swaps the cadence.** Leftover steps from the old stage are cancelled, and the new stage's sequence starts from that day. Moving to Won or Lost cancels everything still open.

### What this demo is NOT (being upfront)

- **The default engine is a deterministic mock LLM, not a live model.** `TemplateWriter` stands in for the model step so the demo runs offline with no API key and the tests are repeatable. The same `IFollowUpWriter` contract is implemented by `OpenAiWriter`, which calls the OpenAI Chat Completions API and falls back to the mock writer on any error. There is a test for that fallback.
- **Not connected to Gmail, Outlook, HubSpot or Pipedrive.** `leads.json` and a file outbox stand in for the CRM and SMTP. Those are swap-in integrations, not this repo.
- **Storage is a local JSON state file.** Enough for a demo. A real build would use a database and the customer's own mailbox.
- It is not a workflow-tool recipe. The code is a small .NET 8 console app you can read and run.

## Run it

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (tested with SDK 10 targeting `net8.0`).

```bash
dotnet test          # 23 tests: cadences, drafting, approval, reply, stage change, fallback, end-to-end
dotnet run --project src/CrmFollowup -- demo
# Full walkthrough (resets ./out). Same output as screenshots/demo-output.txt
```

Useful individual commands:

```bash
dotnet run --project src/CrmFollowup -- reset
dotnet run --project src/CrmFollowup -- seed
dotnet run --project src/CrmFollowup -- due --as-of 2026-11-03
dotnet run --project src/CrmFollowup -- queue
dotnet run --project src/CrmFollowup -- approve --all
dotnet run --project src/CrmFollowup -- due --as-of 2026-11-06
dotnet run --project src/CrmFollowup -- reply LEAD-002 --on 2026-11-06
dotnet run --project src/CrmFollowup -- stage LEAD-001 Proposal --on 2026-11-06
dotnet run --project src/CrmFollowup -- status
```

Optional real model (falls back to the mock writer if the call fails):

```bash
export OPENAI_API_KEY=sk-...          # OPENAI_MODEL defaults to gpt-4o-mini
dotnet run --project src/CrmFollowup -- demo --engine openai
```

Outputs land in `out/`: `outbox/*.eml` (open them in any mail client) and `state.json`.

## Sample data (`sample-data/`)

| File | What it is |
|---|---|
| `leads.json` | Six fictional leads at different stages, all opened on 2026-11-03. |
| `company.json` | Sender name (Northwind Studio), from-address and signer. |

The six leads in the demo:

| Id | Name | Company | Stage | What happens |
|---|---|---|---|---|
| LEAD-001 | Sarah Kim | Acme Dental Group | New → Proposal | Intro and bump go out; then she moves to Proposal and the cadence switches. |
| LEAD-002 | Mark Rivera | Brightline Logistics | Contacted | Check-in on day 3; he replies, so the later value email is cancelled. |
| LEAD-003 | Jenna Walsh | Copperleaf Realty | New | Full New cadence: intro, bump, then the case-study email on day 5. |
| LEAD-004 | Tom Becker | Becker HVAC | Proposal | Proposal check-in on day 2. |
| LEAD-005 | Priya Shah | Lumen Yoga Studios | Won | No follow-ups. |
| LEAD-006 | Dan Ortiz | Ortiz Law PLLC | Lost | No follow-ups. |

## Project layout

```
src/CrmFollowup/
  Program.cs          CLI (demo, seed, due, queue, approve, skip, reply, stage, status)
  Core/Models.cs      leads, tasks, outbound mail, pipeline state
  Core/Sequences.cs   cadences per stage (edit offsets here)
  Core/Llm.cs         IFollowUpWriter, TemplateWriter (mock), OpenAiWriter (optional, with fallback)
  Core/Pipeline.cs    scheduling, due reminders, approval queue, reply / stage rules, state.json
  Core/Outbox.cs      file outbox (.eml)
tests/CrmFollowup.Tests/   cadences, writer, approval, reply, stage change, end-to-end
sample-data/               fictional leads and sender
```

The cadences, writer templates and sample leads were reused from the earlier portfolio sketch in `portfolio/crm-followup/`, then rewritten as a console pipeline to match the quote & invoice demo.

## How a real build would extend this

The same contracts hold if you swap the pieces: an LLM behind `IFollowUpWriter`, SMTP or Microsoft Graph behind `IEmailSender`, and HubSpot / Pipedrive (or the email-agent demo) writing new leads into `leads.json`. The rules stay the same: due reminders only when due, no send without approval, stop on reply, switch on stage change.
