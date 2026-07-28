using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Monitoring.Blazor.Models;

namespace Monitoring.Blazor.Services;

public sealed class SystemInfoCollector(IHttpClientFactory httpClientFactory, IConfiguration configuration)
{
    private readonly object _cpuLock = new();
    private readonly object _netLock = new();
    private CpuSample? _lastCpuSample;
    private DateTime _lastNetSampleUtc = DateTime.UtcNow;
    private NetSnapshot _lastNet = ReadNetworkSnapshot();
    private TimeSpan _lastProcessCpu = TimeSpan.Zero;
    private DateTime _lastProcessCpuUtc = DateTime.UtcNow;

    public async Task<MonitoringInfo> GetServerInfoAsync(CancellationToken cancellationToken = default)
    {
        var hostName = Dns.GetHostName();
        var ip = Dns.GetHostAddresses(hostName).FirstOrDefault(x => x.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)?.ToString() ?? "unknown";
        var os = RuntimeInformation.OSDescription;

        int? status = null;
        var targetAddress = configuration["Monitoring:TargetAddress"] ?? "https://dev.grac.or.kr";
        try
        {
            var client = httpClientFactory.CreateClient(nameof(SystemInfoCollector));
            using var response = await client.GetAsync(targetAddress, cancellationToken);
            status = (int)response.StatusCode;
        }
        catch
        {
            status = null;
        }

        return new MonitoringInfo
        {
            Hostname = hostName,
            Ip = ip,
            Os = os,
            Status = status,
            TargetUrl = targetAddress,
            Dynamic = new DynamicInfo
            {
                CpuInfo = ReadCpuInfo(),
                MemoryInfo = ReadMemoryInfo(),
                DiskInfo = ReadDiskInfo(),
                NetworkInfo = ReadNetworkInfo()
            }
        };
    }

    private CpuInfo ReadCpuInfo()
    {
        lock (_cpuLock)
        {
            if (OperatingSystem.IsWindows() && TryReadWindowsCpuSample(out var windowsSample))
            {
                return BuildCpuInfoFromSample(windowsSample);
            }

            if (OperatingSystem.IsLinux() && TryReadLinuxCpuSample(out var linuxSample))
            {
                return BuildCpuInfoFromSample(linuxSample);
            }

            return ReadFallbackProcessCpuInfo();
        }
    }

    private CpuInfo BuildCpuInfoFromSample(CpuSample current)
    {
        var usage = 0d;
        if (_lastCpuSample is not null)
        {
            var previous = _lastCpuSample.Value;
            var totalDelta = current.TotalTicks - previous.TotalTicks;
            var idleDelta = current.IdleTicks - previous.IdleTicks;
            if (totalDelta > 0)
            {
                usage = (1d - (double)idleDelta / totalDelta) * 100d;
            }
        }

        _lastCpuSample = current;

        return new CpuInfo
        {
            Usage = Math.Round(Math.Clamp(usage, 0, 100), 2),
            Processor = Environment.ProcessorCount
        };
    }

    private CpuInfo ReadFallbackProcessCpuInfo()
    {
        var now = DateTime.UtcNow;
        var proc = System.Diagnostics.Process.GetCurrentProcess();
        var currentCpu = proc.TotalProcessorTime;
        var elapsedSec = Math.Max((now - _lastProcessCpuUtc).TotalSeconds, 1);
        var cpuDeltaMs = (currentCpu - _lastProcessCpu).TotalMilliseconds;
        var usage = cpuDeltaMs / (elapsedSec * 1000d * Environment.ProcessorCount) * 100d;

        _lastProcessCpu = currentCpu;
        _lastProcessCpuUtc = now;

        return new CpuInfo
        {
            Usage = Math.Round(Math.Clamp(usage, 0, 100), 2),
            Processor = Environment.ProcessorCount
        };
    }

    private static MemoryInfo ReadMemoryInfo()
    {
        if (OperatingSystem.IsWindows() && TryReadWindowsMemory(out var winMemory))
        {
            return winMemory;
        }

        if (OperatingSystem.IsLinux() && TryReadLinuxMemory(out var linuxMemory))
        {
            return linuxMemory;
        }

        var gcInfo = GC.GetGCMemoryInfo();
        var total = gcInfo.TotalAvailableMemoryBytes > 0 ? (ulong)gcInfo.TotalAvailableMemoryBytes : 1UL;
        var used = (ulong)GC.GetTotalMemory(false);
        var available = total > used ? total - used : 0UL;

        return new MemoryInfo
        {
            Total = total,
            Available = available,
            Usage = Math.Round((double)used / total * 100d, 2)
        };
    }

    private static DiskInfo ReadDiskInfo()
    {
        try
        {
            var systemRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? "/";
            var drive = DriveInfo.GetDrives().FirstOrDefault(x => x.IsReady && string.Equals(x.RootDirectory.FullName, systemRoot, StringComparison.OrdinalIgnoreCase))
                        ?? DriveInfo.GetDrives().FirstOrDefault(x => x.IsReady);

            if (drive is null)
            {
                return new DiskInfo();
            }

            var total = (ulong)drive.TotalSize;
            var free = (ulong)drive.AvailableFreeSpace;
            return new DiskInfo
            {
                Total = total,
                Free = free,
                Percent = total == 0 ? 0 : Math.Round((double)(total - free) / total * 100d, 2)
            };
        }
        catch
        {
            return new DiskInfo();
        }
    }

