using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;

namespace StudentUI.Service
{
    // 시험 중에만 이 PC의 DNS를 127.0.0.1 로 돌려 놓는다.
    //
    // 네트워크 감시(NetworkControl.dll)는 127.0.0.1:53 에서 DNS 조회를 듣는 방식이라,
    // PC의 DNS가 그쪽을 보고 있지 않으면 조회가 한 건도 들어오지 않는다.
    // 감시를 켜는 것만으로는 아무것도 감지되지 않기 때문에 이 전환이 반드시 필요하다.
    //
    // 원래 설정은 바꾸기 '전에' 파일로 남긴다. 시험 도중 프로그램이 죽어도
    // 다음 실행 때 그 파일을 보고 되돌려, 학생 PC가 인터넷을 잃은 채 남지 않게 한다.
    public static class DnsRedirectService
    {
        private const string Loopback = "127.0.0.1";

        // 시험이 끝나기 전에 프로그램이 죽어도 남아 있어야 하므로 앱 폴더가 아닌 곳에 둔다.
        private static readonly string BackupPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ExamManager", "dns-backup.json");

        private class AdapterDns
        {
            public string Name { get; set; } = string.Empty;
            public bool WasAutomatic { get; set; }          // DHCP 가 주던 값이었는지
            public List<string> Servers { get; set; } = new();
        }

        public static bool IsRedirected => File.Exists(BackupPath);

        public static bool IsAdministrator()
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }

        // DNS를 127.0.0.1 로 바꾸고, 조회를 넘겨줄 원래 DNS 주소를 돌려준다.
        // 실패하면 null 을 돌려주며 이때 설정은 건드리지 않은 상태다.
        public static string? Apply()
        {
            if (!IsAdministrator())
            {
                Debug.WriteLine("DNS를 바꾸려면 관리자 권한이 필요합니다.");
                return null;
            }

            var adapters = CollectActiveAdapters();
            if (adapters.Count == 0)
            {
                Debug.WriteLine("DNS를 바꿀 네트워크 어댑터를 찾지 못했습니다.");
                return null;
            }

            // 조회를 넘겨줄 상위 DNS. 원래 쓰던 것을 그대로 써야
            // 교내망처럼 외부 DNS가 막힌 곳에서도 인터넷이 계속 된다.
            string upstream = adapters
                .SelectMany(a => a.Servers)
                .FirstOrDefault(s => s != Loopback) ?? "8.8.8.8";

            // 바꾸기 전에 남긴다. 이 순서가 뒤집히면 복구할 근거가 사라진다.
            SaveBackup(StripLoopback(adapters));

            foreach (var adapter in adapters)
                Run("netsh", $"interface ipv4 set dnsservers name=\"{adapter.Name}\" static {Loopback} primary");

            FlushCache();
            return upstream;
        }

        // 원래 DNS로 되돌린다. 되돌릴 것이 없으면 조용히 넘어간다.
        public static void Restore()
        {
            List<AdapterDns>? adapters = LoadBackup();
            if (adapters == null) return;

            foreach (var adapter in adapters)
            {
                if (adapter.WasAutomatic || adapter.Servers.Count == 0)
                {
                    Run("netsh", $"interface ipv4 set dnsservers name=\"{adapter.Name}\" source=dhcp");
                    continue;
                }

                Run("netsh", $"interface ipv4 set dnsservers name=\"{adapter.Name}\" static {adapter.Servers[0]} primary");

                // 두 번째부터는 add 로 붙인다. index 는 1 부터라 두 번째가 2 다.
                for (int i = 1; i < adapter.Servers.Count; i++)
                    Run("netsh", $"interface ipv4 add dnsservers name=\"{adapter.Name}\" {adapter.Servers[i]} index={i + 1}");
            }

            FlushCache();
            TryDelete(BackupPath);
        }

        // 지난 실행이 DNS를 되돌리지 못하고 끝났으면 지금 되돌린다.
        // 앱이 뜰 때 한 번 부른다.
        public static void RestoreIfLeftOver()
        {
            if (!IsRedirected) return;

            Debug.WriteLine("지난 시험에서 DNS가 복구되지 않아 지금 되돌립니다.");
            Restore();
        }

