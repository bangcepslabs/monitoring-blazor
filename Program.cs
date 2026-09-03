using Monitoring.Blazor.Components;
using Monitoring.Blazor.Models;
using Monitoring.Blazor.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);


builder.Configuration.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: true);
builder.Configuration.AddJsonFile("appsettings.private.json", optional: true, reloadOnChange: true);
builder.Configuration.AddEnvironmentVariables();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthorization();
builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.Cookie.Name = ".OpsEye.Session";
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.IdleTimeout = TimeSpan.FromHours(8);
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = ".OpsEye.Auth";
        options.LoginPath = "/login";
        options.LogoutPath = "/auth/logout";
        options.AccessDeniedPath = "/login";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Events = new CookieAuthenticationEvents
        {
            OnValidatePrincipal = async context =>
            {
                var sessionId = context.Principal?.FindFirstValue("sid");
                var memberIdValue = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrWhiteSpace(sessionId) || !long.TryParse(memberIdValue, out var memberId))
                {
                    context.RejectPrincipal();
                    await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                    return;
                }

                var dbFactory = context.HttpContext.RequestServices.GetRequiredService<IDbContextFactory<MonitoringDbContext>>();
                await using var db = await dbFactory.CreateDbContextAsync(context.HttpContext.RequestAborted);
                var nowUtc = DateTime.UtcNow;
                var session = await db.MemberSessions.FirstOrDefaultAsync(x => x.SessionId == sessionId, context.HttpContext.RequestAborted);
                var member = await db.Members.FirstOrDefaultAsync(x => x.Id == memberId, context.HttpContext.RequestAborted);
                if (session is null || member is null || !member.IsActive || session.MemberId != member.Id || session.RevokedUtc is not null || session.ExpiresUtc <= nowUtc)
                {
                    context.RejectPrincipal();
                    await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                    return;
                }

                session.LastSeenUtc = nowUtc;
                await db.SaveChangesAsync(context.HttpContext.RequestAborted);
            }
        };
    });
builder.Services.AddHttpClient(nameof(SystemInfoCollector));
builder.Services.AddHttpClient<IpApiService>();
builder.Services.AddHttpClient(nameof(OllamaClient), client =>
{
    client.Timeout = TimeSpan.FromMinutes(5);
});
builder.Services.AddSingleton<SystemInfoCollector>();
builder.Services.AddSingleton<AlertEvaluator>();
builder.Services.AddSingleton<AlertDispatcher>();
builder.Services.AddSingleton<AlertSuppressor>();
builder.Services.AddSingleton<AlertSettingsRepository>();
builder.Services.AddSingleton<SecuritySettingsRepository>();
builder.Services.AddSingleton<EmailSettingsRepository>();
builder.Services.AddSingleton<NotificationSettingsRepository>();
builder.Services.AddScoped<LanguageState>();
builder.Services.AddSingleton<RuntimeSettingsRepository>();
builder.Services.AddSingleton<SqlTargetSettingsRepository>();
builder.Services.AddSingleton<ServerCatalogRepository>();
builder.Services.AddSingleton<SettingsBackupService>();
builder.Services.AddSingleton<EventTimelineService>();
builder.Services.AddSingleton<DependencyStatusService>();
builder.Services.AddSingleton<JavaLogParserService>();
builder.Services.AddSingleton<OllamaClient>();
builder.Services.AddSingleton<OllamaAnalysisStore>();
builder.Services.AddSingleton<LogAutoExportService>();
builder.Services.AddSingleton<LogAutoImportService>();
builder.Services.AddSingleton<MonitoringSnapshotQueue>();
builder.Services.AddSingleton<MonitorStateService>();
builder.Services.AddSingleton<AuditLogService>();
builder.Services.AddSingleton<SlowQueryService>();
builder.Services.AddSingleton<EmailSenderService>();
builder.Services.AddSingleton<SettingsBackupArchiveService>();
builder.Services.AddSingleton<IPasswordHasher<MemberEntity>, PasswordHasher<MemberEntity>>();
builder.Services.AddScoped<MemberAuthService>();
builder.Services.AddDbContextFactory<MonitoringDbContext>(options =>
{
    var connString = builder.Configuration.GetConnectionString("MonitoringDb");
    if (string.IsNullOrWhiteSpace(connString))
    {
        throw new InvalidOperationException("ConnectionStrings:MonitoringDb is required for MSSQL.");
    }

    options.UseSqlServer(connString);
});
builder.Services.AddHostedService<ServerMonitorWorker>();
builder.Services.AddHostedService<MonitoringSnapshotWorker>();
builder.Services.AddHostedService<AlertWorker>();
builder.Services.AddHostedService<DataRetentionWorker>();
builder.Services.AddHostedService<LogAutoExportWorker>();
builder.Services.AddHostedService<LogAutoImportWorker>();
builder.Services.AddHostedService<SettingsBackupWorker>();

var app = builder.Build();

const string PendingTwoFactorMemberIdKey = "pending_2fa_member_id";
const string PendingTwoFactorRememberMeKey = "pending_2fa_remember_me";
const string PendingTwoFactorReturnUrlKey = "pending_2fa_return_url";
const string PendingTwoFactorUserNameKey = "pending_2fa_user_name";


