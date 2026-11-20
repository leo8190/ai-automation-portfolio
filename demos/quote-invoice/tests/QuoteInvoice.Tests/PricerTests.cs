using QuoteInvoice.Core;

public class PricerTests
{
    static readonly List<PriceItem> Sheet = Csv.ReadPriceSheet(File.ReadAllText(Path.Combine(Repo.Data, "price-sheet.csv")));
    const decimal Tax = 0.0825m;

    [Fact]
    public void Mulch_and_mowing_totals_come_from_the_sheet()
    {
        var req = Repo.Request("REQ-001");
        var priced = Pricer.Price(Intake.Parse(req), Sheet, Tax);
        Assert.Empty(priced.Unpriced);
        Assert.Equal(1076.00m, priced.Subtotal);
        Assert.Equal(88.77m, priced.Tax);
        Assert.Equal(1164.77m, priced.Total);
        Assert.Equal(68.00m, priced.Lines.Single(l => l.Sku == "MULCH-CY").UnitPrice);
        Assert.Equal(65.00m, priced.Lines.Single(l => l.Sku == "MOW-WK-MED").UnitPrice);
    }

    [Fact]
    public void Fence_and_gate_totals()
    {
        var priced = Pricer.Price(Intake.Parse(Repo.Request("REQ-004")), Sheet, Tax);
        Assert.Equal(3645.00m, priced.Subtotal);
        Assert.Equal(300.71m, priced.Tax);
        Assert.Equal(3945.71m, priced.Total);
    }

    [Fact]
    public void Irrigation_totals_match_the_recorded_payment()
    {
        var priced = Pricer.Price(Intake.Parse(Repo.Request("REQ-005")), Sheet, Tax);
        Assert.Equal(260.00m, priced.Subtotal);
        Assert.Equal(21.45m, priced.Tax);
        Assert.Equal(281.45m, priced.Total);
    }

    [Fact]
    public void Unknown_sku_is_unpriced_and_never_gets_a_made_up_rate()
    {
        var intake = new IntakeResult("REQ-X", IntakeStatus.ReadyToQuote, "A", "a@x.com", null, "1 Main St",
            new[] { new ExtractedLine("custom", "NOT-A-SKU", 3, "each", "made up") },
            Array.Empty<string>(), Array.Empty<string>(), "x");
        var priced = Pricer.Price(intake, Sheet, Tax);
        Assert.Empty(priced.Lines);
        Assert.Equal(0m, priced.Total);
        Assert.Contains(priced.Unpriced, u => u.Contains("NOT-A-SKU"));
    }

    [Fact]
    public void BuildQuote_refuses_unpriced_lines()
    {
        var company = Repo.Company();
        var intake = Intake.Parse(Repo.Request("REQ-003"));
        var priced = Pricer.Price(intake, Sheet, company.TaxRate);
        Assert.NotEmpty(priced.Unpriced);
        Assert.Throws<InvalidOperationException>(() =>
            Documents.BuildQuote(intake, priced, company, new DateOnly(2026, 11, 3), 1));
    }

    [Fact]
    public void Needs_info_intake_cannot_be_quoted()
    {
        var company = Repo.Company();
        var intake = Intake.Parse(Repo.Request("REQ-002"));
        var priced = Pricer.Price(intake, Sheet, company.TaxRate);
        Assert.Throws<InvalidOperationException>(() =>
            Documents.BuildQuote(intake, priced, company, new DateOnly(2026, 11, 3), 1));
    }
}