    private NetworkInfo ReadNetworkInfo()
    {
        lock (_netLock)
        {
            var current = ReadNetworkSnapshot();
            var now = DateTime.UtcNow;
            var elapsed = Math.Max((now - _lastNetSampleUtc).TotalSeconds, 1);

            var sentPerSec = (long)((double)(current.BytesSent - _lastNet.BytesSent) / elapsed);
            var recvPerSec = (long)((double)(current.BytesRecv - _lastNet.BytesRecv) / elapsed);

            _lastNet = current;
            _lastNetSampleUtc = now;

            return new NetworkInfo
            {
                BytesSent = current.BytesSent,
                BytesRecv = current.BytesRecv,
                SentPerSec = sentPerSec,
                RecvPerSec = recvPerSec,
                SentMbps = Math.Round((sentPerSec * 8d) / (1024d * 1024d), 3),
                RecvMbps = Math.Round((recvPerSec * 8d) / (1024d * 1024d), 3),
                PacketsSent = current.PacketsSent,
                PacketsRecv = current.PacketsRecv,
                ErrIn = current.ErrIn,
                ErrOut = current.ErrOut,
                DropIn = current.DropIn,
                DropOut = current.DropOut
            };
        }
    }

    private static NetSnapshot ReadNetworkSnapshot()
    {
        ulong bytesSent = 0;
        ulong bytesRecv = 0;
        ulong packetsSent = 0;
        ulong packetsRecv = 0;
        ulong errIn = 0;
        ulong errOut = 0;
        ulong dropIn = 0;
        ulong dropOut = 0;

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            try
            {
                var stats = nic.GetIPStatistics();
                bytesSent += (ulong)stats.BytesSent;
                bytesRecv += (ulong)stats.BytesReceived;
                packetsSent += (ulong)stats.UnicastPacketsSent;
                packetsRecv += (ulong)stats.UnicastPacketsReceived;
                errIn += (ulong)stats.IncomingPacketsWithErrors;
                errOut += (ulong)stats.OutgoingPacketsWithErrors;
                dropIn += (ulong)stats.IncomingPacketsDiscarded;
                // Some platforms do not expose OutgoingPacketsDiscarded consistently.
                dropOut += 0;
            }
            catch
            {
                // Ignore NIC failures.
            }
        }

        return new NetSnapshot(bytesSent, bytesRecv, packetsSent, packetsRecv, errIn, errOut, dropIn, dropOut);
    }

    private static bool TryReadLinuxCpuSample(out CpuSample sample)
    {
        sample = default;
        try
        {
            var firstLine = File.ReadLines("/proc/stat").FirstOrDefault();
            if (string.IsNullOrWhiteSpace(firstLine) || !firstLine.StartsWith("cpu "))
            {
                return false;
            }

            var parts = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 5)
            {
                return false;
            }

            ulong[] values = parts.Skip(1).Select(p => ulong.TryParse(p, out var v) ? v : 0UL).ToArray();
            var idle = values.Length > 3 ? values[3] : 0UL;
            var iowait = values.Length > 4 ? values[4] : 0UL;
            var total = values.Aggregate(0UL, (sum, value) => sum + value);

            sample = new CpuSample(idle + iowait, total);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryReadWindowsCpuSample(out CpuSample sample)
    {
        sample = default;
        if (!GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
        {
            return false;
        }

        var idle = FileTimeToUInt64(idleTime);
        var kernel = FileTimeToUInt64(kernelTime);
        var user = FileTimeToUInt64(userTime);
        var total = kernel + user;
        sample = new CpuSample(idle, total);
        return true;
    }

    private static bool TryReadLinuxMemory(out MemoryInfo memory)
    {
        memory = new MemoryInfo();
        try
        {
            var map = new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in File.ReadLines("/proc/meminfo"))
            {
                var split = line.Split(':', 2, StringSplitOptions.TrimEntries);
                if (split.Length != 2)
                {
                    continue;
                }

                var valuePart = split[1].Trim();
                var number = valuePart.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (ulong.TryParse(number, out var parsed))
                {
                    map[split[0]] = parsed * 1024UL;
                }
            }

            if (!map.TryGetValue("MemTotal", out var total) || total == 0)
            {
                return false;
            }

            if (!map.TryGetValue("MemAvailable", out var available))
            {
                available = map.TryGetValue("MemFree", out var free) ? free : 0UL;
            }

            memory = new MemoryInfo
            {
                Total = total,
                Available = available,
                Usage = Math.Round((1d - (double)available / total) * 100d, 2)
            };
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryReadWindowsMemory(out MemoryInfo memory)
    {
        memory = new MemoryInfo();
        var status = new MEMORYSTATUSEX();
        status.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        if (!GlobalMemoryStatusEx(ref status) || status.ullTotalPhys == 0)
        {
            return false;
        }

        memory = new MemoryInfo
        {
            Total = status.ullTotalPhys,
            Available = status.ullAvailPhys,
            Usage = Math.Round((1d - (double)status.ullAvailPhys / status.ullTotalPhys) * 100d, 2)
        };
        return true;
    }

    private static ulong FileTimeToUInt64(FILETIME fileTime)
    {
        return ((ulong)fileTime.dwHighDateTime << 32) | fileTime.dwLowDateTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public uint dwLowDateTime;
        public uint dwHighDateTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FILETIME lpIdleTime, out FILETIME lpKernelTime, out FILETIME lpUserTime);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    private readonly record struct CpuSample(ulong IdleTicks, ulong TotalTicks);

    private sealed record NetSnapshot(
        ulong BytesSent,
        ulong BytesRecv,
        ulong PacketsSent,
        ulong PacketsRecv,
        ulong ErrIn,
        ulong ErrOut,
        ulong DropIn,
        ulong DropOut);
}