using (var scope = app.Services.CreateScope())
{
    try
    {
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<MonitoringDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.Database.EnsureCreatedAsync();
        await db.Database.ExecuteSqlRawAsync(SqlScripts.EnsureLogIpDailyStatsTable);
        await db.Database.ExecuteSqlRawAsync(SqlScripts.EnsureMembersTable);
        await db.Database.ExecuteSqlRawAsync(SqlScripts.EnsureMemberAuditLogsTable);
        await db.Database.ExecuteSqlRawAsync(SqlScripts.EnsureMemberLoginAttemptsTable);
        await db.Database.ExecuteSqlRawAsync(SqlScripts.EnsureMemberSessionsTable);
        await db.Database.ExecuteSqlRawAsync(SqlScripts.EnsurePasswordResetRequestsTable);
        await db.Database.ExecuteSqlRawAsync(SqlScripts.EnsureMemberRecoveryCodesTable);
        await db.Database.ExecuteSqlRawAsync(SqlScripts.EnsureEmailVerificationRequestsTable);
    }
    catch (Exception exception)
    {
        try
        {
            app.Logger.LogWarning(exception, "Database initialization failed. The application will continue without database access.");
        }
        catch
        {
            Console.Error.WriteLine("Database initialization failed. The application will continue without database access.");
            Console.Error.WriteLine(exception);
        }
    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.Map("/health", healthApp =>
{
    healthApp.Run(async context =>
    {
        context.Response.ContentType = "text/plain";
        await context.Response.WriteAsync("ok");
    });
});

app.MapPost("/auth/bootstrap", async (HttpRequest request, HttpContext context, MemberAuthService memberAuth, IDbContextFactory<MonitoringDbContext> dbFactory, EmailSenderService emailSender, CancellationToken ct) =>
{
    var result = await ProcessRegisterAsync(request, context, memberAuth, dbFactory, emailSender, ct, bootstrapOnly: true);
    return result;
}).DisableAntiforgery().AllowAnonymous().RequireRateLimiting("auth");

app.MapPost("/auth/login", async (HttpRequest request, HttpContext context, MemberAuthService memberAuth, IDbContextFactory<MonitoringDbContext> dbFactory, SecuritySettingsRepository securitySettingsRepository, IConfiguration configuration, CancellationToken ct) =>
{
    if (!request.HasFormContentType)
    {
        return Results.BadRequest("Expected form post.");
    }

    var form = await request.ReadFormAsync(ct);
    var userName = form["userName"].ToString();
    var password = form["password"].ToString();
    var rememberMe = form.ContainsKey("rememberMe");
    var returnUrl = NormalizeReturnUrl(form["returnUrl"].ToString());

    if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
    {
        await WriteMemberAuditAsync(dbFactory, "anonymous", MemberAuthService.NormalizeUserName(userName), "login", false, "required fields missing", ct);
        return Results.Redirect($"/login?error=required{returnUrlQuery(returnUrl)}");
    }

    var loginResult = await memberAuth.AuthenticateAsync(userName, password, ct);
    if (!loginResult.Succeeded || loginResult.Member is null)
    {
        await WriteMemberAuditAsync(dbFactory, MemberAuthService.NormalizeUserName(userName), MemberAuthService.NormalizeUserName(userName), "login", false, loginResult.ErrorCode ?? "invalid credentials", ct);
        var errorCode = loginResult.ErrorCode ?? "invalid";
        return Results.Redirect($"/login?error={errorCode}{returnUrlQuery(returnUrl)}");
    }

    var baseSecuritySettings = configuration.GetSection("Monitoring:Security").Get<SecuritySettings>() ?? new SecuritySettings();
    var securitySettings = securitySettingsRepository.Load(baseSecuritySettings);
    if (securitySettings.TwoFactorEnabled && loginResult.Member.TwoFactorEnabled)
    {
        context.Session.SetString(PendingTwoFactorMemberIdKey, loginResult.Member.Id.ToString(CultureInfo.InvariantCulture));
        context.Session.SetString(PendingTwoFactorRememberMeKey, rememberMe ? "true" : "false");
        context.Session.SetString(PendingTwoFactorReturnUrlKey, returnUrl);
        context.Session.SetString(PendingTwoFactorUserNameKey, loginResult.Member.UserName);
        return Results.Redirect($"/two-factor?returnUrl={Uri.EscapeDataString(returnUrl)}");
    }

    var sessionId = await memberAuth.CreateSessionAsync(loginResult.Member, rememberMe, ct);
    await SignInMemberAsync(context, loginResult.Member, rememberMe, sessionId);
    await memberAuth.UpdateLastLoginUtcAsync(loginResult.Member.Id, DateTime.UtcNow, ct);
    await WriteMemberAuditAsync(dbFactory, loginResult.Member.UserName, loginResult.Member.UserName, "login", true, rememberMe ? "remember me" : "session", ct);
    return Results.Redirect(returnUrl);
}).DisableAntiforgery().AllowAnonymous().RequireRateLimiting("auth");

app.MapPost("/auth/two-factor", async (HttpRequest request, HttpContext context, MemberAuthService memberAuth, IDbContextFactory<MonitoringDbContext> dbFactory, CancellationToken ct) =>
{
    if (!request.HasFormContentType)
    {
        return Results.BadRequest("Expected form post.");
    }

    var form = await request.ReadFormAsync(ct);
    var code = form["code"].ToString();
    var returnUrl = NormalizeReturnUrl(form["returnUrl"].ToString());
    var pendingMemberId = context.Session.GetString(PendingTwoFactorMemberIdKey);
    var pendingUserName = context.Session.GetString(PendingTwoFactorUserNameKey);
    var rememberMe = string.Equals(context.Session.GetString(PendingTwoFactorRememberMeKey), "true", StringComparison.OrdinalIgnoreCase);
    var pendingReturnUrl = NormalizeReturnUrl(context.Session.GetString(PendingTwoFactorReturnUrlKey));

    if (string.IsNullOrWhiteSpace(pendingMemberId) || !long.TryParse(pendingMemberId, out var memberId))
    {
        return Results.Redirect("/login?error=invalid");
    }

    var verified = await memberAuth.VerifyTwoFactorCodeAsync(memberId, code, ct) ||
                   await memberAuth.VerifyTwoFactorRecoveryCodeAsync(memberId, code, ct);
    if (!verified)
    {
        await WriteMemberAuditAsync(dbFactory, pendingUserName ?? "anonymous", pendingUserName, "two_factor", false, "invalid code", ct);
        return Results.Redirect($"/two-factor?error=invalid{returnUrlQuery(returnUrl)}");
    }

    var member = await memberAuth.GetMemberByIdAsync(memberId, ct);
    if (member is null)
    {
        return Results.Redirect("/login?error=invalid");
    }

    var sessionId = await memberAuth.CreateSessionAsync(member, rememberMe, ct);
    await SignInMemberAsync(context, member, rememberMe, sessionId);
    await memberAuth.UpdateLastLoginUtcAsync(member.Id, DateTime.UtcNow, ct);
    context.Session.Remove(PendingTwoFactorMemberIdKey);
    context.Session.Remove(PendingTwoFactorRememberMeKey);
    context.Session.Remove(PendingTwoFactorReturnUrlKey);
    context.Session.Remove(PendingTwoFactorUserNameKey);
    await WriteMemberAuditAsync(dbFactory, member.UserName, member.UserName, "two_factor", true, "verified", ct);
    return Results.Redirect(string.IsNullOrWhiteSpace(pendingReturnUrl) ? returnUrl : pendingReturnUrl);
}).DisableAntiforgery().AllowAnonymous().RequireRateLimiting("auth");

app.MapPost("/auth/register", async (HttpRequest request, HttpContext context, MemberAuthService memberAuth, IDbContextFactory<MonitoringDbContext> dbFactory, EmailSenderService emailSender, CancellationToken ct) =>
{
    var result = await ProcessRegisterAsync(request, context, memberAuth, dbFactory, emailSender, ct, bootstrapOnly: false);
    return result;
}).DisableAntiforgery().AllowAnonymous().RequireRateLimiting("auth");

app.MapPost("/auth/logout", async (HttpContext context, IDbContextFactory<MonitoringDbContext> dbFactory) =>
{
    var actor = context.User.Identity?.Name ?? "anonymous";
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    await WriteMemberAuditAsync(dbFactory, actor, actor, "logout", true, null, CancellationToken.None);
    return Results.Redirect("/login");
}).DisableAntiforgery();

app.UseStatusCodePagesWithReExecute("/not-found");
var useHttpsRedirection = app.Configuration.GetValue("Monitoring:UseHttpsRedirection", true);
if (useHttpsRedirection)
{
    app.UseHttpsRedirection();
}
app.UseSession();
app.UseAuthentication();
app.UseRateLimiter();
app.Use(async (context, next) =>
{
    var path = context.Request.Path;
    var acceptsHtml = context.Request.GetTypedHeaders().Accept?
        .Any(value => string.Equals(value.MediaType.Value, "text/html", StringComparison.OrdinalIgnoreCase)) == true;

    if (context.User.Identity?.IsAuthenticated != true &&
        HttpMethods.IsGet(context.Request.Method) &&
        acceptsHtml &&
        !path.StartsWithSegments("/login") &&
        !path.StartsWithSegments("/register") &&
        !path.StartsWithSegments("/api") &&
        !path.StartsWithSegments("/auth"))
    {
        var returnUrl = $"{context.Request.PathBase}{path}{context.Request.QueryString}";
        context.Response.Redirect($"/login?returnUrl={Uri.EscapeDataString(returnUrl)}");
        return;
    }

    await next();
});
app.UseAuthorization();

app.UseAntiforgery();

app.MapStaticAssets().AllowAnonymous();

app.MapGet("/api/monitor/all", (MonitorStateService state) =>
{
    var data = state.GetSnapshot(TimeSpan.FromSeconds(15));
    return Results.Ok(data);
});

app.MapPost("/api/monitor/client-message", async (HttpRequest request, MonitorStateService state) =>
{
    using var reader = new StreamReader(request.Body);
    var json = await reader.ReadToEndAsync();
    if (string.IsNullOrWhiteSpace(json))
    {
        return Results.BadRequest("Empty payload.");
    }

    return state.TryUpdateClientFromJson(json)
        ? Results.Ok()
        : Results.BadRequest("Invalid payload.");
});

app.MapPost("/api/monitor/trigger-refresh", (MonitorStateService state) =>
{
    var data = state.GetSnapshot(TimeSpan.FromSeconds(15)).Select(x => x.Info).ToList();
    return Results.Ok(new { data });
});
app.MapPost("/trigger-refresh", (MonitorStateService state) =>
{
    var data = state.GetSnapshot(TimeSpan.FromSeconds(15)).Select(x => x.Info).ToList();
    return Results.Ok(new { data });
});

app.MapPost("/api/monitor/parse-logs", async (HttpRequest request, IDbContextFactory<MonitoringDbContext> dbFactory) =>
{
    return await ProcessLogUploadAsync(request, dbFactory);
});
app.MapPost("/parse-logs", async (HttpRequest request, IDbContextFactory<MonitoringDbContext> dbFactory) =>
{
    return await ProcessLogUploadAsync(request, dbFactory);
});

app.MapPost("/api/log-export/run", async (LogAutoExportService exportService, CancellationToken ct) =>
{
    var message = await exportService.RunOnceAsync(ct);
    return Results.Ok(new { message });
});

app.MapPost("/api/log-import/run", async (LogAutoImportService importService, CancellationToken ct) =>
{
    var message = await importService.RunOnceAsync(ct);
    return Results.Ok(new { message });
});

app.MapPost("/api/ollama/analyze", async (HttpRequest request, OllamaClient client, OllamaAnalysisStore store, CancellationToken ct) =>
{
    var payload = await request.ReadFromJsonAsync<OllamaAnalyzeRequest>(cancellationToken: ct);
    if (payload is null || string.IsNullOrWhiteSpace(payload.Prompt))
    {
        return Results.BadRequest("prompt required");
    }

    var effectivePrompt = BuildEffectiveOllamaPrompt(payload.Prompt, payload.UserQuestion);
    var response = await client.GenerateAsync(effectivePrompt, payload.SystemPrompt, ct);
    var entry = new OllamaAnalysisEntry
    {
        TimestampUtc = DateTime.UtcNow,
        Source = payload.Source ?? "manual",
        LogDate = payload.LogDate,
        TotalRows = payload.TotalRows ?? 0,
        Status5xxCount = payload.Status5xxCount ?? 0,
        Status5xxRatio = payload.Status5xxRatio ?? 0,
        Prompt = payload.Prompt,
        Response = response
    };
    if (!IsOllamaDisabledResponse(response))
    {
        await store.SaveAsync(entry, ct);
    }
    return Results.Ok(new { response });
});

app.MapPost("/api/ollama/analyze-async", async (HttpRequest request, OllamaClient client, OllamaAnalysisStore store) =>
{
    var payload = await request.ReadFromJsonAsync<OllamaAnalyzeRequest>();
    if (payload is null || string.IsNullOrWhiteSpace(payload.Prompt))
    {
        return Results.BadRequest("prompt required");
    }

    _ = Task.Run(async () =>
    {
        try
        {
            var effectivePrompt = BuildEffectiveOllamaPrompt(payload.Prompt, payload.UserQuestion);
            var response = await client.GenerateAsync(effectivePrompt, payload.SystemPrompt, CancellationToken.None);
            var entry = new OllamaAnalysisEntry
            {
                TimestampUtc = DateTime.UtcNow,
                Source = payload.Source ?? "manual",
                LogDate = payload.LogDate,
                TotalRows = payload.TotalRows ?? 0,
                Status5xxCount = payload.Status5xxCount ?? 0,
                Status5xxRatio = payload.Status5xxRatio ?? 0,
                Prompt = payload.Prompt,
                Response = response
            };
            if (!IsOllamaDisabledResponse(response))
            {
                await store.SaveAsync(entry, CancellationToken.None);
            }
        }
        catch
        {
            // Swallow background errors to avoid crashing the host.
        }
    });

    return Results.Accepted();
});

app.MapPost("/api/ollama/stream", async (HttpRequest request, OllamaClient client, OllamaAnalysisStore store, CancellationToken ct) =>
{
    var payload = await request.ReadFromJsonAsync<OllamaAnalyzeRequest>(cancellationToken: ct);
    if (payload is null || string.IsNullOrWhiteSpace(payload.Prompt))
    {
        return Results.BadRequest("prompt required");
    }

    return Results.Stream(async stream =>
    {
        var builder = new System.Text.StringBuilder();
        try
        {
            var effectivePrompt = BuildEffectiveOllamaPrompt(payload.Prompt, payload.UserQuestion);
            await foreach (var chunk in client.StreamAsync(effectivePrompt, payload.SystemPrompt, ct))
            {
                builder.Append(chunk);
                var bytes = System.Text.Encoding.UTF8.GetBytes(chunk);
                await stream.WriteAsync(bytes, ct);
                await stream.FlushAsync(ct);
            }

            var entry = new OllamaAnalysisEntry
            {
                TimestampUtc = DateTime.UtcNow,
                Source = payload.Source ?? "manual",
                LogDate = payload.LogDate,
                TotalRows = payload.TotalRows ?? 0,
                Status5xxCount = payload.Status5xxCount ?? 0,
                Status5xxRatio = payload.Status5xxRatio ?? 0,
                Prompt = payload.Prompt,
                Response = builder.ToString()
            };
            if (!IsOllamaDisabledResponse(entry.Response))
            {
                await store.SaveAsync(entry, CancellationToken.None);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            var message = $"AI 遺꾩꽍 ?ㅽ뙣: {exception.Message}";
            var bytes = System.Text.Encoding.UTF8.GetBytes(message);
            await stream.WriteAsync(bytes, ct);
            await stream.FlushAsync(ct);
        }
    }, "text/plain; charset=utf-8");
});

app.MapGet("/api/ollama/latest", (OllamaAnalysisStore store) =>
{
    var latest = store.LoadLatest();
    return latest is null
        ? Results.Text("null", "application/json")
        : Results.Json(latest);
});

app.MapGet("/api/ollama/history", (OllamaAnalysisStore store, int? take) =>
{
    var items = store.LoadRecent(Math.Clamp(take ?? 10, 1, 50));
    return Results.Ok(items);
});

static bool IsOllamaDisabledResponse(string? response)
{
    return string.Equals(response?.Trim(), "Ollama is disabled in configuration.", StringComparison.OrdinalIgnoreCase);
}

app.MapGet("/api/monitor/history", async (string hostname, int? minutes, IDbContextFactory<MonitoringDbContext> dbFactory) =>
{
    if (string.IsNullOrWhiteSpace(hostname))
    {
        return Results.BadRequest("hostname required");
    }

    var windowMinutes = minutes is null or <= 0 ? 60 : minutes.Value;
    var since = DateTime.UtcNow.AddMinutes(-windowMinutes);

    await using var db = await dbFactory.CreateDbContextAsync();
    var data = await db.HostSnapshots
        .Where(x => x.Hostname == hostname && x.CreatedUtc >= since)
        .OrderBy(x => x.CreatedUtc)
        .Select(x => new
        {
            x.CreatedUtc,
            x.CpuUsage,
            x.MemoryUsage,
            x.DiskUsage,
            x.SentMbps,
            x.RecvMbps,
            x.Status
        })
        .ToListAsync();

    return Results.Ok(new { hostname, windowMinutes, data });
});

app.MapGet("/api/alerts/history", async (string? hostname, int? take, IDbContextFactory<MonitoringDbContext> dbFactory) =>
{
    var limit = take is null or <= 0 ? 200 : Math.Min(take.Value, 1000);

    await using var db = await dbFactory.CreateDbContextAsync();
    var query = db.AlertEvents.AsQueryable();
    if (!string.IsNullOrWhiteSpace(hostname))
    {
        query = query.Where(x => x.Hostname == hostname);
    }

    var data = await query
        .OrderByDescending(x => x.TimestampUtc)
        .Take(limit)
        .ToListAsync();

    return Results.Ok(new { hostname, take = limit, data });
});

app.MapGet("/api/alerts/suppressions", (AlertSuppressor suppressor) =>
{
    return Results.Ok(suppressor.List());
});

app.MapPost("/api/alerts/mute", async (HttpRequest request, AlertSuppressor suppressor) =>
{
    var payload = await request.ReadFromJsonAsync<SuppressionRequest>();
    if (payload is null || string.IsNullOrWhiteSpace(payload.Hostname) || string.IsNullOrWhiteSpace(payload.Metric))
    {
        return Results.BadRequest("hostname and metric required");
    }

    var minutes = payload.Minutes <= 0 ? 60 : payload.Minutes;
    suppressor.Set(payload.Hostname, payload.Metric, DateTime.UtcNow.AddMinutes(minutes), payload.Reason ?? "mute", "mute");
    return Results.Ok();
});

app.MapPost("/api/alerts/ack", async (HttpRequest request, AlertSuppressor suppressor) =>
{
    var payload = await request.ReadFromJsonAsync<SuppressionRequest>();
    if (payload is null || string.IsNullOrWhiteSpace(payload.Hostname) || string.IsNullOrWhiteSpace(payload.Metric))
    {
        return Results.BadRequest("hostname and metric required");
    }

    var minutes = payload.Minutes <= 0 ? 60 : payload.Minutes;
    suppressor.Set(payload.Hostname, payload.Metric, DateTime.UtcNow.AddMinutes(minutes), payload.Reason ?? "ack", "ack");
    return Results.Ok();
});

app.MapPost("/api/alerts/unmute", async (HttpRequest request, AlertSuppressor suppressor) =>
{
    var payload = await request.ReadFromJsonAsync<SuppressionRequest>();
    if (payload is null || string.IsNullOrWhiteSpace(payload.Hostname) || string.IsNullOrWhiteSpace(payload.Metric))
    {
        return Results.BadRequest("hostname and metric required");
    }

    return suppressor.Clear(payload.Hostname, payload.Metric) ? Results.Ok() : Results.NotFound();
});

app.MapGet("/api/alerts/settings", (AlertSettingsRepository repo, IConfiguration config) =>
{
    var baseSettings = config.GetSection("Monitoring:Alerts").Get<AlertSettings>() ?? new AlertSettings();
    return Results.Ok(repo.Load(baseSettings));
});


app.MapPost("/api/alerts/settings", async (HttpRequest request, HttpContext context, AlertSettingsRepository repo, AuditLogService auditLogService) =>
{
    var settings = await request.ReadFromJsonAsync<AlertSettings>();
    if (settings is null)
    {
        return Results.BadRequest("Invalid settings");
    }

    var baseSettings = context.RequestServices.GetRequiredService<IConfiguration>().GetSection("Monitoring:Alerts").Get<AlertSettings>() ?? new AlertSettings();
    var before = repo.Load(baseSettings);
    repo.Save(settings);
    await auditLogService.WriteChangeAsync(
        "alert_settings_update",
        null,
        [
            new AuditChange("Enabled", before.Enabled.ToString(), settings.Enabled.ToString()),
            new AuditChange("CpuThreshold", before.CpuThreshold.ToString(CultureInfo.InvariantCulture), settings.CpuThreshold.ToString(CultureInfo.InvariantCulture)),
            new AuditChange("RamThreshold", before.RamThreshold.ToString(CultureInfo.InvariantCulture), settings.RamThreshold.ToString(CultureInfo.InvariantCulture)),
            new AuditChange("DiskThreshold", before.DiskThreshold.ToString(CultureInfo.InvariantCulture), settings.DiskThreshold.ToString(CultureInfo.InvariantCulture)),
            new AuditChange("CooldownMinutes", before.CooldownMinutes.ToString(CultureInfo.InvariantCulture), settings.CooldownMinutes.ToString(CultureInfo.InvariantCulture)),
            new AuditChange("RecoveryEnabled", before.RecoveryEnabled.ToString(), settings.RecoveryEnabled.ToString()),
            new AuditChange("AnomalyEnabled", before.AnomalyEnabled.ToString(), settings.AnomalyEnabled.ToString()),
            new AuditChange("MaintenanceWindowEnabled", before.MaintenanceWindowEnabled.ToString(), settings.MaintenanceWindowEnabled.ToString())
        ],
        true,
        "Alert settings updated",
        context.RequestAborted);
    return Results.Ok();
});

app.MapGet("/api/email/settings", (EmailSettingsRepository repo, IConfiguration config) =>
{
    var baseSettings = config.GetSection("Monitoring:Email").Get<EmailSettings>() ?? new EmailSettings();
    return Results.Ok(repo.Load(baseSettings));
});

app.MapPost("/api/email/settings", async (HttpRequest request, HttpContext context, EmailSettingsRepository repo, AuditLogService auditLogService) =>
{
    var settings = await request.ReadFromJsonAsync<EmailSettings>();
    if (settings is null)
    {
        return Results.BadRequest("Invalid settings");
    }

    var baseSettings = context.RequestServices.GetRequiredService<IConfiguration>().GetSection("Monitoring:Email").Get<EmailSettings>() ?? new EmailSettings();
    var before = repo.Load(baseSettings);
    repo.Save(settings);
    await auditLogService.WriteChangeAsync(
        "email_settings_update",
        null,
        [
            new AuditChange("Enabled", before.Enabled.ToString(), settings.Enabled.ToString()),
            new AuditChange("VerificationEnabled", before.VerificationEnabled.ToString(), settings.VerificationEnabled.ToString()),
            new AuditChange("PasswordResetEnabled", before.PasswordResetEnabled.ToString(), settings.PasswordResetEnabled.ToString()),
            new AuditChange("SmtpHost", before.SmtpHost, settings.SmtpHost),
            new AuditChange("SmtpPort", before.SmtpPort.ToString(CultureInfo.InvariantCulture), settings.SmtpPort.ToString(CultureInfo.InvariantCulture)),
            new AuditChange("EnableSsl", before.EnableSsl.ToString(), settings.EnableSsl.ToString()),
            new AuditChange("UseDefaultCredentials", before.UseDefaultCredentials.ToString(), settings.UseDefaultCredentials.ToString()),
            new AuditChange("FromAddress", before.FromAddress, settings.FromAddress),
            new AuditChange("FromName", before.FromName, settings.FromName)
        ],
        true,
        "Email settings updated",
        context.RequestAborted);
    return Results.Ok();
});

app.MapGet("/api/notification/settings", (NotificationSettingsRepository repo, IConfiguration config) =>
{
    var baseSettings = config.GetSection("Monitoring:Notifications").Get<NotificationSettings>() ?? new NotificationSettings();
    return Results.Ok(repo.Load(baseSettings));
});

app.MapPost("/api/notification/settings", async (HttpRequest request, HttpContext context, NotificationSettingsRepository repo, AuditLogService auditLogService) =>
{
    var settings = await request.ReadFromJsonAsync<NotificationSettings>();
    if (settings is null)
    {
        return Results.BadRequest("Invalid settings");
    }

    var baseSettings = context.RequestServices.GetRequiredService<IConfiguration>().GetSection("Monitoring:Notifications").Get<NotificationSettings>() ?? new NotificationSettings();
    var before = repo.Load(baseSettings);
    repo.Save(settings);
    await auditLogService.WriteChangeAsync(
        "notification_settings_update",
        null,
        [
            new AuditChange("Enabled", before.Enabled.ToString(), settings.Enabled.ToString()),
            new AuditChange("SlackEnabled", before.SlackEnabled.ToString(), settings.SlackEnabled.ToString()),
            new AuditChange("SlackWebhookUrl", before.SlackWebhookUrl, settings.SlackWebhookUrl),
            new AuditChange("TeamsEnabled", before.TeamsEnabled.ToString(), settings.TeamsEnabled.ToString()),
            new AuditChange("TeamsWebhookUrl", before.TeamsWebhookUrl, settings.TeamsWebhookUrl),
            new AuditChange("DoorayEnabled", before.DoorayEnabled.ToString(), settings.DoorayEnabled.ToString()),
            new AuditChange("DoorayChannelId", before.DoorayChannelId, settings.DoorayChannelId),
            new AuditChange("DoorayApiToken", before.DoorayApiToken, settings.DoorayApiToken),
            new AuditChange("DiscordEnabled", before.DiscordEnabled.ToString(), settings.DiscordEnabled.ToString()),
            new AuditChange("KakaoWorkEnabled", before.KakaoWorkEnabled.ToString(), settings.KakaoWorkEnabled.ToString()),
            new AuditChange("WebhookEnabled", before.WebhookEnabled.ToString(), settings.WebhookEnabled.ToString()),
            new AuditChange("WebhookUrl", before.WebhookUrl, settings.WebhookUrl)
        ],
        true,
        "Notification settings updated",
        context.RequestAborted);
    return Results.Ok();
});

app.MapGet("/api/runtime/ollama", (RuntimeSettingsRepository repo, IConfiguration config) =>
{
    var baseSettings = config.GetSection("Monitoring:Ollama").Get<OllamaRuntimeSettings>() ?? new OllamaRuntimeSettings();
    return Results.Ok(repo.LoadOllama(baseSettings));
});

app.MapPost("/api/runtime/ollama", async (HttpRequest request, HttpContext context, RuntimeSettingsRepository repo, AuditLogService auditLogService) =>
{
    var settings = await request.ReadFromJsonAsync<OllamaRuntimeSettings>();
    if (settings is null)
    {
        return Results.BadRequest("Invalid settings");
    }

    var baseSettings = context.RequestServices.GetRequiredService<IConfiguration>().GetSection("Monitoring:Ollama").Get<OllamaRuntimeSettings>() ?? new OllamaRuntimeSettings();
    var before = repo.LoadOllama(baseSettings);
    repo.SaveOllama(settings);
    await auditLogService.WriteChangeAsync(
        "ollama_runtime_update",
        null,
        [
            new AuditChange("Enabled", before.Enabled.ToString(), settings.Enabled.ToString()),
            new AuditChange("BaseUrl", before.BaseUrl, settings.BaseUrl),
            new AuditChange("Model", before.Model, settings.Model),
            new AuditChange("TimeoutSeconds", before.TimeoutSeconds.ToString(CultureInfo.InvariantCulture), settings.TimeoutSeconds.ToString(CultureInfo.InvariantCulture)),
            new AuditChange("AutoAnalyzeEnabled", before.AutoAnalyzeEnabled.ToString(), settings.AutoAnalyzeEnabled.ToString()),
            new AuditChange("AutoAnalyzeStatus5xxRatio", before.AutoAnalyzeStatus5xxRatio.ToString(CultureInfo.InvariantCulture), settings.AutoAnalyzeStatus5xxRatio.ToString(CultureInfo.InvariantCulture)),
            new AuditChange("AnalysisOutputFolder", before.AnalysisOutputFolder, settings.AnalysisOutputFolder)
        ],
        true,
        "Ollama runtime settings updated",
        context.RequestAborted);
    return Results.Ok();
});

app.MapGet("/api/runtime/log-export", (RuntimeSettingsRepository repo, IConfiguration config) =>
{
    var baseSettings = config.GetSection("Monitoring:LogExport").Get<LogExportRuntimeSettings>() ?? new LogExportRuntimeSettings();
    return Results.Ok(repo.LoadLogExport(baseSettings));
});

app.MapPost("/api/runtime/log-export", async (HttpRequest request, HttpContext context, RuntimeSettingsRepository repo, AuditLogService auditLogService) =>
{
    var settings = await request.ReadFromJsonAsync<LogExportRuntimeSettings>();
    if (settings is null)
    {
        return Results.BadRequest("Invalid settings");
    }

    var baseSettings = context.RequestServices.GetRequiredService<IConfiguration>().GetSection("Monitoring:LogExport").Get<LogExportRuntimeSettings>() ?? new LogExportRuntimeSettings();
    var before = repo.LoadLogExport(baseSettings);
    repo.SaveLogExport(settings);
    await auditLogService.WriteChangeAsync(
        "log_export_runtime_update",
        null,
        [
            new AuditChange("Enabled", before.Enabled.ToString(), settings.Enabled.ToString()),
            new AuditChange("LogFolder", before.LogFolder, settings.LogFolder),
            new AuditChange("OutputFolder", before.OutputFolder, settings.OutputFolder),
            new AuditChange("RunHour", before.RunHour.ToString(CultureInfo.InvariantCulture), settings.RunHour.ToString(CultureInfo.InvariantCulture)),
            new AuditChange("RunMinute", before.RunMinute.ToString(CultureInfo.InvariantCulture), settings.RunMinute.ToString(CultureInfo.InvariantCulture)),
            new AuditChange("TargetDateOffsetDays", before.TargetDateOffsetDays.ToString(CultureInfo.InvariantCulture), settings.TargetDateOffsetDays.ToString(CultureInfo.InvariantCulture))
        ],
        true,
        "Log export runtime settings updated",
        context.RequestAborted);
    return Results.Ok();
});

app.MapGet("/api/runtime/log-import", (RuntimeSettingsRepository repo, IConfiguration config) =>
{
    var baseSettings = config.GetSection("Monitoring:LogImport").Get<LogImportRuntimeSettings>() ?? new LogImportRuntimeSettings();
    return Results.Ok(repo.LoadLogImport(baseSettings));
});

app.MapPost("/api/runtime/log-import", async (HttpRequest request, HttpContext context, RuntimeSettingsRepository repo, AuditLogService auditLogService) =>
{
    var settings = await request.ReadFromJsonAsync<LogImportRuntimeSettings>();
    if (settings is null)
    {
        return Results.BadRequest("Invalid settings");
    }

    var baseSettings = context.RequestServices.GetRequiredService<IConfiguration>().GetSection("Monitoring:LogImport").Get<LogImportRuntimeSettings>() ?? new LogImportRuntimeSettings();
    var before = repo.LoadLogImport(baseSettings);
    repo.SaveLogImport(settings);
    await auditLogService.WriteChangeAsync(
        "log_import_runtime_update",
        null,
        [
            new AuditChange("Enabled", before.Enabled.ToString(), settings.Enabled.ToString()),
            new AuditChange("LogFolder", before.LogFolder, settings.LogFolder),
            new AuditChange("RunHour", before.RunHour.ToString(CultureInfo.InvariantCulture), settings.RunHour.ToString(CultureInfo.InvariantCulture)),
            new AuditChange("RunMinute", before.RunMinute.ToString(CultureInfo.InvariantCulture), settings.RunMinute.ToString(CultureInfo.InvariantCulture)),
            new AuditChange("TargetDateOffsetDays", before.TargetDateOffsetDays.ToString(CultureInfo.InvariantCulture), settings.TargetDateOffsetDays.ToString(CultureInfo.InvariantCulture)),
            new AuditChange("StateFilePath", before.StateFilePath, settings.StateFilePath),
            new AuditChange("ServerName", before.ServerName, settings.ServerName)
        ],
        true,
        "Log import runtime settings updated",
        context.RequestAborted);
    return Results.Ok();
});

app.MapGet("/api/runtime/log-analysis-rules", (RuntimeSettingsRepository repo) =>
{
    return Results.Ok(repo.LoadLogAnalysis(new LogAnalysisRuntimeSettings()));
});

app.MapPost("/api/runtime/log-analysis-rules", async (HttpRequest request, HttpContext context, RuntimeSettingsRepository repo, AuditLogService auditLogService) =>
{
    var settings = await request.ReadFromJsonAsync<LogAnalysisRuntimeSettings>();
    if (settings is null)
    {
        return Results.BadRequest("Invalid settings");
    }

    var before = repo.LoadLogAnalysis(new LogAnalysisRuntimeSettings());
    repo.SaveLogAnalysis(settings);
    await auditLogService.WriteChangeAsync(
        "log_analysis_runtime_update",
        null,
        [
            new AuditChange("ExcludedIpPatterns", string.Join(", ", before.ExcludedIpPatterns), string.Join(", ", settings.ExcludedIpPatterns)),
            new AuditChange("PriorityUrlTokens", string.Join(", ", before.PriorityUrlTokens), string.Join(", ", settings.PriorityUrlTokens)),
            new AuditChange("AdminProbeTokens", string.Join(", ", before.AdminProbeTokens), string.Join(", ", settings.AdminProbeTokens)),
            new AuditChange("BackupProbeTokens", string.Join(", ", before.BackupProbeTokens), string.Join(", ", settings.BackupProbeTokens)),
            new AuditChange("ConfigProbeTokens", string.Join(", ", before.ConfigProbeTokens), string.Join(", ", settings.ConfigProbeTokens)),
            new AuditChange("ScriptProbeTokens", string.Join(", ", before.ScriptProbeTokens), string.Join(", ", settings.ScriptProbeTokens))
        ],
        true,
        "Log analysis rule settings updated",
        context.RequestAborted);
    return Results.Ok();
});

app.MapGet("/api/security/settings", (SecuritySettingsRepository repo, IConfiguration config) =>
{
    var baseSettings = config.GetSection("Monitoring:Security").Get<SecuritySettings>() ?? new SecuritySettings();
    return Results.Ok(repo.Load(baseSettings));
});

app.MapPost("/api/security/settings", async (HttpRequest request, HttpContext context, SecuritySettingsRepository repo, AuditLogService auditLogService) =>
{
    var settings = await request.ReadFromJsonAsync<SecuritySettings>();
    if (settings is null)
    {
        return Results.BadRequest("Invalid settings");
    }

    var baseSettings = context.RequestServices.GetRequiredService<IConfiguration>().GetSection("Monitoring:Security").Get<SecuritySettings>() ?? new SecuritySettings();
    var before = repo.Load(baseSettings);
    repo.Save(settings);
    await auditLogService.WriteChangeAsync(
        "security_settings_update",
        null,
        [
            new AuditChange("TwoFactorEnabled", before.TwoFactorEnabled.ToString(), settings.TwoFactorEnabled.ToString())
        ],
        true,
        "Security settings updated",
        context.RequestAborted);
    return Results.Ok();
});

app.MapGet("/api/settings/backup", (HttpContext context, SettingsBackupService backupService, AuditLogService auditLogService) =>
{
    var payload = backupService.CreateBackup();
    _ = auditLogService.WriteAsync("settings_backup", null, true, "settings backup generated", context.RequestAborted);
    return Results.Json(payload);
});

app.MapPost("/api/settings/restore", async (HttpRequest request, HttpContext context, SettingsBackupService backupService, AuditLogService auditLogService) =>
{
    var payload = await request.ReadFromJsonAsync<SettingsBackupPayload>();
    if (payload is null)
    {
        return Results.BadRequest("Invalid backup payload.");
    }

    backupService.Restore(payload);
    await auditLogService.WriteAsync("settings_restore", null, true, "settings restored from backup payload", context.RequestAborted);
    return Results.Ok();
});

app.MapGet("/api/settings/backups", (SettingsBackupArchiveService archiveService) =>
{
    return Results.Ok(archiveService.GetRecentArchives(20));
});

app.MapGet("/api/events/timeline", async (int? take, EventTimelineService timelineService, CancellationToken ct) =>
{
    var items = await timelineService.GetRecentEventsAsync(take ?? 100, ct);
    return Results.Ok(items);
});

app.MapGet("/api/dependencies/status", async (DependencyStatusService statusService, CancellationToken ct) =>
{
    var items = await statusService.GetStatusesAsync(ct);
    return Results.Ok(items);
});

app.MapGet("/api/reports/dashboard", async (
    MonitorStateService state,
    ServerCatalogRepository serverCatalogRepository,
    IDbContextFactory<MonitoringDbContext> dbFactory,
    CancellationToken ct) =>
{
    var hosts = state.GetSnapshot(TimeSpan.FromSeconds(15));
    var catalog = serverCatalogRepository.LoadAll();
    await using var db = await dbFactory.CreateDbContextAsync(ct);

    var report = new DashboardReportDto
    {
        GeneratedUtc = DateTime.UtcNow,
        HostCount = hosts.Count,
        OnlineCount = hosts.Count(x => x.IsOnline),
        CriticalCount = hosts.Count(x => x.Info.Dynamic.CpuInfo.Usage >= 85 || x.Info.Dynamic.MemoryInfo.Usage >= 90 || x.Info.Dynamic.DiskInfo.Percent >= 95),
        WarningCount = hosts.Count(x => x.Info.Dynamic.CpuInfo.Usage >= 60 || x.Info.Dynamic.MemoryInfo.Usage >= 75 || x.Info.Dynamic.DiskInfo.Percent >= 85),
        Hosts = hosts.Select(host =>
        {
            catalog.TryGetValue(host.Info.Hostname, out var metadata);
            return new DashboardReportHostDto
            {
                Hostname = host.Info.Hostname,
                Ip = host.Info.Ip,
                IsOnline = host.IsOnline,
                Group = metadata?.Group ?? string.Empty,
                Tags = metadata?.Tags ?? string.Empty,
                Cpu = host.Info.Dynamic.CpuInfo.Usage,
                Memory = host.Info.Dynamic.MemoryInfo.Usage,
                Disk = host.Info.Dynamic.DiskInfo.Percent
            };
        }).ToList(),
        RecentAlerts = await db.AlertEvents.OrderByDescending(x => x.TimestampUtc).Take(20).Select(x => new DashboardReportAlertDto
        {
            TimestampUtc = x.TimestampUtc,
            Hostname = x.Hostname,
            Metric = x.Metric,
            Value = x.Value,
            Threshold = x.Threshold,
            Message = x.Message,
            Type = x.Type.ToString()
        }).ToListAsync(ct),
        RecentLoginFailures = await db.MemberLoginAttempts.Where(x => !x.Success).OrderByDescending(x => x.CreatedUtc).Take(20).Select(x => new DashboardReportLoginDto
        {
            CreatedUtc = x.CreatedUtc,
            UserName = x.UserName,
            Reason = x.Reason,
            IpAddress = x.IpAddress
        }).ToListAsync(ct)
    };

    var json = System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    return Results.File(System.Text.Encoding.UTF8.GetBytes(json), "application/json", $"dashboard-report-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
});

app.MapGet("/api/sql/slow-queries", async (string hostname, SlowQueryService service, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(hostname))
    {
        return Results.BadRequest("hostname required");
    }

    var result = await service.GetSlowQueriesAsync(hostname, ct);
    if (!result.Success)
    {
        return Results.BadRequest(result.Error ?? "Failed to load slow queries.");
    }

    return Results.Ok(result.Rows);
});

app.MapGet("/api/sql/blocking-queries", async (string hostname, SlowQueryService service, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(hostname))
    {
        return Results.BadRequest("hostname required");
    }

    var result = await service.GetBlockingQueriesAsync(hostname, ct);
    if (!result.Success)
    {
        return Results.BadRequest(result.Error ?? "Failed to load blocking queries.");
    }

    return Results.Ok(result.Rows);
});

app.MapGet("/api/sql/top-io-queries", async (string hostname, SlowQueryService service, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(hostname))
    {
        return Results.BadRequest("hostname required");
    }

    var result = await service.GetTopIoQueriesAsync(hostname, ct);
    if (!result.Success)
    {
        return Results.BadRequest(result.Error ?? "Failed to load top IO queries.");
    }

    return Results.Ok(result.Rows);
});

app.MapGet("/api/logs/top-ip-access", async (string? date, int? take, IDbContextFactory<MonitoringDbContext> dbFactory) =>
{
    var selectedDate = DateOnly.FromDateTime(DateTime.Now);
    if (!string.IsNullOrWhiteSpace(date) && DateOnly.TryParse(date, out var parsedDate))
    {
        selectedDate = parsedDate;
    }

    var limit = take is null or <= 0 ? 3 : Math.Min(take.Value, 10);

    await using var db = await dbFactory.CreateDbContextAsync();
    var stats = await db.LogIpDailyStats
        .Where(x => x.LogDate == selectedDate)
        .ToListAsync();

    var rows = stats
        .GroupBy(x => x.ServerName)
        .OrderBy(g => g.Key)
        .Select(g => new TopIpGroupDto(
            g.Key,
            g.OrderByDescending(x => x.RequestCount)
                .ThenBy(x => x.Ip)
                .Take(limit)
                .Select(x => new TopIpRowDto(x.Ip, x.RequestCount, x.Status2xxCount, x.Status3xxCount, x.Status4xxCount, x.Status5xxCount))
                .ToList()))
        .ToList();

    return Results.Ok(new TopIpResponseDto(selectedDate, limit, rows));
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

static async Task SignInMemberAsync(HttpContext context, MemberEntity member, bool rememberMe, string sessionId)
{
    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, member.Id.ToString(CultureInfo.InvariantCulture)),
        new(ClaimTypes.Name, string.IsNullOrWhiteSpace(member.DisplayName) ? member.UserName : member.DisplayName),
        new(ClaimTypes.Role, member.Role),
        new("sid", sessionId)
    };

    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    var principal = new ClaimsPrincipal(identity);
    var properties = new AuthenticationProperties
    {
        IsPersistent = rememberMe,
        AllowRefresh = true,
        ExpiresUtc = rememberMe ? DateTimeOffset.UtcNow.AddHours(8) : null
    };

    await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, properties);
}

