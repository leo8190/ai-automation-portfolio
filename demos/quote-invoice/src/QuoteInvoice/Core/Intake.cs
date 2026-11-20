using System.Text.RegularExpressions;

namespace QuoteInvoice.Core;

/// <summary>
/// Deterministic intake: regex and keyword rules stand in for a model step so the demo
/// runs offline and the tests are repeatable. A real build would put the model behind
/// the same IntakeResult contract (and still refuse to invent a price).
/// </summary>
public static class Intake
{
    static readonly Regex Address = new(
        @"\b(\d+\s+[A-Z][A-Za-z0-9.'-]*(?:\s+[A-Z][A-Za-z0-9.']*)*\s+(?:Lane|Ln|Street|St|Court|Ct|Way|Avenue|Ave|Drive|Dr|Road|Rd|Boulevard|Blvd)\b(?:,\s*[A-Za-z .]+,\s*[A-Z]{2}\s+\d{5})?)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    static readonly Regex Phone = new(
        @"\b(?:\+1\s*)?(?:\(?\d{3}\)?[-.\s])\d{3}[-.\s]\d{4}\b|\b\d{3}-\d{4}\b",
        RegexOptions.Compiled);

    static readonly string[] UnknownKeywords = { "solar", "rooftop solar", "photovoltaic", "hvac install", "roof replacement", "asphalt paving" };

    public static IntakeResult Parse(IncomingRequest req)
    {
        var text = req.Subject + "\n" + req.Body;
        var address = Longest(Address.Matches(text));
        var phone = Phone.Match(text) is { Success: true } pm ? pm.Value : null;

        var lines = new List<ExtractedLine>();
        var missing = new List<string>();
        var unknown = new List<string>();

        ExtractMulch(text, lines, missing);
        ExtractMowing(text, lines, missing);
        ExtractFence(text, lines, missing);
        ExtractIrrigation(text, lines, missing);
        ExtractUnknown(text, lines, unknown);

        if (string.IsNullOrWhiteSpace(address) && (lines.Count > 0 || unknown.Count > 0 || LooksLikeServiceRequest(text)))
            missing.Add("site_address");

        IntakeStatus status;
        if (unknown.Count > 0 && lines.All(l => l.Sku is null))
            status = IntakeStatus.UnknownService;
        else if (missing.Count > 0 || lines.Any(l => l.Sku is null || l.Quantity is null))
            status = IntakeStatus.NeedsInfo;
        else if (lines.Count == 0)
        {
            status = IntakeStatus.NeedsInfo;
            missing.Add("service_details");
        }
        else
            status = IntakeStatus.ReadyToQuote;

        var summary = status switch
        {
            IntakeStatus.ReadyToQuote => $"Ready to quote {lines.Count} line(s) at {address}.",
            IntakeStatus.NeedsInfo => "Missing: " + string.Join(", ", missing.Distinct()),
            IntakeStatus.UnknownService => "Not on the price sheet: " + string.Join(", ", unknown),
            _ => ""
        };

        return new IntakeResult(req.Id, status, req.FromName, req.FromEmail, phone, address,
            lines, missing.Distinct().ToList(), unknown, summary);
    }

    static void ExtractMulch(string text, List<ExtractedLine> lines, List<string> missing)
    {
        if (!Regex.IsMatch(text, @"\bmulch\b", RegexOptions.IgnoreCase)) return;
        var qty = Regex.Match(text, @"(\d+(?:\.\d+)?)\s*cubic yards?(?:\s+of mulch)?|\bmulch\b[^\d]{0,40}(\d+(?:\.\d+)?)\s*cubic yards?", RegexOptions.IgnoreCase);
        if (qty.Success)
        {
            var n = decimal.Parse(qty.Groups[1].Success ? qty.Groups[1].Value : qty.Groups[2].Value);
            lines.Add(new ExtractedLine("mulch", "MULCH-CY", n, "cubic_yard", $"{n} cubic yards of mulch"));
        }
        else
        {
            lines.Add(new ExtractedLine("mulch", null, null, "cubic_yard", "mulch (quantity not stated)", "quantity_cubic_yards"));
            missing.Add("mulch_cubic_yards");
        }
    }

    static void ExtractMowing(string text, List<ExtractedLine> lines, List<string> missing)
    {
        if (!Regex.IsMatch(text, @"\b(mowing|mow|lawn)\b", RegexOptions.IgnoreCase)) return;
        var acres = Regex.Match(text, @"(\d+(?:\.\d+)?)\s*acres?", RegexOptions.IgnoreCase);
        if (!acres.Success)
        {
            lines.Add(new ExtractedLine("mowing", null, null, "visit", "weekly mowing (lawn size not stated)", "lawn_size_acres"));
            missing.Add("lawn_size_acres");
            return;
        }
        var size = decimal.Parse(acres.Groups[1].Value);
        var sku = size <= 0.25m ? "MOW-WK" : size <= 0.5m ? "MOW-WK-MED" : "MOW-WK-LG";
        // First month = 4 weekly visits, taken from the price sheet (never a made-up package price).
        lines.Add(new ExtractedLine("mowing", sku, 4, "visit", $"weekly mowing, {size} acre lawn, first 4 visits"));
    }

