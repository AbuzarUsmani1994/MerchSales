using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using StockOrder.Modules.Catalog.Application;

namespace StockOrder.Modules.Catalog.Api;

/// <summary>
/// The customer menu as one small HTML page: no script at all, styles inline, pictures loaded
/// lazily from the same site, so it opens fast on a customer's phone over mobile data. Every
/// value from the database is HTML-encoded.
/// </summary>
public static class PublicMenuPage
{
    /// <summary>No scripts, no outside requests; pictures from this site only.</summary>
    public const string ContentSecurityPolicy =
        "default-src 'none'; img-src 'self'; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";

    private static readonly HtmlEncoder Html = HtmlEncoder.Default;

    public static string Render(string code, PublicMenu menu)
    {
        var page = new StringBuilder(16 * 1024);
        Head(page, $"{menu.BranchName} – {menu.TenantName}");
        page.Append("<header class=\"hero\"><div class=\"mark\">").Append(E(Initial(menu.TenantName))).Append("</div><div>")
            .Append("<h1>").Append(E(menu.TenantName)).Append("</h1>")
            .Append("<p>").Append(E(menu.BranchName)).Append("</p></div></header>");

        if (menu.Sections.Count == 0)
        {
            page.Append("<main><p class=\"empty\">The menu is being updated. Please ask our staff.</p></main>");
            Foot(page, menu);
            return page.ToString();
        }

        if (menu.Sections.Count > 1)
        {
            page.Append("<nav>");
            foreach (var (section, index) in menu.Sections.Select((s, i) => (s, i)))
            {
                page.Append("<a href=\"#s").Append(index).Append("\">").Append(E(SectionName(section))).Append("</a>");
            }

            page.Append("</nav>");
        }

        page.Append("<main>");
        foreach (var (section, index) in menu.Sections.Select((s, i) => (s, i)))
        {
            page.Append("<section id=\"s").Append(index).Append("\"><h2>").Append(E(SectionName(section))).Append("</h2><div class=\"grid\">");
            foreach (var item in section.Items)
            {
                page.Append("<article>");
                if (item.ImageAttachmentId is { } imageId)
                {
                    page.Append("<img loading=\"lazy\" decoding=\"async\" alt=\"\" src=\"/m/").Append(E(code)).Append("/items/")
                        .Append(item.Id.ToString("D")).Append("/image?v=").Append(imageId.ToString("N")).Append("\">");
                }
                else
                {
                    page.Append("<div class=\"ph\" style=\"background:hsl(").Append(Hue(item.Name)).Append(" 55% 52%)\">")
                        .Append(E(Initial(item.Name))).Append("</div>");
                }

                page.Append("<div class=\"body\"><h3>").Append(E(item.Name)).Append("</h3>");
                if (!string.IsNullOrWhiteSpace(item.Description))
                {
                    page.Append("<p>").Append(E(item.Description)).Append("</p>");
                }

                page.Append("<div class=\"price\">").Append(E(Price(item.Price))).Append("</div></div></article>");
            }

            page.Append("</div></section>");
        }

        page.Append("</main>");
        Foot(page, menu);
        return page.ToString();
    }

    public static string RenderNotFound()
    {
        var page = new StringBuilder();
        Head(page, "Menu not available");
        page.Append("<main class=\"gone\"><h1>This menu isn't available</h1><p>Please ask our staff to show you the menu QR code again.</p></main></body></html>");
        return page.ToString();
    }

    /// <summary>"Rs 1,850", or "Rs 1,850.50" when there are paisa.</summary>
    public static string Price(decimal price) =>
        "Rs " + price.ToString(decimal.Round(price) == price ? "#,0" : "#,0.00", CultureInfo.InvariantCulture);

    /// <summary>The same placeholder colour as the app and back office for a name without a picture.</summary>
    public static int Hue(string name)
    {
        var hash = 0;
        foreach (var unit in name)
        {
            hash = unchecked(hash * 31 + unit);
        }

        return (int)(Math.Abs((long)hash) % 360);
    }

    private static string SectionName(PublicMenuSection section) => section.Name ?? "More";

    private static string Initial(string name) =>
        name.Trim() is { Length: > 0 } trimmed ? char.ToUpperInvariant(trimmed[0]).ToString() : "?";

