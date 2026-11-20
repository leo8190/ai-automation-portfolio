# Quote & invoice automation for a service business

> **ALL DATA IS FICTIONAL.** Harborline Property Services, the customers, addresses, prices and payments in `sample-data/` are invented. No real company or person is involved.

Small working demo of the loop a service business runs every week: a quote request comes in, a priced quote goes out, one follow-up if it sits unsigned, and an accepted quote becomes an invoice whose reminders stop when payment is recorded.

It shows the rules that keep that loop honest:

- **Prices come only from the sheet.** `sample-data/price-sheet.csv` is the only source of rates. If a request asks for something that is not on the sheet (rooftop solar, in the sample), the demo **does not invent a number** and no quote is issued. There is a test for that.
- **Missing details are requested, not guessed.** A fence request with no length, material or address gets a draft asking for those fields, and no quote.
- **Nothing is sent until a person approves it.** Intake drafts, quotes, the one follow-up, invoices and payment reminders all sit in an approval queue. Approving writes a real `.eml` (with the PDF attached) to `out/outbox/`.
- **One quote follow-up, then stop.** Unsigned quotes get a single reminder after 5 days, and only while the quote is still valid.
- **Invoice reminders stop when paid.** Heads-up 3 days before the due date, due today, then 7 days late. A payment in `payments.csv` marks the invoice paid and further reminders are a no-op. Re-issuing the same request returns the same quote number (idempotent).

### What this demo is NOT (being upfront)

- **No LLM calls.** Deterministic rules (regex, keyword matching, the price sheet) stand in for the model steps, so it runs offline with no API key and the tests are repeatable. In a real build the model step sits behind the same `IntakeResult` contract and the same "never invent a price" rule.
- **Not connected to Gmail, Outlook, QuickBooks or Stripe.** `requests.json` and a file outbox stand in for the inbox and SMTP. Those are swap-in integrations, not this repo.
- **Storage is a local JSON state file.** Enough for a demo. A real build would use a database and the customer's own mailbox and accounting export.
- It is not a workflow-tool recipe. The code is a small .NET 8 console app you can read and run.

## Run it

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (tested with SDK 10 targeting `net8.0`).

```bash
dotnet test          # 23 tests: intake, pricing, refusal to invent prices, follow-up, payment, end-to-end
dotnet run --project src/QuoteInvoice -- demo
# Full walkthrough (resets ./out). Same output as screenshots/demo-output.txt
```

Useful individual commands:

```bash
dotnet run --project src/QuoteInvoice -- reset
dotnet run --project src/QuoteInvoice -- intake
dotnet run --project src/QuoteInvoice -- quote --issue-date 2026-11-03
dotnet run --project src/QuoteInvoice -- queue
dotnet run --project src/QuoteInvoice -- approve --all
dotnet run --project src/QuoteInvoice -- followup --as-of 2026-11-08
dotnet run --project src/QuoteInvoice -- accept Q-2026-003 --on 2026-11-05
dotnet run --project src/QuoteInvoice -- remind --as-of 2026-11-17
dotnet run --project src/QuoteInvoice -- status
```

Outputs land in `out/`: `quotes/*.pdf`, `invoices/*.pdf`, `outbox/*.eml` (open them in any mail client), and `state.json`.

## Sample data (`sample-data/`)

| File | What it is |
|---|---|
| `price-sheet.csv` | Published rates. The pricer will not go outside this list. |
| `requests.json` | Five fictional inbox requests (complete, incomplete, unknown service, fence + gate, irrigation). |
| `company.json` | Seller name, tax rate (8.25%), quote validity (14 days), invoice terms (Net 15), follow-up after 5 days. |
| `payments.csv` | A payment against `INV-2026-001` on 2026-11-18, used to show reminders stopping. |

The five requests in the demo:

| Id | From | What happens |
|---|---|---|
| REQ-001 | Maya Chen | 12 yd³ of mulch + weekly mowing on 0.3 acres → quote **Q-2026-001** ($1,164.77) |
| REQ-002 | Jordan Blake | "Quote a fence" with no length, material or address → **needs info**, no quote |
| REQ-003 | Priya Shah | 8 kW rooftop solar, not on the sheet → **unknown service**, no invented price |
| REQ-004 | Luis Ortega | 80 ft cedar fence + one gate → quote **Q-2026-002** ($3,945.71) |
| REQ-005 | Elena Vasquez | Irrigation diagnosis + 4 heads → quote **Q-2026-003** ($281.45), later accepted as **INV-2026-001** |

## Project layout

```
src/QuoteInvoice/
  Program.cs          CLI (demo, intake, quote, queue, approve, followup, accept, remind)
  Core/Intake.cs      request parsing, missing-field and unknown-service flags
  Core/Pricer.cs      price-sheet lookup; unpriced lines block the quote
  Core/Documents.cs   quote and invoice totals, numbering, validity / due dates
  Core/Pdf.cs         QuestPDF templates
  Core/Copy.cs        draft emails (needs-info, quote, one follow-up, invoice, dunning)
  Core/Reminders.cs   quote follow-up rule + invoice dunning schedule
  Core/Pipeline.cs    orchestration, approval queue, idempotent issue, state.json
  Core/Outbox.cs      file outbox (.eml)
  Core/Csv.cs         dependency-free CSV
tests/QuoteInvoice.Tests/   intake, pricing, reminders, approval, end-to-end
sample-data/                fictional requests, price sheet, payment
```

QuestPDF is used under its Community license (free for businesses under $1M annual revenue).

## How a real build would extend this

The same contracts hold if you swap the pieces: an LLM or a mailbox connector behind `Intake.Parse`, SMTP or Microsoft Graph behind `IEmailSender`, and QuickBooks / Stripe webhooks writing into the payments file. The rules stay the same: no invented prices, no send without approval, one quote follow-up, reminders that stop when paid.
