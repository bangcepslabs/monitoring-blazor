import os
import platform
import socket
import time
import psutil
import requests

server_url = os.getenv("MONITOR_SERVER_URL", "http://localhost:8050/api/monitor/client-message")
host_address = os.getenv("MONITOR_TARGET_URL")
agent_api_key = os.getenv("MONITOR_AGENT_API_KEY", "")
send_interval_seconds = max(1, int(os.getenv("MONITOR_SEND_INTERVAL_SECONDS", "2")))
max_retry_delay_seconds = max(send_interval_seconds, int(os.getenv("MONITOR_MAX_RETRY_DELAY_SECONDS", "60")))

last_net_io = psutil.net_io_counters()
last_time = time.time()


def get_client_system_info():
    global last_net_io, last_time

    hostname = socket.gethostname()
    os_name = platform.system()
    ip = get_primary_ip()

    status_code = None
    try:
        status_code = requests.get(host_address, verify=False, timeout=3).status_code
    except Exception:
        pass

    cpu_percent = psutil.cpu_percent(interval=1)

    current_net_io = psutil.net_io_counters()
    current_time = time.time()

    time_delta = current_time - last_time
    if time_delta == 0:
        time_delta = 1

    sent_bytes = current_net_io.bytes_sent - last_net_io.bytes_sent
    recv_bytes = current_net_io.bytes_recv - last_net_io.bytes_recv

    sent_mbps = round((sent_bytes * 8) / time_delta / 1000000, 3)
    recv_mbps = round((recv_bytes * 8) / time_delta / 1000000, 3)

    last_net_io = current_net_io
    last_time = current_time

    cpu_count = psutil.cpu_count()
    mem = psutil.virtual_memory()
    disk = psutil.disk_usage(get_disk_root())

    return {
        "hostname": hostname,
        "ip": ip,
        "os": os_name,
        "status": status_code,
        "target_url": host_address,
        "dynamic": {
            "memory_info": {
                "total": mem.total,
                "available": mem.available,
                "usage": mem.percent
            },
            "cpu_info": {
                "usage": cpu_percent,
                "processor": cpu_count
            },
            "disk_info": {
                "free": disk.free,
                "total": disk.total,
                "percent": disk.percent
            },
            "network_info": {
                "bytes_sent": current_net_io.bytes_sent,
                "bytes_recv": current_net_io.bytes_recv,
                "sent_mbps": sent_mbps,
                "recv_mbps": recv_mbps,
                "packets_sent": current_net_io.packets_sent,
                "packets_recv": current_net_io.packets_recv,
                "errin": current_net_io.errin,
                "errout": current_net_io.errout,
                "dropin": current_net_io.dropin,
                "dropout": current_net_io.dropout
            }
        }
    }


def send_loop():
    retry_delay = send_interval_seconds
    session = requests.Session()

    if not agent_api_key:
        raise RuntimeError("MONITOR_AGENT_API_KEY is not configured.")
    if not host_address:
        raise RuntimeError("MONITOR_TARGET_URL is not configured.")

    while True:
        try:
            data = get_client_system_info()
            response = session.post(
                server_url,
                json=data,
                headers={
                    "Content-Type": "application/json",
                    "X-OpsEye-Agent-Key": agent_api_key,
                },
                timeout=3,
            )
            response.raise_for_status()

            print(
                f"[{data['hostname']}] CPU: {data['dynamic']['cpu_info']['usage']}%, "
                f"TX: {data['dynamic']['network_info']['sent_mbps']} Mbps, "
                f"RX: {data['dynamic']['network_info']['recv_mbps']} Mbps"
            )
            retry_delay = send_interval_seconds
        except Exception as err:
            print(f"Error sending data (retrying in {retry_delay}s): {err}")

            time.sleep(retry_delay)
            retry_delay = min(retry_delay * 2, max_retry_delay_seconds)
            continue

        time.sleep(send_interval_seconds)


def get_primary_ip():
    try:
        sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        sock.connect(("8.8.8.8", 80))
        ip = sock.getsockname()[0]
        sock.close()
        return ip
    except Exception:
        try:
            return socket.gethostbyname(socket.gethostname())
        except Exception:
            return "unknown"


def get_disk_root():
    if os.name == "nt":
        return os.getenv("SYSTEMDRIVE", "C:") + "\\"
    return "/"


if __name__ == "__main__":
    send_loop()