    private static string E(string value) => Html.Encode(value);

    private static void Head(StringBuilder page, string title) =>
        page.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\">")
            .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">")
            .Append("<meta name=\"robots\" content=\"noindex\"><meta name=\"theme-color\" content=\"#7C2D12\">")
            .Append("<title>").Append(E(title)).Append("</title><style>").Append(Styles).Append("</style></head><body>");

    private static void Foot(StringBuilder page, PublicMenu menu)
    {
        page.Append("<footer><strong>").Append(E(menu.BranchName)).Append("</strong>");
        if (!string.IsNullOrWhiteSpace(menu.BranchAddress))
        {
            page.Append("<span>").Append(E(menu.BranchAddress)).Append("</span>");
        }

        if (!string.IsNullOrWhiteSpace(menu.BranchPhone))
        {
            page.Append("<span>").Append(E(menu.BranchPhone)).Append("</span>");
        }

        page.Append("<span class=\"note\">Prices in Pakistani rupees.</span></footer></body></html>");
    }

    // The restaurant palette of the waiter's app: burnt orange on cream.
    private const string Styles =
        """
        :root{--primary:#C2410C;--ink:#431407;--muted:#78716C;--bg:#FFF7ED;--soft:#FFEDD5;--card:#fff;--line:#FED7AA}
        *{box-sizing:border-box}
        body{margin:0;background:var(--bg);color:var(--ink);font:15px/1.4 system-ui,-apple-system,"Segoe UI",Roboto,sans-serif;-webkit-text-size-adjust:100%}
        .hero{display:flex;gap:14px;align-items:center;padding:22px 16px 20px;color:#fff;background:linear-gradient(135deg,#7C2D12,#C2410C 60%,#F59E0B)}
        .mark{flex:none;width:48px;height:48px;border-radius:14px;background:rgba(255,255,255,.18);display:flex;align-items:center;justify-content:center;font-weight:800;font-size:22px}
        .hero h1{margin:0;font-size:22px;line-height:1.2}
        .hero p{margin:2px 0 0;opacity:.9}
        nav{position:sticky;top:0;z-index:1;display:flex;gap:8px;overflow-x:auto;padding:10px 16px;background:var(--bg);border-bottom:1px solid var(--line);scrollbar-width:none}
        nav::-webkit-scrollbar{display:none}
        nav a{flex:none;padding:7px 14px;border-radius:999px;background:var(--card);border:1px solid var(--line);color:var(--ink);text-decoration:none;font-weight:600;font-size:14px}
        main{max-width:960px;margin:0 auto;padding:4px 16px 24px}
        section{scroll-margin-top:60px}
        h2{margin:20px 0 10px;font-size:18px}
        .grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:12px}
        @media(min-width:640px){.grid{grid-template-columns:repeat(3,minmax(0,1fr))}}
        @media(min-width:900px){.grid{grid-template-columns:repeat(4,minmax(0,1fr))}}
        article{background:var(--card);border:1px solid var(--line);border-radius:16px;overflow:hidden;display:flex;flex-direction:column}
        article img,.ph{display:block;width:100%;aspect-ratio:1/1;object-fit:cover;background:var(--soft)}
        .ph{display:flex;align-items:center;justify-content:center;color:#fff;font-weight:800;font-size:42px}
        .body{padding:10px 12px 12px;display:flex;flex-direction:column;gap:4px;flex:1}
        h3{margin:0;font-size:15px;line-height:1.25}
        .body p{margin:0;color:var(--muted);font-size:13px;display:-webkit-box;-webkit-line-clamp:2;-webkit-box-orient:vertical;overflow:hidden}
        .price{margin-top:auto;padding-top:4px;color:var(--primary);font-weight:800;font-size:16px}
        .empty,.gone{text-align:center;color:var(--muted);padding:48px 16px}
        .gone h1{color:var(--ink);font-size:20px}
        footer{display:flex;flex-direction:column;gap:2px;align-items:center;text-align:center;padding:20px 16px 32px;color:var(--muted);font-size:13px;border-top:1px solid var(--line)}
        footer strong{color:var(--ink)}
        .note{margin-top:6px;font-size:12px}
        """;
}