        // ── 내부 ─────────────────────────────────────────────────────

        // 실제로 인터넷으로 나가는 어댑터만 고른다.
        // 기본 게이트웨이가 없는 가상 어댑터까지 건드리면 되돌릴 것만 늘어난다.
        private static List<AdapterDns> CollectActiveAdapters()
        {
            var result = new List<AdapterDns>();

            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                IPInterfaceProperties properties;
                try { properties = nic.GetIPProperties(); }
                catch { continue; }

                bool hasGateway = properties.GatewayAddresses
                    .Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork &&
                              !g.Address.Equals(System.Net.IPAddress.Any));
                if (!hasGateway) continue;

                var servers = properties.DnsAddresses
                    .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
                    .Select(a => a.ToString())
                    .ToList();

                result.Add(new AdapterDns
                {
                    Name = nic.Name,
                    WasAutomatic = IsDnsFromDhcp(nic.Id),
                    Servers = servers,
                });
            }

            return result;
        }

        // DNS를 손으로 지정했는지 DHCP 가 준 것인지는 레지스트리의 NameServer 로 갈린다.
        // 손으로 지정한 경우에만 값이 들어 있고, DHCP 면 비어 있다.
        // 이걸 구분하지 않으면 자동이던 PC를 고정 IP로 되돌려 놓게 된다.
        private static bool IsDnsFromDhcp(string interfaceId)
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    $@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{interfaceId}");

                string? nameServer = key?.GetValue("NameServer") as string;
                return string.IsNullOrWhiteSpace(nameServer);
            }
            catch
            {
                // 못 읽으면 자동이었다고 본다. 고정으로 잘못 되돌리는 것보다 안전하다.
                return true;
            }
        }

        // 백업에서 127.0.0.1 을 걷어낸다.
        //
        // 지난 시험이 복구되지 못하고 끝나면 지금 DNS 가 이미 127.0.0.1 이다.
        // 그대로 백업하면 '원래 값'이 127.0.0.1 이 되어, 시험이 끝나고 되돌려도
        // 여전히 감시 프로그램을 가리킨다 — 그 PC 는 인터넷이 되지 않는 채로 남는다.
        //
        // 걷어낸 뒤 남는 주소가 없으면 DHCP 로 되돌리게 표시한다.
        // 공유기나 교내망이 주는 값을 다시 받아오는 것이 원래 상태에 가장 가깝다.
        private static List<AdapterDns> StripLoopback(List<AdapterDns> adapters)
        {
            var cleaned = new List<AdapterDns>();

            foreach (var adapter in adapters)
            {
                var servers = adapter.Servers.Where(s => s != Loopback).ToList();

                cleaned.Add(new AdapterDns
                {
                    Name         = adapter.Name,
                    WasAutomatic = adapter.WasAutomatic || servers.Count == 0,
                    Servers      = servers,
                });
            }

            return cleaned;
        }

        private static void SaveBackup(List<AdapterDns> adapters)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(BackupPath)!);
                File.WriteAllText(BackupPath,
                    JsonSerializer.Serialize(adapters, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"DNS 백업을 저장하지 못했습니다: {ex.Message}");
            }
        }

        private static List<AdapterDns>? LoadBackup()
        {
            try
            {
                if (!File.Exists(BackupPath)) return null;
                return JsonSerializer.Deserialize<List<AdapterDns>>(File.ReadAllText(BackupPath));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"DNS 백업을 읽지 못했습니다: {ex.Message}");
                return null;
            }
        }

        // 이미 캐시된 도메인은 조회가 나가지 않아 감시에 걸리지 않는다.
        // 전환하고 되돌릴 때 모두 비워야 한다.
        private static void FlushCache() => Run("ipconfig", "/flushdns");

        private static void Run(string file, string arguments)
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo(file, arguments)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                });

                process?.WaitForExit(5000);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{file} {arguments} 실행 실패: {ex.Message}");
            }
        }

        private static void TryDelete(string path)
        {
            try { File.Delete(path); } catch { }
        }
    }
}
