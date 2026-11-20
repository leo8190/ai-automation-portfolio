namespace QuoteInvoice.Core;

public static class Copy
{
    public static OutboundEmail NeedsInfo(IntakeResult intake, Company company)
    {
        var missing = string.Join("\n", intake.MissingFields.Select(PrettyMissing).Select(m => $"- {m}"));
        var body = $"""
            Hi {First(intake.ContactName)},

            Thanks for reaching out to {company.Name}. I can put an itemized quote together as soon as I have a few details:

            {missing}

            Reply with those and I'll send a quote the same day. I only use prices from our sheet — I won't guess a number.

            Best,
            {company.Name}
            {company.Phone}
            """;
        return new OutboundEmail(intake.Email, $"Re: your quote request — a couple of details ({intake.RequestId})", body);
    }

    public static OutboundEmail UnknownService(IntakeResult intake, Company company)
    {
        var items = string.Join(", ", intake.UnknownServices);
        var where = string.IsNullOrWhiteSpace(intake.Address) ? "your site" : intake.Address;
        var body = $"""
            Hi {First(intake.ContactName)},

            Thanks for the note about {items} at {where}. That service isn't on our current price sheet (lawn care, mulch, fencing and irrigation), so I haven't issued a quote or made up a price.

            If you'd like, a person here will look at it and tell you whether we can take it on.

            Best,
            {company.Name}
            {company.Phone}
            """;
        return new OutboundEmail(intake.Email, $"Re: {items} — not on our price sheet ({intake.RequestId})", body);
    }

    public static OutboundEmail QuoteEmail(Quote quote, Company company, string pdfPath)
    {
        var body = $"""
            Hi {First(quote.ClientName)},

            Please find attached quote {quote.Number} for the work at {quote.Address}.

            Total: ${quote.Total:N2} (valid until {quote.ValidUntil:MMMM d, yyyy}).

            Every line comes from our price sheet. Reply "accept" and we'll turn this into an invoice and schedule the work.

            Best,
            {company.Name}
            {company.Phone}
            """;
        return new OutboundEmail(quote.ClientEmail, $"Quote {quote.Number} from {company.Name} (${quote.Total:N2})", body, pdfPath);
    }

    public static OutboundEmail QuoteReminder(Quote quote, Company company, string pdfPath)
    {
        var body = $"""
            Hi {First(quote.ClientName)},

            Just a short follow-up on quote {quote.Number} for {quote.Address} (${quote.Total:N2}). It stays valid until {quote.ValidUntil:MMMM d, yyyy}.

            If you'd like to go ahead, reply "accept". I won't send another reminder after this one.

            Best,
            {company.Name}
            """;
        return new OutboundEmail(quote.ClientEmail, $"Following up on quote {quote.Number} (${quote.Total:N2})", body, pdfPath);
    }

    public static OutboundEmail InvoiceEmail(Invoice inv, Company company, string pdfPath)
    {
        var body = $"""
            Hi {First(inv.ClientName)},

            Thank you for accepting quote {inv.QuoteNumber}. Invoice {inv.Number} is attached for ${inv.Total:N2}, due {inv.DueDate:MMMM d, yyyy}.

            Please reference the invoice number with your payment.

            Best,
            {company.Name}
            """;
        return new OutboundEmail(inv.ClientEmail, $"Invoice {inv.Number} from {company.Name} (${inv.Total:N2})", body, pdfPath);
    }

    public static OutboundEmail InvoiceReminder(Invoice inv, InvoiceReminderStage stage, DateOnly asOf, Company company)
    {
        var (subject, lead) = stage switch
        {
            InvoiceReminderStage.Upcoming => ($"Reminder: invoice {inv.Number} is due {inv.DueDate:MMM d}",
                $"This is a heads-up that invoice {inv.Number} for ${inv.Total:N2} is due on {inv.DueDate:MMMM d, yyyy}."),
            InvoiceReminderStage.DueToday => ($"Invoice {inv.Number} is due today",
                $"Invoice {inv.Number} for ${inv.Total:N2} is due today."),
            InvoiceReminderStage.Overdue => ($"Invoice {inv.Number} is {(asOf.DayNumber - inv.DueDate.DayNumber)} days past due",
                $"Invoice {inv.Number} for ${inv.Total:N2} is {(asOf.DayNumber - inv.DueDate.DayNumber)} days past due."),
            _ => throw new ArgumentOutOfRangeException(nameof(stage))
        };
        var body = $"""
            Hi {First(inv.ClientName)},

            {lead}

            Please reference {inv.Number} with your payment. Reminders stop automatically once payment is recorded.

            Best,
            {company.Name}
            """;
        return new OutboundEmail(inv.ClientEmail, subject, body);
    }

    static string First(string name) => name.Split(' ', 2)[0];

    static string PrettyMissing(string key) => key switch
    {
        "site_address" => "Site address",
        "fence_material" => "Fence material (cedar is on our sheet; other materials need a person to price them)",
        "fence_length_ft" => "Fence length in linear feet",
        "fence_material_on_price_sheet" => "A material that is on our price sheet, or a person to price this one",
        "mulch_cubic_yards" => "Mulch quantity in cubic yards",
        "lawn_size_acres" => "Lawn size in acres (or a lot-size estimate)",
        "gate_material" => "Gate material",
        "service_details" => "Which service you'd like quoted, with quantities",
        _ => key.Replace('_', ' ')
    };
}
