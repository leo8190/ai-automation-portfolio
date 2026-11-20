# Loom script (~2 min) — quote & invoice automation (FICTIONAL data)

Setup: VS Code with `demos/quote-invoice` open, terminal on the right, `README.md` showing. Font size large. Run `dotnet run --project src/QuoteInvoice -- reset` before recording, or just start with `demo` (it resets `out/` itself).

**0:00–0:15 — What this is**
"Hi, I'm Leonardo. This is a small demo of quote and invoice automation for a service business. A request comes in, a priced quote goes out, there's one follow-up if it sits there, and an accepted quote becomes an invoice. All the data is fictional, and it doesn't call a model — those steps plug in behind the same rules."

**0:15–0:40 — Intake, no guessing**
Run `dotnet run --project src/QuoteInvoice -- demo`, then scroll to Step 1.
"Five requests hit the inbox. Maya's mulch and mowing job has every detail, so it's ready to quote. Jordan asked for a fence but didn't give a length, a material or an address, so it goes to needs-info — I don't invent those. Priya asked for rooftop solar, which is not on the price sheet, so there's no quote and no made-up number."

**0:40–1:05 — Quotes from the sheet only**
Point at Step 2 and open `out/quotes/Q-2026-001.pdf`.
"The ready jobs become itemized PDFs. Every line is a SKU from the price sheet — 12 yards of mulch at $68, four weekly mows at $65, tax at 8.25%. If a SKU isn't on the sheet, the code refuses to issue the quote. There's a test for that."

**1:05–1:30 — Approval, then an accepted quote becomes an invoice**
Scroll to Steps 3 and 4, open `out/invoices/INV-2026-001.pdf` if it's on screen.
"Nothing is sent until a person approves the drafts. Elena accepts Q-2026-003, and it becomes invoice INV-2026-001, due in 15 days. The PDF is the quote lines, now billed."

**1:30–1:50 — One follow-up, and invoice reminders stop when paid**
Scroll to Steps 5 and 6.
"The two unsigned quotes get one follow-up five days later. Elena is skipped because she already accepted. Running the job again sends nothing. On the invoice side, a heads-up goes out three days before due. Payment is recorded on the 18th, so on the due date there is no reminder."

**1:50–2:00 — Close**
Run `dotnet test`, which shows 23 passing.
"That's 23 tests, including an end-to-end run with real PDFs and a file outbox. In your build this would sit on your inbox and your price sheet, with you approving every message. Happy to walk through the code."

Rules for the recording: don't claim production use, a live mailbox, QuickBooks, or a model call. Don't show real customer data. Don't mention workflow tools by name.
