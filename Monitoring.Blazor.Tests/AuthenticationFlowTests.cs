using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.Identity;
using Monitoring.Blazor.Models;
using Monitoring.Blazor.Services;
using Xunit;

namespace Monitoring.Blazor.Tests;

public sealed class AuthenticationFlowTests : IAsyncLifetime
{
    private TestDbContextFactory _dbFactory = null!;
    private MemberAuthService _auth = null!;

    public async Task InitializeAsync()
    {
        _dbFactory = new TestDbContextFactory();
        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            await db.Database.EnsureCreatedAsync();
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Monitoring:DataDirectory"] = Path.Combine(Path.GetTempPath(), "opseye-tests")
            })
            .Build();
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Loopback;
        httpContext.Request.Headers.UserAgent = "OpsEye.Tests";
        var httpAccessor = new HttpContextAccessor { HttpContext = httpContext };
        var emailRepo = new EmailSettingsRepository(configuration);
        var audit = new AuditLogService(_dbFactory, httpAccessor, NullLogger<AuditLogService>.Instance);
        _auth = new MemberAuthService(
            _dbFactory,
            new PasswordHasher<MemberEntity>(),
            httpAccessor,
            DataProtectionProvider.Create("OpsEye.Tests"),
            configuration,
            emailRepo,
            audit);
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    [Fact]
    public async Task RegisterThenAuthenticate_SucceedsAndNormalizesUserName()
    {
        var member = await _auth.RegisterMemberAsync("  TestUser ", "Test User", "Strong!Password123");
        var result = await _auth.AuthenticateAsync("TESTUSER", "Strong!Password123");

        Assert.Equal("testuser", member.UserName);
        Assert.True(result.Succeeded);
        Assert.Equal("testuser", result.Member?.UserName);
    }

    [Fact]
    public async Task FailedAuthentication_FifthAttemptLocksAccount()
    {
        await _auth.RegisterMemberAsync("locked-user", "Locked User", "Strong!Password123");

        LoginResult result = default!;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            result = await _auth.AuthenticateAsync("locked-user", "Wrong!Password123");
        }

        Assert.False(result.Succeeded);
        Assert.True(result.IsLocked);
        Assert.Equal("locked", result.ErrorCode);

        var validAttempt = await _auth.AuthenticateAsync("locked-user", "Strong!Password123");
        Assert.False(validAttempt.Succeeded);
        Assert.True(validAttempt.IsLocked);
    }
}

internal sealed class TestDbContextFactory : IDbContextFactory<MonitoringDbContext>
{
    private readonly string _databaseName = $"opseye-auth-tests-{Guid.NewGuid():N}";

    public MonitoringDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<MonitoringDbContext>()
            .UseInMemoryDatabase(_databaseName)
            .Options;
        return new MonitoringDbContext(options);
    }

    public Task<MonitoringDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CreateDbContext());
    }
}
