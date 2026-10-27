using Shouldly;
using StockOrder.Infrastructure.Database;
using StockOrder.Modules.Catalog.Api;
using StockOrder.Modules.Catalog.Application;
using Xunit;

namespace StockOrder.UnitTests;

/// <summary>The customer menu page a waiter's QR opens: anyone can open it, so nothing typed into the catalog may run as code.</summary>
public sealed class PublicMenuPageTests
{
    private const string Code = "abcdefghjkmnpq23";

    [Fact]
    public void Shows_items_with_price_picture_and_placeholder()
    {
        var pictureId = Guid.NewGuid();
        var withPicture = new PublicMenuItem(Guid.NewGuid(), "Large Pizza", "Cheesy", 1850m, pictureId);
        var withoutPicture = new PublicMenuItem(Guid.NewGuid(), "Cola", null, 120.5m, null);
        var html = PublicMenuPage.Render(Code, Menu(new PublicMenuSection(Guid.NewGuid(), "Pizzas", [withPicture]), new PublicMenuSection(null, null, [withoutPicture])));

        html.ShouldContain("Large Pizza");
        html.ShouldContain("Rs 1,850<");
        html.ShouldContain("Rs 120.50<");
        html.ShouldContain($"src=\"/m/{Code}/items/{withPicture.Id:D}/image?v={pictureId:N}\"");
        html.ShouldContain($"hsl({PublicMenuPage.Hue("Cola")} 55% 52%)");
        html.ShouldContain(">More</a>");
        html.ShouldNotContain("<script");
    }

    [Fact]
    public void Encodes_everything_from_the_catalog()
    {
        var evil = new PublicMenuItem(Guid.NewGuid(), "<script>alert(1)</script>", "\"><img src=x onerror=alert(1)>", 1m, null);
        var html = PublicMenuPage.Render(Code, Menu(new PublicMenuSection(null, "<b>Deals</b>", [evil])) with { BranchName = "<i>Eden</i>" });

        html.ShouldNotContain("<script>alert");
        html.ShouldNotContain("<img src=x");
        html.ShouldNotContain("<b>Deals");
        html.ShouldNotContain("<i>Eden");
    }

    [Fact]
    public void Placeholder_colour_matches_the_app_and_back_office()
    {
        // Same 31-hash as the Dart and TypeScript placeholders, computed by hand for "Ab": 65*31+98 = 2113.
        PublicMenuPage.Hue("Ab").ShouldBe(2113 % 360);
        PublicMenuPage.Hue(new string('z', 40)).ShouldBeInRange(0, 359);
    }

    [Fact]
    public void Menu_codes_are_random_and_checked_before_any_lookup()
    {
        var code = PublicMenuCode.New();
        PublicMenuCode.IsWellFormed(code).ShouldBeTrue();
        PublicMenuCode.New().ShouldNotBe(code);
        PublicMenuCode.IsWellFormed("short").ShouldBeFalse();
        PublicMenuCode.IsWellFormed("ABCDEFGHJKMNPQ23").ShouldBeFalse();
        PublicMenuCode.IsWellFormed("abcdefghjkmnpq2'").ShouldBeFalse();
    }

    private static PublicMenu Menu(params PublicMenuSection[] sections) => new("Cheezious", "Lake City", "Main Boulevard", "042-111", sections);
}
