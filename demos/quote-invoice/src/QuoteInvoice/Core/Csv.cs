using System.Globalization;
using System.Text;

namespace QuoteInvoice.Core;

/// <summary>Tiny RFC-4180 CSV reader (quoted fields, escaped quotes). No dependencies.</summary>
public static class Csv
{
    public static List<string[]> Read(string text)
    {
        var rows = new List<string[]>();
        var row = new List<string>();
        var field = new StringBuilder();
        var i = 0;
        var inQuotes = false;
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        while (i < text.Length)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i += 2; continue; }
                    inQuotes = false; i++; continue;
                }
                field.Append(c); i++; continue;
            }
            if (c == '"') { inQuotes = true; i++; continue; }
            if (c == ',') { row.Add(field.ToString()); field.Clear(); i++; continue; }
            if (c == '\n')
            {
                row.Add(field.ToString()); field.Clear();
                if (row.Count > 1 || row[0].Length > 0) rows.Add(row.ToArray());
                row = new List<string>(); i++; continue;
            }
            field.Append(c); i++;
        }
        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            if (row.Count > 1 || row[0].Length > 0) rows.Add(row.ToArray());
        }
        return rows;
    }

    public static List<PriceItem> ReadPriceSheet(string text)
    {
        var rows = Read(text);
        if (rows.Count == 0) return new();
        return rows.Skip(1).Select(r => new PriceItem(
            r[0].Trim(), r[1].Trim(), r[2].Trim(),
            decimal.Parse(r[3], CultureInfo.InvariantCulture),
            r.Length > 4 ? r[4].Trim() : "")).ToList();
    }

    public static List<Payment> ReadPayments(string text)
    {
        var rows = Read(text);
        if (rows.Count == 0) return new();
        return rows.Skip(1).Select(r => new Payment(
            r[0].Trim(),
            DateOnly.Parse(r[1], CultureInfo.InvariantCulture),
            decimal.Parse(r[2], CultureInfo.InvariantCulture))).ToList();
    }
}
