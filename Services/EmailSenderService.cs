using System.Net;
using System.Net.Mail;

namespace Monitoring.Blazor.Services;

public sealed class EmailSenderService(
    IConfiguration configuration,
    EmailSettingsRepository emailSettingsRepository)
{
    public EmailSettings GetSettings()
    {
        var baseSettings = configuration.GetSection("Monitoring:Email").Get<EmailSettings>() ?? new EmailSettings();
        return emailSettingsRepository.Load(baseSettings);
    }

    public bool IsVerificationEnabled()
    {
        var settings = GetSettings();
        return settings.Enabled && settings.VerificationEnabled;
    }

    public bool IsPasswordResetEnabled()
    {
        var settings = GetSettings();
        return settings.Enabled && settings.PasswordResetEnabled;
    }

    public async Task SendVerificationEmailAsync(
        string toAddress,
        string verificationUrl,
        string? displayName = null,
        CancellationToken ct = default)
    {
        var settings = GetSettings();
        if (!settings.Enabled || !settings.VerificationEnabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(toAddress))
        {
            throw new InvalidOperationException("Email address is required.");
        }

        if (string.IsNullOrWhiteSpace(settings.SmtpHost))
        {
            throw new InvalidOperationException("SMTP host is required.");
        }

        if (string.IsNullOrWhiteSpace(settings.FromAddress))
        {
            throw new InvalidOperationException("From address is required.");
        }

        using var message = new MailMessage
        {
            From = new MailAddress(settings.FromAddress.Trim(), string.IsNullOrWhiteSpace(settings.FromName) ? "OpsEye" : settings.FromName.Trim()),
            Subject = settings.VerificationSubject,
            BodyEncoding = System.Text.Encoding.UTF8,
            SubjectEncoding = System.Text.Encoding.UTF8,
            Body = BuildPlainBody(verificationUrl),
            IsBodyHtml = false
        };
        message.To.Add(new MailAddress(toAddress.Trim(), string.IsNullOrWhiteSpace(displayName) ? string.Empty : displayName.Trim()));
        message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(BuildHtmlBody(verificationUrl), System.Text.Encoding.UTF8, "text/html"));

        using var client = new SmtpClient(settings.SmtpHost.Trim(), settings.SmtpPort)
        {
            EnableSsl = settings.EnableSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = settings.UseDefaultCredentials
        };

        if (!settings.UseDefaultCredentials &&
            !string.IsNullOrWhiteSpace(settings.SmtpUsername))
        {
            client.Credentials = new NetworkCredential(settings.SmtpUsername.Trim(), settings.SmtpPassword ?? string.Empty);
        }

        await client.SendMailAsync(message, ct);
    }

    public async Task SendPasswordResetEmailAsync(
        string toAddress,
        string resetUrl,
        string? displayName = null,
        CancellationToken ct = default)
    {
        var settings = GetSettings();
        if (!settings.Enabled || !settings.PasswordResetEnabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(toAddress))
        {
            throw new InvalidOperationException("Email address is required.");
        }

        if (string.IsNullOrWhiteSpace(settings.SmtpHost))
        {
            throw new InvalidOperationException("SMTP host is required.");
        }

        if (string.IsNullOrWhiteSpace(settings.FromAddress))
        {
            throw new InvalidOperationException("From address is required.");
        }

        using var message = new MailMessage
        {
            From = new MailAddress(settings.FromAddress.Trim(), string.IsNullOrWhiteSpace(settings.FromName) ? "OpsEye" : settings.FromName.Trim()),
            Subject = "Reset your OpsEye password",
            BodyEncoding = System.Text.Encoding.UTF8,
            SubjectEncoding = System.Text.Encoding.UTF8,
            Body = BuildPasswordResetPlainBody(resetUrl),
            IsBodyHtml = false
        };
        message.To.Add(new MailAddress(toAddress.Trim(), string.IsNullOrWhiteSpace(displayName) ? string.Empty : displayName.Trim()));
        message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(BuildPasswordResetHtmlBody(resetUrl), System.Text.Encoding.UTF8, "text/html"));

        using var client = new SmtpClient(settings.SmtpHost.Trim(), settings.SmtpPort)
        {
            EnableSsl = settings.EnableSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = settings.UseDefaultCredentials
        };

        if (!settings.UseDefaultCredentials &&
            !string.IsNullOrWhiteSpace(settings.SmtpUsername))
        {
            client.Credentials = new NetworkCredential(settings.SmtpUsername.Trim(), settings.SmtpPassword ?? string.Empty);
        }

        await client.SendMailAsync(message, ct);
    }

    private static string BuildPlainBody(string verificationUrl)
    {
        return $"""
OpsEye email verification

Please confirm your email address by opening this link:
{verificationUrl}

If you did not request this account, you can ignore this message.
""";
    }

    private static string BuildHtmlBody(string verificationUrl)
    {
        var safeUrl = WebUtility.HtmlEncode(verificationUrl);
        return $"""
<html>
  <body style="font-family:Segoe UI,Arial,sans-serif;line-height:1.5;color:#111827;">
    <h2 style="margin-bottom:0.5rem;">OpsEye email verification</h2>
    <p>Please confirm your email address by opening the link below.</p>
    <p><a href="{safeUrl}" style="display:inline-block;padding:10px 16px;background:#2563eb;color:#fff;text-decoration:none;border-radius:8px;">Verify email</a></p>
    <p style="word-break:break-all;"><a href="{safeUrl}">{safeUrl}</a></p>
    <p style="color:#6b7280;">If you did not request this account, you can ignore this message.</p>
  </body>
</html>
""";
    }

    private static string BuildPasswordResetPlainBody(string resetUrl)
    {
        return $"""
OpsEye password reset

Please reset your password by opening this link:
{resetUrl}

If you did not request a password reset, you can ignore this message.
""";
    }

    private static string BuildPasswordResetHtmlBody(string resetUrl)
    {
        var safeUrl = WebUtility.HtmlEncode(resetUrl);
        return $"""
<html>
  <body style="font-family:Segoe UI,Arial,sans-serif;line-height:1.5;color:#111827;">
    <h2 style="margin-bottom:0.5rem;">OpsEye password reset</h2>
    <p>You can reset your password by opening the link below.</p>
    <p><a href="{safeUrl}" style="display:inline-block;padding:10px 16px;background:#2563eb;color:#fff;text-decoration:none;border-radius:8px;">Reset password</a></p>
    <p style="word-break:break-all;"><a href="{safeUrl}">{safeUrl}</a></p>
    <p style="color:#6b7280;">If you did not request a password reset, you can ignore this message.</p>
  </body>
</html>
""";
    }
}
