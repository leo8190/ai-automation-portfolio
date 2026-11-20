using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace QuoteInvoice.Core;

public static class Pdf
{
    static Pdf() => QuestPDF.Settings.License = LicenseType.Community;

    public static byte[] RenderQuote(Quote q, Company company) =>
        Render(company, "QUOTE", q.Number, $"Issued: {q.IssueDate:MMM d, yyyy}",
            $"Valid until: {q.ValidUntil:MMM d, yyyy}", q.ClientName, q.ClientEmail, q.Phone, q.Address,
            q.Lines, q.Subtotal, q.TaxRate, q.Tax, q.Total,
            "This quote uses only prices from our published sheet. Anything not on the sheet is left unpriced and sent back to you. No work is scheduled until you accept.");

    public static byte[] RenderInvoice(Invoice inv, Company company) =>
        Render(company, "INVOICE", inv.Number, $"Issued: {inv.IssueDate:MMM d, yyyy}",
            $"Due: {inv.DueDate:MMM d, yyyy}", inv.ClientName, inv.ClientEmail, null, inv.Address,
            inv.Lines, inv.Subtotal, inv.TaxRate, inv.Tax, inv.Total,
            $"Payment: ACH / wire to {company.Name}. Please reference {inv.Number}. This invoice was generated from accepted quote {inv.QuoteNumber}.");

    static byte[] Render(
        Company company, string title, string number, string issued, string extra,
        string client, string email, string? phone, string address,
        IReadOnlyList<QuoteLine> lines, decimal subtotal, decimal taxRate, decimal tax, decimal total,
        string footerNote)
    {
        const string accent = "#1F5C4A";
        return Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.Letter);
            page.Margin(40);
            page.DefaultTextStyle(t => t.FontSize(10).FontColor(Colors.Grey.Darken4));

            page.Header().Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text(company.Name).FontSize(16).Bold().FontColor(accent);
                    c.Item().Text(company.Address);
                    c.Item().Text($"{company.Email}  ·  {company.Phone}");
                });
                row.ConstantItem(200).AlignRight().Column(c =>
                {
                    c.Item().Text(title).FontSize(22).Bold().FontColor(accent);
                    c.Item().Text($"# {number}").SemiBold();
                    c.Item().Text(issued);
                    c.Item().Text(extra).Bold();
                });
            });

            page.Content().PaddingVertical(20).Column(col =>
            {
                col.Spacing(14);
                col.Item().Background(Colors.Grey.Lighten4).Padding(10).Column(c =>
                {
                    c.Item().Text(title == "INVOICE" ? "BILL TO" : "PREPARED FOR").FontSize(8).FontColor(Colors.Grey.Darken1);
                    c.Item().Text(client).Bold();
                    c.Item().Text(email);
                    if (!string.IsNullOrWhiteSpace(phone)) c.Item().Text(phone);
                    c.Item().Text(address);
                });

                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(c =>
                    {
                        c.ConstantColumn(70);
                        c.RelativeColumn(5);
                        c.ConstantColumn(50);
                        c.ConstantColumn(50);
                        c.ConstantColumn(70);
                        c.ConstantColumn(70);
                    });
                    t.Header(h =>
                    {
                        foreach (var label in new[] { "SKU", "Description", "Qty", "Unit", "Rate", "Amount" })
                            h.Cell().Background(accent).Padding(5).Text(label).FontColor(Colors.White).SemiBold();
                    });
                    var i = 0;
                    foreach (var l in lines)
                    {
                        var bg = i++ % 2 == 0 ? Colors.White : Colors.Grey.Lighten5;
                        t.Cell().Background(bg).Padding(5).Text(l.Sku);
                        t.Cell().Background(bg).Padding(5).Text(l.Description);
                        t.Cell().Background(bg).Padding(5).AlignRight().Text($"{l.Quantity:0.##}");
                        t.Cell().Background(bg).Padding(5).Text(l.Unit.Replace("_", " "));
                        t.Cell().Background(bg).Padding(5).AlignRight().Text($"${l.UnitPrice:N2}");
                        t.Cell().Background(bg).Padding(5).AlignRight().Text($"${l.Amount:N2}");
                    }
                });

                col.Item().AlignRight().Width(240).Column(c =>
                {
                    void Line(string k, string v, bool bold = false)
                    {
                        c.Item().Row(r =>
                        {
                            var left = r.RelativeItem().Text(k);
                            var right = r.RelativeItem().AlignRight().Text(v);
                            if (bold) { left.Bold().FontSize(12); right.Bold().FontSize(12).FontColor(accent); }
                        });
                    }
                    Line("Subtotal", $"${subtotal:N2}");
                    Line($"Sales tax ({taxRate:P2})", $"${tax:N2}");
                    c.Item().PaddingVertical(4).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
                    Line(title == "INVOICE" ? "Total due (USD)" : "Quote total (USD)", $"${total:N2}", bold: true);
                });

                col.Item().PaddingTop(8).Text(footerNote).FontSize(9).FontColor(Colors.Grey.Darken1);
                col.Item().Text("Fictional demo data — not a real company or offer.").FontSize(8).Italic().FontColor(Colors.Grey.Medium);
            });

            page.Footer().AlignCenter().Text(t =>
            {
                t.Span($"{company.Name}  ·  Page ");
                t.CurrentPageNumber(); t.Span(" of "); t.TotalPages();
            });
        })).GeneratePdf();
    }
}
