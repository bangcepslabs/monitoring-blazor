using Microsoft.AspNetCore.Http;
using Monitoring.Blazor.Services;
using Xunit;

namespace Monitoring.Blazor.Tests;

public sealed class ApiAccessPolicyTests
{
    [Theory]
    [InlineData("/api/settings/backup")]
    [InlineData("/api/sql/slow-queries")]
    [InlineData("/api/runtime/ollama")]
    public void RequiresAdmin_ProtectsSensitiveRoutes(string path)
    {
        Assert.True(ApiAccessPolicy.RequiresAdmin(new PathString(path)));
    }

    [Fact]
    public void IngestEndpointIsTheOnlyPublicApiPath()
    {
        Assert.True(ApiAccessPolicy.IsPublicIngest(new PathString("/api/monitor/client-message")));
        Assert.False(ApiAccessPolicy.IsPublicIngest(new PathString("/api/monitor/all")));
    }
}