static string NormalizeReturnUrl(string? returnUrl)
{
    if (string.IsNullOrWhiteSpace(returnUrl))
    {
        return "/";
    }

    return returnUrl.StartsWith("/", StringComparison.Ordinal) ? returnUrl : "/";
}

static string returnUrlQuery(string returnUrl)
{
    return string.IsNullOrWhiteSpace(returnUrl) || returnUrl == "/"
        ? string.Empty
        : $"&returnUrl={Uri.EscapeDataString(returnUrl)}";
}

static string BuildEffectiveOllamaPrompt(string prompt, string? userQuestion)
{
    if (string.IsNullOrWhiteSpace(userQuestion))
    {
        return prompt;
    }

    return string.Concat(
        prompt,
        "\n\n[추가 질문]\n",
        userQuestion.Trim(),
        "\n\n[추가 질문 응답 지침]\n",
        "위 질문에 대해 JSON 근거를 사용해 반드시 별도 문단으로 직접 답하라. ",
        "질문과 무관한 기본 요약만 반복하지 마라. ",
        "질문에 포함된 IP, URL, 차단 여부를 우선 검토하고 마지막에 결론을 명확히 추가하라.");
}

static string BuildAbsoluteUrl(HttpContext context, string relativePath)
{
    var path = relativePath.StartsWith("/", StringComparison.Ordinal) ? relativePath : "/" + relativePath;
    return $"{context.Request.Scheme}://{context.Request.Host}{context.Request.PathBase}{path}";
}

