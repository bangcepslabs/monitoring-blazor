namespace Monitoring.Blazor.Services;

public sealed class LanguageState
{
    private string _language = "en";

    public event Action? Changed;

    public string Code => _language;

    public bool IsKorean => string.Equals(_language, "ko", StringComparison.OrdinalIgnoreCase);

    public void SetLanguage(string? language)
    {
        var next = string.Equals(language, "ko", StringComparison.OrdinalIgnoreCase) ? "ko" : "en";
        if (string.Equals(_language, next, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _language = next;
        Changed?.Invoke();
    }

    public string T(string korean, string english) => IsKorean ? korean : english;
}
