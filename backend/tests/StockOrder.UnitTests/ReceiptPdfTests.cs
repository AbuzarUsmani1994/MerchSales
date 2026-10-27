using Shouldly;
using StockOrder.Modules.Sales.Application;
using StockOrder.Modules.Sales.Infrastructure;
using Xunit;

namespace StockOrder.UnitTests;

/// <summary>The 80 mm counter receipt renders on any server (fonts are embedded), Urdu names included.</summary>
public sealed class ReceiptPdfTests
{
    internal static ReceiptModel Sample(bool copy = false) => new(
        "Lahori Grill", "Gulberg", "45-B Main Boulevard, Gulberg III, Lahore", "042-111-222-333",
        "SO-000051", "INV-000040", "PAY-000040", new DateTime(2026, 10, 27, 13, 42, 0), "Glb.Cashier", "Table 3 · Sara",
        [
            new ReceiptLine(1, "Chicken Tikka Pizza", 1850, 1850),
            new ReceiptLine(2, "چکن کڑاہی", 1100, 2200),
            new ReceiptLine(1, "Chocolate Shake", 590, 590),
        ],
        4640, 500, "Visit reward", 4140,
        [new ReceiptPayment("Cash", 4140, null)], 5000, 860,
        new ReceiptMember("Sara Khan", "********544", 1, 5, false), copy);

    [Fact]
    public void Renders_a_pdf()
    {
        var pdf = ReceiptPdf.Render(Sample());
        System.Text.Encoding.ASCII.GetString(pdf, 0, 5).ShouldBe("%PDF-");
        pdf.Length.ShouldBeGreaterThan(1000);
    }

    [Fact]
    public void Renders_one_continuous_page_for_a_roll_printer()
    {
        var pages = ReceiptPdf.RenderImages(Sample(copy: true)).ToList();
        pages.Count.ShouldBe(1);
        if (Environment.GetEnvironmentVariable("RECEIPT_PNG") is { Length: > 0 } path)
        {
            File.WriteAllBytes(path, pages[0]);
        }
    }
}