static async Task DeleteMemberAsync(IDbContextFactory<MonitoringDbContext> dbFactory, long memberId, CancellationToken ct)
{
    await using var db = await dbFactory.CreateDbContextAsync(ct);
    var member = await db.Members.FirstOrDefaultAsync(x => x.Id == memberId, ct);
    if (member is null)
    {
        return;
    }

    var emailRequests = await db.EmailVerificationRequests.Where(x => x.MemberId == memberId).ToListAsync(ct);
    var passwordResetRequests = await db.PasswordResetRequests.Where(x => x.UserName == member.UserName).ToListAsync(ct);
    var sessions = await db.MemberSessions.Where(x => x.MemberId == memberId).ToListAsync(ct);
    var recoveryCodes = await db.MemberRecoveryCodes.Where(x => x.MemberId == memberId).ToListAsync(ct);

    db.EmailVerificationRequests.RemoveRange(emailRequests);
    db.PasswordResetRequests.RemoveRange(passwordResetRequests);
    db.MemberSessions.RemoveRange(sessions);
    db.MemberRecoveryCodes.RemoveRange(recoveryCodes);
    db.Members.Remove(member);
    await db.SaveChangesAsync(ct);
}

static async Task WriteMemberAuditAsync(
    IDbContextFactory<MonitoringDbContext> dbFactory,
    string actorUserName,
    string? targetUserName,
    string action,
    bool success,
    string? details,
    CancellationToken ct)
{
    try
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.MemberAuditLogs.Add(new MemberAuditLogEntity
        {
            ActorUserName = string.IsNullOrWhiteSpace(actorUserName) ? "system" : actorUserName.Trim().ToLowerInvariant(),
            TargetUserName = string.IsNullOrWhiteSpace(targetUserName) ? null : targetUserName.Trim().ToLowerInvariant(),
            Action = action,
            Details = details,
            Success = success,
            CreatedUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
    }
    catch
    {
        // Audit logs must not break authentication flows.
    }
}

static async Task<IResult> ProcessRegisterAsync(
    HttpRequest request,
    HttpContext context,
    MemberAuthService memberAuth,
    IDbContextFactory<MonitoringDbContext> dbFactory,
    EmailSenderService emailSender,
    CancellationToken ct,
    bool bootstrapOnly)
{
    if (!request.HasFormContentType)
    {
        return Results.BadRequest("Expected form post.");
    }

    var form = await request.ReadFormAsync(ct);
    var userName = form["userName"].ToString();
    var displayName = form["displayName"].ToString();
    var emailAddress = form["email"].ToString();
    var password = form["password"].ToString();
    var confirmPassword = form["confirmPassword"].ToString();
    var returnUrl = NormalizeReturnUrl(form["returnUrl"].ToString());
    var isBootstrap = bootstrapOnly || string.Equals(form["isBootstrap"].ToString(), "true", StringComparison.OrdinalIgnoreCase);
    var emailSettings = emailSender.GetSettings();
    var emailAddressRequired = emailSettings.Enabled && (emailSettings.VerificationEnabled || emailSettings.PasswordResetEnabled);

    if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(password))
    {
        await WriteMemberAuditAsync(dbFactory, "anonymous", MemberAuthService.NormalizeUserName(userName), bootstrapOnly ? "bootstrap_register" : "register", false, "required fields missing", ct);
        return Results.Redirect($"/register?error=required{returnUrlQuery(returnUrl)}");
    }

    if (emailAddressRequired && string.IsNullOrWhiteSpace(emailAddress))
    {
        return Results.Redirect($"/register?error=email_required{returnUrlQuery(returnUrl)}");
    }

    if (!string.Equals(password, confirmPassword, StringComparison.Ordinal))
    {
        return Results.Redirect($"/register?error=password_mismatch{returnUrlQuery(returnUrl)}");
    }

    if (isBootstrap)
    {
        if (await memberAuth.HasMembersAsync(ct))
        {
            await WriteMemberAuditAsync(dbFactory, "system", MemberAuthService.NormalizeUserName(userName), "bootstrap_register", false, "members already exist", ct);
            return Results.Redirect($"/register?error=configured{returnUrlQuery(returnUrl)}");
        }

        try
        {
            var member = await memberAuth.CreateBootstrapMemberAsync(userName, displayName, password, "Admin", emailAddress, ct);
            if (emailSettings.VerificationEnabled)
            {
                var token = await memberAuth.CreateEmailVerificationRequestAsync(member.Id, emailAddress, ct);
                var verifyUrl = BuildAbsoluteUrl(context, $"/verify-email?token={Uri.EscapeDataString(token)}");
                try
                {
                    await emailSender.SendVerificationEmailAsync(emailAddress, verifyUrl, displayName, ct);
                }
                catch (Exception sendEx)
                {
                    await DeleteMemberAsync(dbFactory, member.Id, ct);
                    await WriteMemberAuditAsync(dbFactory, "system", MemberAuthService.NormalizeUserName(userName), "email_verification_send", false, sendEx.Message, ct);
                    return Results.Redirect($"/register?error=email_send_failed{returnUrlQuery(returnUrl)}");
                }
            }

            await WriteMemberAuditAsync(dbFactory, "system", MemberAuthService.NormalizeUserName(userName), "bootstrap_register", true, "role=Admin", ct);
            return Results.Redirect($"/login?success={(emailSettings.VerificationEnabled ? "verification_sent" : "registered")}{returnUrlQuery(returnUrl)}");
        }
        catch (Exception ex)
        {
            if (ex.Message.Contains("exists", StringComparison.OrdinalIgnoreCase))
            {
                await WriteMemberAuditAsync(dbFactory, "system", MemberAuthService.NormalizeUserName(userName), "bootstrap_register", false, "duplicate user", ct);
                return Results.Redirect($"/register?error=duplicate{returnUrlQuery(returnUrl)}");
            }

            if (ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase))
            {
                await WriteMemberAuditAsync(dbFactory, "system", MemberAuthService.NormalizeUserName(userName), "bootstrap_register", false, "password policy", ct);
                return Results.Redirect($"/register?error=password_policy{returnUrlQuery(returnUrl)}");
            }

            await WriteMemberAuditAsync(dbFactory, "system", MemberAuthService.NormalizeUserName(userName), "bootstrap_register", false, "bootstrap failed", ct);
            return Results.Redirect($"/register?error=bootstrap{returnUrlQuery(returnUrl)}");
        }
    }

    try
    {
        var member = await memberAuth.RegisterMemberAsync(userName, displayName, password, emailAddress, ct);
        if (emailAddressRequired && emailSettings.VerificationEnabled)
        {
            var token = await memberAuth.CreateEmailVerificationRequestAsync(member.Id, emailAddress, ct);
            var verifyUrl = BuildAbsoluteUrl(context, $"/verify-email?token={Uri.EscapeDataString(token)}");
            try
            {
                await emailSender.SendVerificationEmailAsync(emailAddress, verifyUrl, displayName, ct);
            }
            catch (Exception sendEx)
            {
                await DeleteMemberAsync(dbFactory, member.Id, ct);
                await WriteMemberAuditAsync(dbFactory, "system", MemberAuthService.NormalizeUserName(userName), "email_verification_send", false, sendEx.Message, ct);
                return Results.Redirect($"/register?error=email_send_failed{returnUrlQuery(returnUrl)}");
            }
        }

        await WriteMemberAuditAsync(dbFactory, "system", MemberAuthService.NormalizeUserName(userName), "register", true, "role=User", ct);
        return Results.Redirect($"/login?success={(emailSettings.VerificationEnabled ? "verification_sent" : "registered")}{returnUrlQuery(returnUrl)}");
    }
    catch (Exception ex)
    {
        if (ex.Message.Contains("exists", StringComparison.OrdinalIgnoreCase))
        {
            await WriteMemberAuditAsync(dbFactory, "system", MemberAuthService.NormalizeUserName(userName), "register", false, "duplicate user", ct);
            return Results.Redirect($"/register?error=duplicate{returnUrlQuery(returnUrl)}");
        }

        if (ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase))
        {
            await WriteMemberAuditAsync(dbFactory, "system", MemberAuthService.NormalizeUserName(userName), "register", false, "password policy", ct);
            return Results.Redirect($"/register?error=password_policy{returnUrlQuery(returnUrl)}");
        }

        await WriteMemberAuditAsync(dbFactory, "system", MemberAuthService.NormalizeUserName(userName), "register", false, "register failed", ct);
        return Results.Redirect($"/register?error=register_failed{returnUrlQuery(returnUrl)}");
    }
}

