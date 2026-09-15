using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace StudentUI.Service
{
    // 시험 중에만 이 PC 의 바깥 통신을 막고, 교수 PC 와의 연결만 남긴다.
    //
    // 기본 아웃바운드 정책을 '차단'으로 바꾸는 것만으로는 부족하다.
    // Claude · Copilot 같은 앱은 '모든 주소 허용' 규칙을 이미 갖고 있고, 허용 규칙은 기본 정책보다 우선한다.
    // 반대로 차단 규칙은 허용 규칙보다 우선하므로, '교수 PC 를 뺀 모든 주소 차단' 규칙 하나로 그 앱들까지 막는다.
    //
    // 기본 정책은 건드리지 않는다. 되돌릴 때는 이 규칙을 지우고, 우리가 켠 방화벽만 다시 끄면 된다.
    // 시험 도중 프로그램이 죽어도 다음 실행 때 같은 일을 해서, 학생 PC 가 인터넷을 잃은 채 남지 않게 한다.
    public static class FirewallPolicyService
    {
        // 이 이름의 규칙만 만들고 지운다. 학교가 넣어 둔 다른 규칙은 건드리지 않는다.
        private const string BlockRuleName = "ExamManager-Block-Outbound";

        // Windows 방화벽 COM 상수 (icftypes.h)
        private const int RuleDirectionOut      = 2;            // NET_FW_RULE_DIR_OUT
        private const int RuleActionBlock       = 0;            // NET_FW_ACTION_BLOCK
        private const int AnyProtocol           = 256;          // NET_FW_IP_PROTOCOL_ANY
        private const int AllProfiles           = 0x7FFFFFFF;   // NET_FW_PROFILE2_ALL
        private const int ModifyStateGpOverride = 1;            // NET_FW_MODIFY_STATE_GP_OVERRIDE
        private static readonly int[] ProfileTypes = { 1, 2, 4 };   // 도메인 · 개인 · 공용

        // 시험 때문에 우리가 켠 방화벽 프로필. 끝나면 이것만 다시 꺼야 원래 상태가 된다.
        // 프로그램이 죽어도 남아 있어야 하므로 앱 폴더가 아닌 곳에 둔다.
        private static readonly string EnabledProfilesPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ExamManager", "firewall-enabled-profiles.txt");

        // 차단을 건다. 성공하면 null, 실패하면 교수 화면에 보일 이유를 돌려준다.
        public static string? Apply(string professorIp)
        {
            if (!IPAddress.TryParse(professorIp, out IPAddress? professor) ||
                professor.AddressFamily != AddressFamily.InterNetwork)
                return $"교수 PC 주소를 알 수 없음 ({professorIp})";

            try
            {
                dynamic policy = CreateCom("HNetCfg.FwPolicy2");

                // 그룹 정책이 방화벽을 관리하는 PC 는 여기서 넣은 규칙이 적용되지 않는다.
                if ((int)policy.LocalPolicyModifyState == ModifyStateGpOverride)
                    return "그룹 정책이 방화벽을 관리하고 있어 차단을 적용할 수 없음";

                // 방화벽이 꺼진 프로필은 규칙이 있어도 아무것도 막지 않는다.
                EnableFirewall(policy);
                foreach (int profile in ProfileTypes)
                {
                    if (!(bool)policy.FirewallEnabled[profile])
                    {
                        Restore();
                        return "방화벽을 켜지 못함 (다른 보안 프로그램이 관리 중일 수 있음)";
                    }
                }

                // 지난 시험의 규칙이 남아 있으면 겹치지 않게 먼저 지운다.
                RemoveBlockRule(policy);

                dynamic rule = CreateCom("HNetCfg.FWRule");
                rule.Name            = BlockRuleName;
                rule.Description     = "시험 중 교수 PC 외 통신 차단. ExamManager 가 답안 제출 뒤 지운다.";
                rule.Direction       = RuleDirectionOut;
                rule.Action          = RuleActionBlock;
                rule.Protocol        = AnyProtocol;
                rule.RemoteAddresses = BuildBlockedAddresses(professor);
                // 활성 프로필에만 걸면, 시험 중 핫스팟을 붙여 다른 프로필로 잡힌 연결이 빠져나간다.
                rule.Profiles        = AllProfiles;
                rule.Enabled         = true;
                policy.Rules.Add(rule);

                return null;
            }
            catch (Exception ex)
            {
                // 켜 둔 방화벽까지 되돌린다. 차단은 못 걸었는데 방화벽만 켜진 채로 남을 이유가 없다.
                Debug.WriteLine($"방화벽 차단을 걸지 못했습니다: {ex.Message}");
                Restore();
                return $"방화벽 설정 실패: {ex.Message}";
            }
        }

        // 차단 규칙을 지우고, 우리가 켠 방화벽을 다시 끈다. 되돌렸으면 true. 여러 번 불려도 안전하다.
        // 앱이 뜰 때도 한 번 불러, 지난 시험이 되돌리지 못하고 끝난 흔적을 치운다.
        public static bool Restore()
        {
            try
            {
                dynamic policy = CreateCom("HNetCfg.FwPolicy2");
                RemoveBlockRule(policy);

                int enabledByUs = LoadEnabledProfiles();
                foreach (int profile in ProfileTypes)
                {
                    if ((enabledByUs & profile) != 0)
                        policy.FirewallEnabled[profile] = false;
                }

                // 다 되돌린 뒤에만 지운다. 실패했으면 남겨 두어 다음 실행 때 다시 시도한다.
                TryDelete(EnabledProfilesPath);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"방화벽을 되돌리지 못했습니다: {ex.Message}");
                return false;
            }
        }

        // ── 내부 ─────────────────────────────────────────────────────

        private static void EnableFirewall(dynamic policy)
        {
            int enabledByUs = LoadEnabledProfiles();

            foreach (int profile in ProfileTypes)
            {
                if ((bool)policy.FirewallEnabled[profile]) continue;

                // 켜기 전에 남긴다. 켠 뒤 기록하기 전에 죽으면, 시험이 끝나도 끌 근거가 사라진다.
                enabledByUs |= profile;
                Directory.CreateDirectory(Path.GetDirectoryName(EnabledProfilesPath)!);
                File.WriteAllText(EnabledProfilesPath, enabledByUs.ToString());

                policy.FirewallEnabled[profile] = true;
            }
        }

        private static int LoadEnabledProfiles()
        {
            try { return int.Parse(File.ReadAllText(EnabledProfilesPath)); }
            catch { return 0; }     // 기록이 없으면 우리가 켠 것도 없다
        }

        // 같은 이름의 규칙이 없으면 Item 이 '파일을 찾을 수 없음'(0x80070002) 예외를 던진다.
        private static void RemoveBlockRule(dynamic policy)
        {
            try { policy.Rules.Item(BlockRuleName); }
            catch (FileNotFoundException) { return; }

            policy.Rules.Remove(BlockRuleName);
        }

        // 막을 주소 목록. 교수 PC · DHCP 서버 · 브로드캐스트만 빼고 전부다.
        // 방화벽 규칙에는 '이 주소만 빼고' 조건이 없어서, 뺄 주소 사이사이를 범위로 적는다.
        //
        // DHCP 를 빼는 이유: 시험 중 IP 임대 갱신이 막히면 IP 를 잃고 교수 PC 연결까지 끊긴다.
        private static string BuildBlockedAddresses(IPAddress professor)
        {
            List<uint> excluded = DhcpServers()
                .Append(professor)
                .Append(IPAddress.Broadcast)
                .Select(ToUInt32)
                .Distinct()
                .OrderBy(address => address)
                .ToList();

            var ranges = new List<string>();
            ulong next = 0;     // 아직 목록에 넣지 않은 첫 주소
            foreach (uint address in excluded)
            {
                if (address > next) ranges.Add($"{ToIp((uint)next)}-{ToIp(address - 1)}");
                next = (ulong)address + 1;
            }
            // 브로드캐스트(255.255.255.255)가 늘 마지막이라 그 뒤로 남는 범위는 없다.

            // 교수 PC 와는 IPv4 로만 이어지므로 IPv6 는 통째로 막는다.
            ranges.Add("::-ffff:ffff:ffff:ffff:ffff:ffff:ffff:ffff");
            return string.Join(",", ranges);
        }

        private static IEnumerable<IPAddress> DhcpServers() =>
            NetworkInterface.GetAllNetworkInterfaces()
                .Where(nic => nic.OperationalStatus == OperationalStatus.Up)
                .SelectMany(nic => nic.GetIPProperties().DhcpServerAddresses)
                .Where(address => address.AddressFamily == AddressFamily.InterNetwork);

        private static uint ToUInt32(IPAddress address) =>
            BinaryPrimitives.ReadUInt32BigEndian(address.GetAddressBytes());

        private static string ToIp(uint value)
        {
            byte[] bytes = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
            return new IPAddress(bytes).ToString();
        }

        private static dynamic CreateCom(string progId) =>
            Activator.CreateInstance(Type.GetTypeFromProgID(progId, throwOnError: true)!)!;

        private static void TryDelete(string path)
        {
            try { File.Delete(path); } catch { }
        }
    }
}
