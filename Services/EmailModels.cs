namespace Monitoring.Blazor.Services;

public sealed class EmailSettings
{
    public bool Enabled { get; set; } = false;
    public bool VerificationEnabled { get; set; } = false;
    public bool PasswordResetEnabled { get; set; } = false;
    public string SmtpHost { get; set; } = string.Empty;
    public int SmtpPort { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public bool UseDefaultCredentials { get; set; } = false;
    public string? SmtpUsername { get; set; }
    public string? SmtpPassword { get; set; }
    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = "OpsEye";
    public string VerificationSubject { get; set; } = "Confirm your email address";
}