    static void ExtractFence(string text, List<ExtractedLine> lines, List<string> missing)
    {
        if (!Regex.IsMatch(text, @"\bfence\b|\bgate\b", RegexOptions.IgnoreCase)) return;
        var hasFenceWord = Regex.IsMatch(text, @"\bfence\b", RegexOptions.IgnoreCase);
        var material = Regex.Match(text, @"\b(cedar|vinyl|wood|wood privacy|chain[- ]?link|aluminum)\b", RegexOptions.IgnoreCase);
        var length = Regex.Match(text, @"(\d+(?:\.\d+)?)\s*(?:linear\s+)?(?:feet|foot|ft)\b", RegexOptions.IgnoreCase);

        if (hasFenceWord)
        {
            if (!material.Success) missing.Add("fence_material");
            if (!length.Success) missing.Add("fence_length_ft");

            if (material.Success && length.Success)
            {
                var mat = material.Groups[1].Value.ToLowerInvariant();
                if (mat is "cedar" or "wood" or "wood privacy")
                {
                    var ft = decimal.Parse(length.Groups[1].Value);
                    lines.Add(new ExtractedLine("fence", "FENCE-WD", ft, "linear_foot", $"{ft} linear feet of cedar fence"));
                }
                else
                {
                    lines.Add(new ExtractedLine("fence", null, decimal.Parse(length.Groups[1].Value), "linear_foot",
                        $"{length.Groups[1].Value} ft {material.Groups[1].Value} fence", "material_not_on_price_sheet"));
                    missing.Add("fence_material_on_price_sheet");
                }
            }
            else
            {
                lines.Add(new ExtractedLine("fence", null, length.Success ? decimal.Parse(length.Groups[1].Value) : null,
                    "linear_foot", "fence (details missing)", "incomplete"));
            }
        }

        var gate = Regex.Match(text, @"\b(\d+|one|a|an)\s+cedar\s+gate|\bplus one cedar gate|\bone cedar gate|\ba cedar gate|\bcedar gate\b", RegexOptions.IgnoreCase);
        if (gate.Success)
        {
            lines.Add(new ExtractedLine("gate", "GATE-WD", 1, "each", "one cedar gate"));
        }
        else if (Regex.IsMatch(text, @"\bgate\b", RegexOptions.IgnoreCase) && !hasFenceWord)
        {
            missing.Add("gate_material");
        }
    }

    static void ExtractIrrigation(string text, List<ExtractedLine> lines, List<string> missing)
    {
        if (!Regex.IsMatch(text, @"\b(irrigation|sprinkler|sprinklers)\b", RegexOptions.IgnoreCase)) return;
        if (Regex.IsMatch(text, @"\b(diagnosis|diagnostic|visit|inspect|repair quote|need a diagnosis)\b", RegexOptions.IgnoreCase)
            || Regex.IsMatch(text, @"\bsprinklers?\b", RegexOptions.IgnoreCase))
        {
            lines.Add(new ExtractedLine("irrigation_visit", "IRR-VISIT", 1, "visit", "irrigation diagnosis visit"));
        }
        var heads = Regex.Match(text, @"(\d+)\s+(?:replacement\s+)?heads?\b", RegexOptions.IgnoreCase);
        if (heads.Success)
        {
            var n = decimal.Parse(heads.Groups[1].Value);
            lines.Add(new ExtractedLine("irrigation_head", "IRR-HEAD", n, "each", $"{n} irrigation head replacements"));
        }
    }

    static void ExtractUnknown(string text, List<ExtractedLine> lines, List<string> unknown)
    {
        foreach (var kw in UnknownKeywords)
        {
            if (Regex.IsMatch(text, $@"\b{Regex.Escape(kw)}\b", RegexOptions.IgnoreCase))
            {
                var label = kw == "solar" || kw == "rooftop solar" || kw == "photovoltaic"
                    ? "rooftop solar install"
                    : kw;
                if (!unknown.Contains(label))
                {
                    unknown.Add(label);
                    lines.Add(new ExtractedLine("unknown", null, null, "", label, "not_on_price_sheet"));
                }
                break;
            }
        }
    }

    static bool LooksLikeServiceRequest(string text) =>
        Regex.IsMatch(text, @"\b(quote|bid|estimate|how much)\b", RegexOptions.IgnoreCase);


    static string? Longest(MatchCollection matches)
    {
        if (matches.Count == 0) return null;
        return Collapse(matches.Cast<Match>().Select(m => m.Groups[1].Value).MaxBy(s => s.Length)!);
    }

    static string Collapse(string s) => Regex.Replace(s.Trim(), @"\s+", " ");
}
