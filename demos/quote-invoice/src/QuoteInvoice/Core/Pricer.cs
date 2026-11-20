namespace QuoteInvoice.Core;

public record PricedQuoteDraft(
    IReadOnlyList<QuoteLine> Lines,
    decimal Subtotal,
    decimal Tax,
    decimal Total,
    IReadOnlyList<string> Unpriced);

public static class Pricer
{
    /// <summary>
    /// Prices only from the sheet. Unknown SKUs are returned in Unpriced — the caller
    /// must not invent a number, and no Quote is issued until Unpriced is empty.
    /// </summary>
    public static PricedQuoteDraft Price(IntakeResult intake, IReadOnlyList<PriceItem> sheet, decimal taxRate)
    {
        var bySku = sheet.ToDictionary(p => p.Sku, StringComparer.OrdinalIgnoreCase);
        var lines = new List<QuoteLine>();
        var unpriced = new List<string>();

        foreach (var line in intake.Lines)
        {
            if (line.Sku is null || line.Quantity is null)
            {
                unpriced.Add(line.Flag ?? line.Kind);
                continue;
            }
            if (!bySku.TryGetValue(line.Sku, out var item))
            {
                unpriced.Add($"sku_not_on_sheet:{line.Sku}");
                continue;
            }
            var qty = line.Quantity.Value;
            var amount = Math.Round(qty * item.UnitPriceUsd, 2, MidpointRounding.AwayFromZero);
            var desc = item.Service;
            if (line.Kind == "mowing") desc = $"{item.Service} — first 4 visits";
            lines.Add(new QuoteLine(item.Sku, desc, qty, item.Unit, item.UnitPriceUsd, amount));
        }

        foreach (var u in intake.UnknownServices)
            if (!unpriced.Contains(u)) unpriced.Add(u);

        var subtotal = lines.Sum(l => l.Amount);
        var tax = Math.Round(subtotal * taxRate, 2, MidpointRounding.AwayFromZero);
        return new PricedQuoteDraft(lines, subtotal, tax, subtotal + tax, unpriced);
    }
}
