using QuoteInvoice.Core;

public class IntakeTests
{
    static IncomingRequest Load(string id) =>
        System.Text.Json.JsonSerializer.Deserialize<List<IncomingRequest>>(
            File.ReadAllText(Path.Combine(Repo.Data, "requests.json")),
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!
        .Single(r => r.Id == id);

    [Fact]
    public void Complete_mulch_and_mowing_request_is_ready_to_quote()
    {
        var r = Intake.Parse(Load("REQ-001"));
        Assert.Equal(IntakeStatus.ReadyToQuote, r.Status);
        Assert.Equal("18 Willow Lane, Harborline, WA 98000", r.Address);
        Assert.Equal("555-0142", r.Phone);
        Assert.Contains(r.Lines, l => l.Sku == "MULCH-CY" && l.Quantity == 12);
        Assert.Contains(r.Lines, l => l.Sku == "MOW-WK-MED" && l.Quantity == 4);
        Assert.Empty(r.MissingFields);
        Assert.Empty(r.UnknownServices);
    }

    [Fact]
    public void Incomplete_fence_request_is_needs_info_and_does_not_guess()
    {
        var r = Intake.Parse(Load("REQ-002"));
        Assert.Equal(IntakeStatus.NeedsInfo, r.Status);
        Assert.Null(r.Address);
        Assert.Contains("fence_length_ft", r.MissingFields);
        Assert.Contains("fence_material", r.MissingFields);
        Assert.Contains("site_address", r.MissingFields);
        Assert.DoesNotContain(r.Lines, l => l.Sku is not null && l.Quantity is not null);
    }

    [Fact]
    public void Solar_request_is_unknown_service_with_address_kept()
    {
        var r = Intake.Parse(Load("REQ-003"));
        Assert.Equal(IntakeStatus.UnknownService, r.Status);
        Assert.Equal("90 Cedar Court, Harborline, WA 98000", r.Address);
        Assert.Contains(r.UnknownServices, u => u.Contains("solar", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(r.Lines, l => l.Sku is not null);
    }

    [Fact]
    public void Cedar_fence_and_gate_are_extracted()
    {
        var r = Intake.Parse(Load("REQ-004"));
        Assert.Equal(IntakeStatus.ReadyToQuote, r.Status);
        Assert.Contains(r.Lines, l => l.Sku == "FENCE-WD" && l.Quantity == 80);
        Assert.Contains(r.Lines, l => l.Sku == "GATE-WD" && l.Quantity == 1);
        Assert.Equal("44 Birch Street, Harborline, WA 98000", r.Address);
    }

    [Fact]
    public void Irrigation_visit_and_heads_are_extracted()
    {
        var r = Intake.Parse(Load("REQ-005"));
        Assert.Equal(IntakeStatus.ReadyToQuote, r.Status);
        Assert.Contains(r.Lines, l => l.Sku == "IRR-VISIT" && l.Quantity == 1);
        Assert.Contains(r.Lines, l => l.Sku == "IRR-HEAD" && l.Quantity == 4);
    }
}
