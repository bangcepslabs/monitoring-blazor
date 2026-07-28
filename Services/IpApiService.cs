using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Monitoring.Blazor.Models;

namespace Monitoring.Blazor.Services;

public sealed class IpApiService
{
    private const int MaxRequestsPerMinute = 40;
    private static readonly TimeSpan RateWindow = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan SuccessCacheDuration = TimeSpan.FromHours(24);
    private static readonly TimeSpan FailureCacheDuration = TimeSpan.FromMinutes(5);
    private static readonly SemaphoreSlim RateGate = new(1, 1);
    private static readonly Queue<DateTimeOffset> RequestWindow = new();
    private static readonly ConcurrentDictionary<string, Lazy<Task<IpApiResult>>> InFlight = new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly ILogger<IpApiService> _logger;

    public IpApiService(HttpClient httpClient, IMemoryCache cache, ILogger<IpApiService> logger)
    {
        _httpClient = httpClient;
        _cache = cache;
        _logger = logger;
    }

    public Task<IpApiResult> GetIpInfoAsync(string ip)
        => GetIpInfoAsync(ip, CancellationToken.None);

    public async Task<IpApiResult> GetIpInfoAsync(string ip, CancellationToken cancellationToken)
    {
        var normalized = NormalizeIp(ip);
        if (normalized is null)
        {
            return new IpApiResult
            {
                Query = ip?.Trim() ?? string.Empty,
                Status = "error"
            };
        }

        var cacheKey = BuildCacheKey(normalized);
        if (_cache.TryGetValue(cacheKey, out IpApiResult? cached) && cached is not null)
        {
            return cached;
        }

        var lazy = InFlight.GetOrAdd(
            normalized,
            key => new Lazy<Task<IpApiResult>>(() => FetchAndCacheAsync(key), LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            return await lazy.Value.WaitAsync(cancellationToken);
        }
        catch
        {
            if (lazy.IsValueCreated && lazy.Value.IsFaulted)
            {
                InFlight.TryRemove(normalized, out _);
            }

            throw;
        }
    }

    private async Task<IpApiResult> FetchAndCacheAsync(string ip)
    {
        try
        {
            var cacheKey = BuildCacheKey(ip);
            if (_cache.TryGetValue(cacheKey, out IpApiResult? cached) && cached is not null)
            {
                return cached;
            }

            await WaitForRateSlotAsync();

            using var response = await _httpClient.GetAsync($"http://ip-api.com/json/{Uri.EscapeDataString(ip)}");
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var throttled = CreateResult(ip, "throttled");
                _cache.Set(cacheKey, throttled, FailureCacheDuration);
                return throttled;
            }

            if (!response.IsSuccessStatusCode)
            {
                var failed = CreateResult(ip, "error");
                _cache.Set(cacheKey, failed, FailureCacheDuration);
                _logger.LogWarning("ip-api request failed for {Ip} with status {StatusCode}", ip, response.StatusCode);
                return failed;
            }

            var content = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<IpApiResult>(content, JsonOptions) ?? CreateResult(ip, "error");
            if (string.IsNullOrWhiteSpace(result.Query))
            {
                result.Query = ip;
            }

            if (string.IsNullOrWhiteSpace(result.Status))
            {
                result.Status = "error";
            }

            var ttl = string.Equals(result.Status, "success", StringComparison.OrdinalIgnoreCase)
                ? SuccessCacheDuration
                : FailureCacheDuration;

            _cache.Set(cacheKey, result, ttl);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error fetching IP info for {Ip}", ip);
            var error = CreateResult(ip, "error");
            _cache.Set(BuildCacheKey(ip), error, FailureCacheDuration);
            return error;
        }
        finally
        {
            InFlight.TryRemove(ip, out _);
        }
    }

    private static async Task WaitForRateSlotAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            TimeSpan delay = TimeSpan.Zero;
            await RateGate.WaitAsync(cancellationToken);
            try
            {
                var now = DateTimeOffset.UtcNow;
                while (RequestWindow.Count > 0 && now - RequestWindow.Peek() >= RateWindow)
                {
                    RequestWindow.Dequeue();
                }

                if (RequestWindow.Count < MaxRequestsPerMinute)
                {
                    RequestWindow.Enqueue(now);
                    return;
                }

                delay = RequestWindow.Peek() + RateWindow - now;
                if (delay < TimeSpan.Zero)
                {
                    delay = TimeSpan.Zero;
                }
            }
            finally
            {
                RateGate.Release();
            }

            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    private static string? NormalizeIp(string? ip)
    {
        var value = ip?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value;
    }

    private static string BuildCacheKey(string ip)
        => $"ip-api:{ip.ToLowerInvariant()}";

    private static IpApiResult CreateResult(string ip, string status)
        => new()
        {
            Query = ip,
            Status = status
        };
}