static async Task<IResult> ProcessLogUploadAsync(HttpRequest request, IDbContextFactory<MonitoringDbContext> dbFactory)
{
    if (!request.HasFormContentType)
    {
        return Results.BadRequest("Expected multipart form.");
    }

    var form = await request.ReadFormAsync();
    var files = form.Files;
    var serverName = string.IsNullOrWhiteSpace(form["serverName"]) ? "unknown" : form["serverName"].ToString().Trim();
    var nowUtc = DateTime.UtcNow;
    var aggregateMap = new Dictionary<(DateOnly LogDate, string Ip), LogIpAggregate>();

    await using var db = await dbFactory.CreateDbContextAsync();
    foreach (var file in files)
    {
        await using var stream = file.OpenReadStream();
        using var reader = new StreamReader(stream);
        var text = await reader.ReadToEndAsync();
        var parsed = ApacheLogParser.ParseLines(text.Split(Environment.NewLine));

        var aggregates = parsed
            .Select(row =>
            {
                var logDate = TryParseLogDate(row.Date, nowUtc);
                return new
                {
                    LogDate = logDate,
                    row.Ip,
                    Status = row.Status
                };
            })
            .GroupBy(x => new { x.LogDate, x.Ip })
            .Select(g => new LogIpAggregate
            {
                LogDate = g.Key.LogDate,
                Ip = g.Key.Ip,
                RequestCount = g.LongCount(),
                Status2xxCount = g.LongCount(x => IsStatusInRange(x.Status, 200, 299)),
                Status3xxCount = g.LongCount(x => IsStatusInRange(x.Status, 300, 399)),
                Status4xxCount = g.LongCount(x => IsStatusInRange(x.Status, 400, 499)),
                Status5xxCount = g.LongCount(x => IsStatusInRange(x.Status, 500, 599))
            })
            .ToList();

        foreach (var aggregate in aggregates)
        {
            var key = (aggregate.LogDate, aggregate.Ip);
            if (aggregateMap.TryGetValue(key, out var existingAggregate))
            {
                existingAggregate.RequestCount += aggregate.RequestCount;
                existingAggregate.Status2xxCount += aggregate.Status2xxCount;
                existingAggregate.Status3xxCount += aggregate.Status3xxCount;
                existingAggregate.Status4xxCount += aggregate.Status4xxCount;
                existingAggregate.Status5xxCount += aggregate.Status5xxCount;
            }
            else
            {
                aggregateMap[key] = new LogIpAggregate
                {
                    LogDate = aggregate.LogDate,
                    Ip = aggregate.Ip,
                    RequestCount = aggregate.RequestCount,
                    Status2xxCount = aggregate.Status2xxCount,
                    Status3xxCount = aggregate.Status3xxCount,
                    Status4xxCount = aggregate.Status4xxCount,
                    Status5xxCount = aggregate.Status5xxCount
                };
            }
        }
    }

    foreach (var aggregate in aggregateMap.Values)
    {
        var existing = await db.LogIpDailyStats.FirstOrDefaultAsync(x =>
            x.ServerName == serverName &&
            x.LogDate == aggregate.LogDate &&
            x.Ip == aggregate.Ip);

        if (existing is null)
        {
            db.LogIpDailyStats.Add(new LogIpDailyStatEntity
            {
                ServerName = serverName,
                LogDate = aggregate.LogDate,
                Ip = aggregate.Ip,
                RequestCount = aggregate.RequestCount,
                Status2xxCount = aggregate.Status2xxCount,
                Status3xxCount = aggregate.Status3xxCount,
                Status4xxCount = aggregate.Status4xxCount,
                Status5xxCount = aggregate.Status5xxCount,
                FirstSeenUtc = nowUtc,
                LastSeenUtc = nowUtc
            });
        }
        else
        {
            existing.RequestCount += aggregate.RequestCount;
            existing.Status2xxCount += aggregate.Status2xxCount;
            existing.Status3xxCount += aggregate.Status3xxCount;
            existing.Status4xxCount += aggregate.Status4xxCount;
            existing.Status5xxCount += aggregate.Status5xxCount;
            existing.LastSeenUtc = nowUtc;
        }
    }

    await db.SaveChangesAsync();
    return Results.Ok(new { files = files.Count, aggregates = aggregateMap.Count });
}

