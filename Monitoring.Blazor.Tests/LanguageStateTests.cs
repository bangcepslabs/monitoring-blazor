using Monitoring.Blazor.Services;
using Xunit;

namespace Monitoring.Blazor.Tests;

public sealed class LanguageStateTests
{
    [Theory]
    [InlineData("Agent recovered", "에이전트 연결 복구", "Agent recovered")]
    [InlineData("RAM", "메모리", "Memory")]
    [InlineData("log_import_runtime_update", "로그 수집 설정 변경", "Log import settings updated")]
    public void KnownSystemLabelsFollowLanguage(string code, string korean, string english)
    {
        var language = new LanguageState();
        Assert.Equal(korean, language.Label(code));
        language.SetLanguage("en");
        Assert.Equal(english, language.Label(code));
    }

    [Fact]
    public void ExistingUiMessageFollowsLanguageSwitch()
    {
        var language = new LanguageState();
        var message = language.T("파일 35개를 불러왔습니다.", "Loaded 35 files.");
        language.SetLanguage("en");
        Assert.Equal("Loaded 35 files.", language.Localize(message));
        language.SetLanguage("ko");
        Assert.Equal("파일 35개를 불러왔습니다.", language.Localize(message));
    }

    [Fact]
    public void ArbitraryLogTextIsNotPartiallyTranslated()
    {
        var language = new LanguageState();
        language.T("오류", "Error");
        const string log = "GET /api/Error?message=오류 HTTP/1.1";
        Assert.Equal(log, language.Localize(log));
        language.SetLanguage("en");
        Assert.Equal(log, language.Localize(log));
    }

    [Fact]
    public void LanguageChangeNotifiesOnlyWhenSelectionChanges()
    {
        var language = new LanguageState();
        var events = 0;
        language.Changed += () => events++;
        language.SetLanguage("ko");
        language.SetLanguage("en");
        language.SetLanguage("en");
        Assert.Equal(1, events);
    }

    [Fact]
    public void DependencyPresentationPreservesPathsAndDiagnostics()
    {
        var language = new LanguageState();
        var entry = new DependencyStatusEntry("Paths", "Ready", "Log: C:\\Logs\\Error | Output: D:\\Reports", true);
        Assert.Equal("로그: C:\\Logs\\Error | 내보내기: D:\\Reports", DependencyStatusText.Details(entry, language));
        language.SetLanguage("en");
        Assert.Equal(entry.Details, DependencyStatusText.Details(entry, language));
        const string diagnostic = "SQL timeout (Error 258)";
        Assert.Equal(diagnostic, DependencyStatusText.Details(entry with { Name = "Database", Details = diagnostic }, language));
    }
}
