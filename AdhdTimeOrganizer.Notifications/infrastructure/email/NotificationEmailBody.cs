namespace AdhdTimeOrganizer.Notifications.infrastructure.email;

/// <summary>
/// Wraps a rendered (title, body) pair in a minimal inline-styled HTML mail. Inline styles only —
/// mail clients strip &lt;style&gt; blocks. Everything interpolated is escaped: the body comes from
/// a payload that business code supplies, so it must never be trusted as markup.
/// </summary>
public static class NotificationEmailBody
{
    public static string Build(string appName, string title, string body, string? portalUrl)
    {
        var appNameHtml = Escape(appName);
        var titleHtml = Escape(title);
        var bodyHtml = Escape(body);

        var link = string.IsNullOrWhiteSpace(portalUrl)
            ? string.Empty
            : $"""
               <p style="margin:24px 0 0;">
                 <a href="{Escape(portalUrl)}" style="display:inline-block;padding:10px 18px;background:#1f6feb;color:#ffffff;text-decoration:none;border-radius:6px;font-size:14px;">Otvoriť v portáli</a>
               </p>
               """;

        return $"""
                <div style="font-family:Segoe UI,Roboto,Helvetica,Arial,sans-serif;color:#1f2328;max-width:560px;margin:0 auto;padding:24px;">
                  <p style="margin:0 0 16px;font-size:13px;color:#656d76;text-transform:uppercase;letter-spacing:.04em;">{appNameHtml}</p>
                  <h1 style="margin:0 0 12px;font-size:20px;line-height:1.3;">{titleHtml}</h1>
                  <p style="margin:0;font-size:15px;line-height:1.55;">{bodyHtml}</p>
                  {link}
                  <hr style="border:0;border-top:1px solid #d0d7de;margin:28px 0 12px;" />
                  <p style="margin:0;font-size:12px;color:#656d76;">Toto je prevádzková notifikácia zo systému {appNameHtml}. Doručovanie e-mailom si môžete vypnúť v nastaveniach notifikácií.</p>
                </div>
                """;
    }

    /// <summary>
    /// Escapes only the five HTML-significant characters. Deliberately NOT
    /// <see cref="System.Net.WebUtility.HtmlEncode"/>, which also entity-escapes every non-ASCII
    /// character and would turn Slovak diacritics into a wall of <c>&amp;#237;</c> in the mail source.
    /// The message is sent as UTF-8, so the accented characters are safe verbatim.
    /// </summary>
    private static string Escape(string value) =>
        value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&#39;");
}