static DateOnly TryParseLogDate(string value, DateTime fallbackUtc)
{
    return DateOnly.TryParse(value, out var parsed) ? parsed : DateOnly.FromDateTime(fallbackUtc);
}

static bool IsStatusInRange(string value, int minInclusive, int maxInclusive)
{
    if (!int.TryParse(value, out var status))
    {
        return false;
    }

    return status >= minInclusive && status <= maxInclusive;
}

internal sealed record TopIpResponseDto(DateOnly Date, int Take, List<TopIpGroupDto> Items);

internal sealed record TopIpGroupDto(string ServerName, List<TopIpRowDto> Items);

internal sealed record TopIpRowDto(string Ip, long RequestCount, long Status2xxCount, long Status3xxCount, long Status4xxCount, long Status5xxCount);

internal sealed class DashboardReportDto
{
    public DateTime GeneratedUtc { get; set; }
    public int HostCount { get; set; }
    public int OnlineCount { get; set; }
    public int WarningCount { get; set; }
    public int CriticalCount { get; set; }
    public List<DashboardReportHostDto> Hosts { get; set; } = [];
    public List<DashboardReportAlertDto> RecentAlerts { get; set; } = [];
    public List<DashboardReportLoginDto> RecentLoginFailures { get; set; } = [];
}

