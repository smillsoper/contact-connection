using System.Net.Http.Headers;
using System.Net.Http.Json;
using ContactConnection.Application.Interfaces.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ContactConnection.Infrastructure.Email;

public class ResendEmailService : IEmailService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ResendEmailService> _logger;
    private readonly string _apiKey;
    private readonly string _fromAddress;

    public ResendEmailService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<ResendEmailService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _apiKey = configuration["Resend:ApiKey"]
            ?? throw new InvalidOperationException("Resend:ApiKey is not configured.");
        _fromAddress = configuration["Resend:FromAddress"]
            ?? "ContactConnection <noreply@contactconnection.io>";
    }

    public Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default) =>
        SendAsync(new EmailMessage { To = [to], Subject = subject, HtmlBody = htmlBody }, ct);

    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        if (!message.HasRecipients)
            throw new ArgumentException("EmailMessage has no recipients (to/cc/bcc all empty).", nameof(message));

        var client = _httpClientFactory.CreateClient("Resend");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        var payload = new Dictionary<string, object?>
        {
            ["from"]    = ComposeFrom(message.FromName),
            ["to"]      = message.To,
            ["subject"] = message.Subject,
            ["html"]    = message.HtmlBody,
        };
        if (message.Cc.Count > 0)  payload["cc"]  = message.Cc;
        if (message.Bcc.Count > 0) payload["bcc"] = message.Bcc;
        if (!string.IsNullOrWhiteSpace(message.ReplyTo)) payload["reply_to"] = message.ReplyTo;
        if (message.Attachments.Count > 0)
            payload["attachments"] = message.Attachments
                .Select(a => new
                {
                    filename = a.FileName,
                    content  = Convert.ToBase64String(a.Content),
                    content_type = a.ContentType,
                })
                .ToArray();

        var response = await client.PostAsJsonAsync("https://api.resend.com/emails", payload, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Resend API returned {StatusCode}: {Body}", (int)response.StatusCode, responseBody);
            response.EnsureSuccessStatusCode();
        }

        _logger.LogInformation(
            "Email sent via Resend to [{To}] cc [{Cc}] bcc [{Bcc}] — subject: {Subject}{Attach} — response: {Body}",
            string.Join(",", message.To), string.Join(",", message.Cc), string.Join(",", message.Bcc),
            message.Subject,
            message.Attachments.Count > 0 ? $" (+{message.Attachments.Count} attachment(s))" : "",
            responseBody);
    }

    /// <summary>
    /// Builds the RFC-5322 From header. The address always stays the configured sender (Resend
    /// only sends from verified domains); only the display name is overridable per message.
    /// </summary>
    private string ComposeFrom(string? fromName)
    {
        if (string.IsNullOrWhiteSpace(fromName)) return _fromAddress;

        var start = _fromAddress.IndexOf('<');
        var end   = _fromAddress.IndexOf('>');
        var address = start >= 0 && end > start
            ? _fromAddress[(start + 1)..end].Trim()
            : _fromAddress.Trim();

        var cleanName = fromName.Replace("\"", "").Replace("\r", " ").Replace("\n", " ").Trim();
        return $"{cleanName} <{address}>";
    }
}
