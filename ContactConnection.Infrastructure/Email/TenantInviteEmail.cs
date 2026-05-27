namespace ContactConnection.Infrastructure.Email;

public static class TenantInviteEmail
{
    public static string Subject(string tenantName) =>
        $"You've been invited to ContactConnection — {tenantName}";

    public static string HtmlBody(string tenantName, string subdomain, string onboardingUrl) => $"""
        <!DOCTYPE html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1.0">
          <title>Welcome to ContactConnection</title>
        </head>
        <body style="margin:0;padding:0;background-color:#030712;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,sans-serif;">
          <table width="100%" cellpadding="0" cellspacing="0" role="presentation">
            <tr>
              <td align="center" style="padding:48px 16px;">
                <table width="520" cellpadding="0" cellspacing="0" role="presentation"
                       style="background-color:#111827;border-radius:12px;overflow:hidden;border:1px solid #1f2937;">

                  <!-- Header -->
                  <tr>
                    <td style="padding:32px 40px 24px;border-bottom:1px solid #1f2937;">
                      <p style="margin:0 0 4px;color:#6b7280;font-size:11px;font-weight:600;letter-spacing:0.1em;text-transform:uppercase;">
                        Platform Invitation
                      </p>
                      <p style="margin:0;color:#f9fafb;font-size:20px;font-weight:700;letter-spacing:-0.02em;">
                        ContactConnection
                      </p>
                    </td>
                  </tr>

                  <!-- Body -->
                  <tr>
                    <td style="padding:32px 40px 28px;">
                      <h1 style="margin:0 0 12px;color:#f9fafb;font-size:22px;font-weight:700;letter-spacing:-0.02em;">
                        Your workspace is ready
                      </h1>
                      <p style="margin:0 0 8px;color:#9ca3af;font-size:15px;line-height:1.65;">
                        A ContactConnection tenant has been provisioned for
                        <strong style="color:#e5e7eb;">{tenantName}</strong>.
                      </p>
                      <p style="margin:0 0 28px;color:#9ca3af;font-size:15px;line-height:1.65;">
                        Click the button below to complete your onboarding and set up your workspace,
                        users, and settings.
                      </p>
                      <a href="{onboardingUrl}"
                         style="display:inline-block;background-color:#4f46e5;color:#ffffff;text-decoration:none;
                                padding:13px 28px;border-radius:8px;font-size:14px;font-weight:600;
                                letter-spacing:0.01em;">
                        Complete Onboarding
                      </a>

                      <!-- Subdomain callout -->
                      <table width="100%" cellpadding="0" cellspacing="0" role="presentation" style="margin-top:32px;">
                        <tr>
                          <td style="background-color:#1f2937;border-radius:8px;padding:20px 24px;border-left:3px solid #4f46e5;">
                            <p style="margin:0 0 4px;color:#6b7280;font-size:11px;font-weight:600;letter-spacing:0.1em;text-transform:uppercase;">
                              Your sign-in subdomain
                            </p>
                            <p style="margin:0 0 8px;color:#f9fafb;font-size:18px;font-weight:700;letter-spacing:0.02em;">
                              {subdomain}
                            </p>
                            <p style="margin:0;color:#6b7280;font-size:12px;line-height:1.5;">
                              Save this — you'll enter it in the <strong style="color:#9ca3af;">Tenant subdomain</strong> field each time you sign in.
                            </p>
                          </td>
                        </tr>
                      </table>
                    </td>
                  </tr>

                  <!-- Footer -->
                  <tr>
                    <td style="padding:20px 40px 28px;border-top:1px solid #1f2937;">
                      <p style="margin:0;color:#4b5563;font-size:12px;line-height:1.6;">
                        If you weren't expecting this invitation, you can safely ignore this email.<br>
                        This link will expire in 7 days.
                      </p>
                    </td>
                  </tr>

                </table>
              </td>
            </tr>
          </table>
        </body>
        </html>
        """;
}
