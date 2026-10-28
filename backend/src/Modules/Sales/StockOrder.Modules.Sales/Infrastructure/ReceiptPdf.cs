using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using StockOrder.Modules.Sales.Application;

namespace StockOrder.Modules.Sales.Infrastructure;

/// <summary>
/// The 80 mm counter receipt (section 16) with QuestPDF (Community licence: free below USD 1M
/// annual revenue, see README). One continuous page as long as the receipt, for roll printers;
/// an A4 printer prints it as a narrow strip. Fonts are embedded: Roboto for Latin text and
/// Noto Nastaliq Urdu for names typed in Urdu.
/// </summary>
public static class ReceiptPdf
{
    private const string Latin = "Roboto";
    private const string Urdu = "Noto Nastaliq Urdu";
    private static readonly Lazy<bool> Setup = new(() =>
    {
        QuestPDF.Settings.License = LicenseType.Community;
        QuestPDF.Settings.UseSystemFonts = false;
        var assembly = typeof(ReceiptPdf).Assembly;
        foreach (var resource in assembly.GetManifestResourceNames().Where(n => n.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)))
        {
            using var stream = assembly.GetManifestResourceStream(resource)!;
            QuestPDF.Drawing.FontManager.RegisterFontFromStream(stream);
        }

        return true;
    });

    public static byte[] Render(ReceiptModel r) => Build(r).GeneratePdf();

    /// <summary>The receipt as PNG images (one per page), for previews and tests.</summary>
    public static IEnumerable<byte[]> RenderImages(ReceiptModel r) => Build(r).GenerateImages(new ImageGenerationSettings { RasterDpi = 200 });

    private static Document Build(ReceiptModel r)
    {
        _ = Setup.Value;
        return Document.Create(doc => doc.Page(page =>
        {
            page.ContinuousSize(80, Unit.Millimetre);
            page.Margin(4, Unit.Millimetre);
            page.DefaultTextStyle(t => t.FontFamily(Latin, Urdu).FontSize(9).FontColor(Colors.Black));
            page.Content().Column(col =>
            {
                col.Spacing(2);
                col.Item().AlignCenter().Text(r.CompanyName).Bold().FontSize(14);
                col.Item().AlignCenter().Text(r.BranchName).FontSize(10);
                if (!string.IsNullOrWhiteSpace(r.BranchAddress))
                {
                    col.Item().AlignCenter().Text(r.BranchAddress).FontSize(8);
                }

                if (!string.IsNullOrWhiteSpace(r.BranchPhone))
                {
                    col.Item().AlignCenter().Text($"Tel {r.BranchPhone}").FontSize(8);
                }

                if (r.IsCopy)
                {
                    col.Item().PaddingTop(2).AlignCenter().Text("*** COPY ***").Bold();
                }

                if (r.ShortOrderNo is { } shortNo)
                {
                    col.Item().PaddingTop(3).AlignCenter().Text(r.TableName is { } tableName ? $"Order #{shortNo} · Table {tableName}" : $"Order #{shortNo}")
                        .Bold().FontSize(16);
                }

                Rule(col);
                Pair(col, "Receipt", r.ReceiptNo ?? r.InvoiceNo, bold: true);
                Pair(col, "Invoice", r.InvoiceNo);
                if (r.OrderNo is { } orderNo)
                {
                    Pair(col, "Order", orderNo);
                }

                Pair(col, "Date", r.PaidAtLocal.ToString("d MMM yyyy, h:mm tt", CultureInfo.InvariantCulture));
                if (r.Cashier is { } cashier)
                {
                    Pair(col, "Cashier", cashier);
                }

                if (!string.IsNullOrWhiteSpace(r.Label))
                {
                    Pair(col, "For", r.Label);
                }

                Rule(col);
                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.ConstantColumn(7, Unit.Millimetre);
                        c.RelativeColumn();
                        c.ConstantColumn(17, Unit.Millimetre);
                    });
                    foreach (var line in r.Lines)
                    {
                        table.Cell().PaddingVertical(1).Text(Qty(line.Quantity));
                        table.Cell().PaddingVertical(1).Column(c =>
                        {
                            c.Item().Text(line.Name);
                            if (line.Quantity != 1)
                            {
                                c.Item().Text($"@ {Money(line.UnitPrice)}").FontSize(7).FontColor(Colors.Grey.Darken2);
                            }
                        });
                        table.Cell().PaddingVertical(1).AlignRight().Text(Money(line.Amount));
                    }
                });

                Rule(col);
                Pair(col, "Subtotal", Money(r.SubTotal));
                if (r.Discount > 0)
                {
                    Pair(col, r.DiscountLabel ?? "Discount", "-" + Money(r.Discount));
                }

                col.Item().PaddingTop(2).Row(row =>
                {
                    row.RelativeItem().Text("TOTAL").Bold().FontSize(12);
                    row.AutoItem().Text($"Rs {Money(r.Total)}").Bold().FontSize(12);
                });

                Rule(col);
                foreach (var payment in r.Payments)
                {
                    Pair(col, payment.Reference is { } reference ? $"{payment.Method} ({reference})" : payment.Method, Money(payment.Amount));
                }

                if (r.Tendered is { } tendered)
                {
                    Pair(col, "Cash received", Money(tendered));
                    Pair(col, "Change", Money(r.Change ?? 0), bold: true);
                }

                if (r.Member is { } member)
                {
                    Rule(col);
                    col.Item().AlignCenter().Text($"Visit rewards · {member.Name}").Bold();
                    col.Item().AlignCenter().Text(member.Phone).FontSize(8);
                    col.Item().AlignCenter().Text(member.RewardReady
                        ? "Your reward is ready for your next visit!"
                        : $"{member.Visits} of {member.VisitsForReward} visits · {member.VisitsForReward - member.Visits} more for your reward");
                }

                Rule(col);
                col.Item().AlignCenter().Text("Thank you! Please visit again.").FontSize(9);
                col.Item().AlignCenter().Text("Prices in Pakistani rupees.").FontSize(7).FontColor(Colors.Grey.Darken2);
            });
        }));
    }

    private static void Rule(ColumnDescriptor col) => col.Item().PaddingVertical(2).LineHorizontal(0.6f).LineColor(Colors.Black);

    private static void Pair(ColumnDescriptor col, string label, string value, bool bold = false) =>
        col.Item().Row(row =>
        {
            var left = row.RelativeItem().Text(label);
            var right = row.AutoItem().Text(value);
            if (bold)
            {
                left.Bold();
                right.Bold();
            }
        });

    private static string Money(decimal value) =>
        value.ToString(decimal.Round(value) == value ? "#,0" : "#,0.00", CultureInfo.InvariantCulture);

    private static string Qty(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
