using Monitoring.Blazor.Services;
using Xunit;

namespace Monitoring.Blazor.Tests;

public sealed class MemberAuthServiceTests
{
    [Fact]
    public void NormalizeUserName_TrimsAndLowercases()
    {
        Assert.Equal("admin", MemberAuthService.NormalizeUserName("  Admin "));
    }

    [Theory]
    [InlineData("short1!A")]
    [InlineData("alllowercase123!")]
    [InlineData("NoNumber!Password")]
    [InlineData("NoSpecial123Password")]
    public void ValidatePasswordPolicy_RejectsWeakPasswords(string password)
    {
        Assert.NotNull(MemberAuthService.ValidatePasswordPolicy("user", password));
    }

    [Fact]
    public void ValidatePasswordPolicy_AcceptsStrongPassword()
    {
        Assert.Null(MemberAuthService.ValidatePasswordPolicy("user", "Strong!Password123"));
    }
}
