using Microsoft.AspNetCore.Http;

namespace Monitoring.Blazor.Services;

public static class ApiAccessPolicy
{
    public static bool IsPublicIngest(PathString path) =>
        path.Equals("/api/monitor/client-message");

    public static bool RequiresAdmin(PathString path) =>
        path.StartsWithSegments("/api/admin")
        || path.StartsWithSegments("/api/settings")
        || path.StartsWithSegments("/api/security")
        || path.StartsWithSegments("/api/email")
        || path.StartsWithSegments("/api/notification")
        || path.StartsWithSegments("/api/runtime")
        || path.StartsWithSegments("/api/sql")
        || path.StartsWithSegments("/api/log-export")
        || path.StartsWithSegments("/api/log-import");
}