internal sealed class DashboardReportHostDto
{
    public string Hostname { get; set; } = string.Empty;
    public string Ip { get; set; } = string.Empty;
    public bool IsOnline { get; set; }
    public string Group { get; set; } = string.Empty;
    public string Tags { get; set; } = string.Empty;
    public double Cpu { get; set; }
    public double Memory { get; set; }
    public double Disk { get; set; }
}

internal sealed class DashboardReportAlertDto
{
    public DateTime TimestampUtc { get; set; }
    public string Hostname { get; set; } = string.Empty;
    public string Metric { get; set; } = string.Empty;
    public double Value { get; set; }
    public double Threshold { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
}

internal sealed class DashboardReportLoginDto
{
    public DateTime CreatedUtc { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
}

internal sealed class LogIpAggregate
{
    public required DateOnly LogDate { get; set; }
    public required string Ip { get; set; }
    public long RequestCount { get; set; }
    public long Status2xxCount { get; set; }
    public long Status3xxCount { get; set; }
    public long Status4xxCount { get; set; }
    public long Status5xxCount { get; set; }
}

internal sealed record SuppressionRequest(string Hostname, string Metric, int Minutes, string? Reason);

internal sealed record OllamaAnalyzeRequest(
    string Prompt,
    string? SystemPrompt,
    string? Source,
    DateOnly? LogDate,
    long? TotalRows,
    long? Status5xxCount,
    double? Status5xxRatio,
    string? UserQuestion);




