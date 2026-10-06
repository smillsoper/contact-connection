using System.Net;

namespace ContactConnection.Infrastructure.Email;

/// <summary>Client-portal invite / set-password email (S181). Every interpolated value is HTML-encoded.</summary>
public static class ClientUserInviteEmail
{
    public static string Subject(string tenantName, bool reset) =>
        reset ? $"Set a new password for your {tenantName} dashboards" : $"You've been invited to view {tenantName} dashboards";

    public static string HtmlBody(string tenantName, string firstName, string acceptUrl, string loginUrl, bool reset)
    {
        var t = WebUtility.HtmlEncode(tenantName);
        var name = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(firstName) ? "there" : firstName);
        var accept = WebUtility.HtmlEncode(acceptUrl);
        var login = WebUtility.HtmlEncode(loginUrl);
        var heading = reset ? "Set a new password" : "Your dashboards are ready";
        var intro = reset
            ? $"A new password link was requested for your <strong style=\"color:#e5e7eb;\">{t}</strong> dashboard account."
            : $"<strong style=\"color:#e5e7eb;\">{t}</strong> has given you access to live reporting dashboards.";
        var button = reset ? "Set Password" : "Set Up Your Account";
        return $"""
            <!DOCTYPE html>
            <html lang="en">
            <head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1.0"><title>{heading}</title></head>
            <body style="margin:0;padding:0;background-color:#030712;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,sans-serif;">
              <table width="100%" cellpadding="0" cellspacing="0" role="presentation">
                <tr><td align="center" style="padding:48px 16px;">
                  <table width="520" cellpadding="0" cellspacing="0" role="presentation" style="background-color:#111827;border-radius:12px;overflow:hidden;border:1px solid #1f2937;">
                    <tr><td style="padding:32px 40px 24px;border-bottom:1px solid #1f2937;">
                      <p style="margin:0 0 4px;color:#6b7280;font-size:11px;font-weight:600;letter-spacing:0.1em;text-transform:uppercase;">Client dashboards</p>
                      <p style="margin:0;color:#f9fafb;font-size:20px;font-weight:700;">{t}</p>
                    </td></tr>
                    <tr><td style="padding:32px 40px 28px;">
                      <h1 style="margin:0 0 12px;color:#f9fafb;font-size:22px;font-weight:700;">{heading}</h1>
                      <p style="margin:0 0 8px;color:#9ca3af;font-size:15px;line-height:1.65;">Hi {name},</p>
                      <p style="margin:0 0 28px;color:#9ca3af;font-size:15px;line-height:1.65;">{intro} The link below works once and expires in 7 days.</p>
                      <a href="{accept}" style="display:inline-block;background-color:#4f46e5;color:#ffffff;text-decoration:none;padding:13px 28px;border-radius:8px;font-size:14px;font-weight:600;">{button}</a>
                      <table width="100%" cellpadding="0" cellspacing="0" role="presentation" style="margin-top:32px;">
                        <tr><td style="background-color:#1f2937;border-radius:8px;padding:20px 24px;border-left:3px solid #4f46e5;">
                          <p style="margin:0 0 4px;color:#6b7280;font-size:11px;font-weight:600;letter-spacing:0.1em;text-transform:uppercase;">Your sign-in page</p>
                          <p style="margin:0 0 10px;"><a href="{login}" style="color:#818cf8;font-size:15px;font-weight:600;text-decoration:none;word-break:break-all;">{login}</a></p>
                          <p style="margin:0;color:#6b7280;font-size:12px;line-height:1.5;">Bookmark it — it's where you'll sign in from now on.</p>
                        </td></tr>
                      </table>
                    </td></tr>
                    <tr><td style="padding:20px 40px 28px;border-top:1px solid #1f2937;">
                      <p style="margin:0;color:#4b5563;font-size:12px;line-height:1.6;">If you weren't expecting this, you can ignore it — nothing happens until the link is used.</p>
                    </td></tr>
                  </table>
                </td></tr>
              </table>
            </body>
            </html>
            """;
    }
}
