using System.Text.Json.Serialization;

namespace Monitoring.Blazor.Models;

public sealed class MonitoringInfo
{
    [JsonPropertyName("hostname")]
    public string Hostname { get; set; } = "unknown";

    [JsonPropertyName("ip")]
    public string Ip { get; set; } = "unknown";

    [JsonPropertyName("os")]
    public string Os { get; set; } = "unknown";

    [JsonPropertyName("status")]
    public int? Status { get; set; }

    [JsonPropertyName("target_url")]
    public string? TargetUrl { get; set; }

    [JsonPropertyName("dynamic")]
    public DynamicInfo Dynamic { get; set; } = new();
}

public sealed class DynamicInfo
{
    [JsonPropertyName("memory_info")]
    public MemoryInfo MemoryInfo { get; set; } = new();

    [JsonPropertyName("cpu_info")]
    public CpuInfo CpuInfo { get; set; } = new();

    [JsonPropertyName("disk_info")]
    public DiskInfo DiskInfo { get; set; } = new();

    [JsonPropertyName("network_info")]
    public NetworkInfo NetworkInfo { get; set; } = new();
}

public sealed class MemoryInfo
{
    [JsonPropertyName("total")]
    public ulong Total { get; set; }

    [JsonPropertyName("available")]
    public ulong Available { get; set; }

    [JsonPropertyName("usage")]
    public double Usage { get; set; }
}

public sealed class CpuInfo
{
    [JsonPropertyName("usage")]
    public double Usage { get; set; }

    [JsonPropertyName("processor")]
    public int Processor { get; set; }
}

public sealed class DiskInfo
{
    [JsonPropertyName("free")]
    public ulong Free { get; set; }

    [JsonPropertyName("total")]
    public ulong Total { get; set; }

    [JsonPropertyName("percent")]
    public double Percent { get; set; }
}

public sealed class NetworkInfo
{
    [JsonPropertyName("bytes_sent")]
    public ulong BytesSent { get; set; }

    [JsonPropertyName("bytes_recv")]
    public ulong BytesRecv { get; set; }

    [JsonPropertyName("sent_per_sec")]
    public long SentPerSec { get; set; }

    [JsonPropertyName("recv_per_sec")]
    public long RecvPerSec { get; set; }

    [JsonPropertyName("sent_mbps")]
    public double SentMbps { get; set; }

    [JsonPropertyName("recv_mbps")]
    public double RecvMbps { get; set; }

    [JsonPropertyName("packets_sent")]
    public ulong PacketsSent { get; set; }

    [JsonPropertyName("packets_recv")]
    public ulong PacketsRecv { get; set; }

    [JsonPropertyName("errin")]
    public ulong ErrIn { get; set; }

    [JsonPropertyName("errout")]
    public ulong ErrOut { get; set; }

    [JsonPropertyName("dropin")]
    public ulong DropIn { get; set; }

    [JsonPropertyName("dropout")]
    public ulong DropOut { get; set; }
}

public sealed class HostState
{
    public MonitoringInfo Info { get; set; } = new();
    public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;
    public bool IsOnline { get; set; } = true;
}

public sealed class ParsedLogRow
{
    public string Date { get; set; } = string.Empty;
    public string Time { get; set; } = string.Empty;
    public string Ip { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public string Uri { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Referrer { get; set; } = string.Empty;
    public string UserAgent { get; set; } = string.Empty;
}
