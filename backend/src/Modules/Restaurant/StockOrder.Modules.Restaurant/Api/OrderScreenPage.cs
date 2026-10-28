using System.Text;
using System.Text.Encodings.Web;
using StockOrder.Modules.Restaurant.Application;

namespace StockOrder.Modules.Restaurant.Api;

/// <summary>
/// "Now preparing / Ready to collect" as one page for a TV. The numbers are in the HTML, so it
/// works even before the script runs; the script (from this site only) refreshes them every few
/// seconds and chimes when a number becomes ready, once someone has tapped the screen to allow sound.
/// </summary>
public static class OrderScreenPage
{
    /// <summary>Its own script and data only; nothing from anywhere else.</summary>
    public const string ContentSecurityPolicy =
        "default-src 'none'; script-src 'self'; connect-src 'self'; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";

    private static readonly HtmlEncoder Html = HtmlEncoder.Default;

    public static string Render(OrderScreenView view)
    {
        var page = new StringBuilder(4 * 1024);
        page.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\">")
            .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">")
            .Append("<title>Orders – ").Append(E(view.BranchName)).Append("</title>")
            .Append("<style>").Append(Styles).Append("</style>")
            .Append("<script src=\"/s/screen.js\" defer></script></head><body>")
            .Append("<header><h1>").Append(E(view.TenantName)).Append("</h1><span>").Append(E(view.BranchName)).Append("</span>")
            .Append("<button id=\"sound\" type=\"button\">Tap for sound</button></header>")
            .Append("<main><section class=\"prep\"><h2>Now preparing</h2><div id=\"preparing\" class=\"nums\">");
        Numbers(page, view.Preparing);
        page.Append("</div></section><section class=\"ready\"><h2>Ready to collect</h2><div id=\"ready\" class=\"nums\">");
        Numbers(page, view.Ready);
        page.Append("</div></section></main><footer id=\"status\"></footer></body></html>");
        return page.ToString();
    }

    public static string RenderNotFound() =>
        "<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">" +
        "<title>Screen not available</title><style>" + Styles + "</style></head><body><main class=\"gone\"><p>This screen link isn't switched on. " +
        "Ask the manager for the current link.</p></main></body></html>";

    private static void Numbers(StringBuilder page, IReadOnlyList<int> numbers)
    {
        foreach (var n in numbers)
        {
            page.Append("<b>").Append(n).Append("</b>");
        }
    }

    private static string E(string value) => Html.Encode(value);

    private const string Styles =
        "*{box-sizing:border-box}html,body{margin:0;height:100%;background:#111;color:#fff;font-family:system-ui,'Segoe UI',Roboto,Arial,sans-serif}" +
        "body{display:flex;flex-direction:column}" +
        "header{display:flex;align-items:baseline;gap:1rem;padding:1rem 2rem;background:#1c1c1c}" +
        "h1{margin:0;font-size:clamp(1.2rem,3vw,2.2rem)}header span{opacity:.7;font-size:clamp(1rem,2vw,1.4rem)}" +
        "#sound{margin-left:auto;background:#333;color:#fff;border:1px solid #555;border-radius:6px;padding:.4rem .8rem;font-size:1rem}" +
        "main{flex:1;display:grid;grid-template-columns:1fr 1fr}" +
        "section{padding:1.5rem 2rem}.prep{border-right:2px solid #333}" +
        "h2{margin:0 0 1rem;font-size:clamp(1.4rem,3.5vw,3rem);text-transform:uppercase;letter-spacing:.05em}" +
        ".prep h2{color:#f5b942}.ready h2{color:#4cd07d}" +
        ".nums{display:flex;flex-wrap:wrap;gap:1rem}" +
        ".nums b{min-width:2.2em;text-align:center;font-size:clamp(2.2rem,7vw,6rem);line-height:1.1;padding:.1em .25em;border-radius:12px}" +
        ".prep b{background:#2a2410;color:#f5b942}.ready b{background:#0f2b1a;color:#4cd07d}" +
        ".ready b.new{animation:pulse 1s ease-in-out 6}@keyframes pulse{50%{background:#4cd07d;color:#000}}" +
        "footer{padding:.4rem 2rem;font-size:.9rem;opacity:.6;min-height:1.6rem}" +
        ".gone{display:flex;align-items:center;justify-content:center;padding:2rem;font-size:1.5rem;text-align:center}" +
        "@media (max-width:700px){main{grid-template-columns:1fr}.prep{border-right:0;border-bottom:2px solid #333}}";

    /// <summary>Polls /s/{code}/data every 4 seconds; numbers are written with textContent only.</summary>
    public const string Script =
        """
        (function () {
          var code = location.pathname.split('/')[2];
          var shown = null, audio = null;
          var soundButton = document.getElementById('sound');
          soundButton.addEventListener('click', function () {
            try { audio = new (window.AudioContext || window.webkitAudioContext)(); chime(); soundButton.textContent = 'Sound on'; } catch (e) { }
          });
          function chime() {
            if (!audio) return;
            [0, 0.18].forEach(function (at, i) {
              var o = audio.createOscillator(), g = audio.createGain();
              o.frequency.value = i ? 1318 : 988; o.connect(g); g.connect(audio.destination);
              g.gain.setValueAtTime(0.25, audio.currentTime + at); g.gain.exponentialRampToValueAtTime(0.001, audio.currentTime + at + 0.5);
              o.start(audio.currentTime + at); o.stop(audio.currentTime + at + 0.5);
            });
          }
          function fill(id, numbers, fresh) {
            var box = document.getElementById(id);
            box.replaceChildren();
            numbers.forEach(function (n) {
              var b = document.createElement('b'); b.textContent = String(n);
              if (fresh && fresh.indexOf(n) >= 0) b.className = 'new';
              box.appendChild(b);
            });
          }
          function current(id) {
            return Array.prototype.map.call(document.getElementById(id).children, function (b) { return Number(b.textContent); });
          }
          shown = current('ready');
          function poll() {
            fetch('/s/' + encodeURIComponent(code) + '/data', { cache: 'no-store' })
              .then(function (r) { if (r.status === 404) { location.reload(); throw 0; } return r.json(); })
              .then(function (d) {
                var fresh = d.ready.filter(function (n) { return shown.indexOf(n) < 0; });
                fill('preparing', d.preparing, null);
                fill('ready', d.ready, fresh);
                if (fresh.length) chime();
                shown = d.ready;
                document.getElementById('status').textContent = '';
              })
              .catch(function (e) { if (e !== 0) document.getElementById('status').textContent = 'Reconnecting…'; })
              .finally(function () { setTimeout(poll, 4000); });
          }
          setTimeout(poll, 4000);
        })();
        """;
}
