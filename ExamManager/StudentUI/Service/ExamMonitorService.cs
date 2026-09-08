using NetworkLib;
using System;
using System.Collections.Generic;
using System.Text;

namespace StudentUI.Service
{
    // 교수 PC의 명령에 맞춰 프로세스 감시를 켜고, 적발 내용을 교수 PC로 되돌려 보낸다.
    //
    // 교수 PC에서 오는 패킷 세 가지를 순서대로 처리한다.
    //   ① ProcessListUpdate — 이번 시험의 감시 목록. 받아서 네이티브에 넣는다.
    //   ② ExtractArchive    — 시험 시작. 압축 해제가 끝난 뒤에 감시를 켠다.
    //   ③ ExamPhaseChange   — 시험 종료. 감시를 멈춘다.
    //
    // 프로세스 감시와 네트워크 감시를 같은 자리에서 켜고 끈다.
    // 교수가 따로 누를 것이 없도록 시험 시작·종료 신호에 그대로 묶었다.
    //
    // ②에서 '해제가 끝난 뒤'가 중요하다. 감시를 먼저 켜면 압축 해제에 쓰는
    // 7za.exe가 시험 중 새로 실행된 프로그램으로 적발된다.
    // ③도 같은 이유로 필요하다. 답안 수집도 7za.exe를 쓰므로, 감시를 멈추지 않고
    // 답안을 걷으면 학생 전원이 부정행위로 보고된다.
    public class ExamMonitorService : IDisposable
    {
        public static ExamMonitorService Instance { get; } = new ExamMonitorService();

        // 적발 내용을 학생 화면에도 알린다.
        // 지금은 금지 프로그램이 조용히 종료되기만 해서, 학생이 이유를 모르고 계속 다시 켠다.
        // 네이티브 감시 스레드에서 발생하므로 받는 쪽에서 화면 스레드로 넘겨야 한다.
        public event Action<string>? CheatWarning;
        // 네이티브 감시 DLL 래퍼. 시험이 시작될 때 처음 만들어진다(EnsureProcessControl 참고).
        private ProcessControlService? _processControl;
        private NetworkControlService? _networkControl;

        // 교수 UI 에서 도메인 목록을 보낼 방법이 아직 없어 여기에 둔다.
        // 프로토콜에 도메인 목록 패킷이 생기면 이 상수는 지우고 받은 값을 쓰면 된다.
        private static readonly string[] DefaultBlockedDomains =
        {
            "chatgpt.com", "openai.com", "claude.ai", "anthropic.com",
            "gemini.google.com", "bard.google.com", "copilot.microsoft.com",
            "perplexity.ai", "wrtn.ai", "poe.com",
        };

        private ExamMonitorService() { }

        // 앱 시작 시 한 번 호출 — 구독만 해두고 감시는 아직 켜지 않는다.
        public void Start()
        {
            NetworkService.Instance.PacketReceived += OnPacketReceived;
            ExamFileStore.Instance.ExamStartHandled += OnExamStartHandled;
        }

        private void OnPacketReceived(PacketType type, IntPtr payload, uint payloadLen)
        {
            if (type == PacketType.ProcessListUpdate) ApplyProcessList(payload, payloadLen);
            else if (type == PacketType.ExamPhaseChange) ApplyPhaseChange(payload, payloadLen);
        }

        // ── ① 감시 목록 수신 ──
        private void ApplyProcessList(IntPtr payload, uint payloadLen)
        {
            // 형식이 깨진 패킷은 통째로 버린다.
            // 반쯤 읽힌 목록을 적용하면 금지 프로그램이 빠진 채로 감시가 돌 수 있다.
            if (!ProcessListPayload.TryDecode(payload, payloadLen,
                                              out List<string> whitelist,
                                              out List<string> blacklist))
                return;

            ProcessControlService? processControl = EnsureProcessControl();
            if (processControl == null) return;

            // 네이티브 DLL은 파이프로 이어 붙인 한 줄을 받는다 (네트워크 형식과 다르다).
            processControl.SetWhitelist(string.Join("|", whitelist));
            processControl.SetBlacklist(string.Join("|", blacklist));
        }

        // ── ② 압축 해제까지 끝났으니 감시 시작 ──
        private void OnExamStartHandled()
        {
            bool processOn = EnsureProcessControl()?.StartMonitoring() ?? false;
            bool networkOn = StartNetworkMonitoring();

            ReportMonitorStatus(processOn, networkOn);
        }

        // 감시가 실제로 켜졌는지 교수에게 알린다.
        // 이 보고가 없으면 교수는 감시가 도는 줄 알고 시험을 진행하게 된다.
        private void ReportMonitorStatus(bool processOn, bool networkOn)
        {
            MonitorFlags flags = MonitorFlags.None;
            if (processOn) flags |= MonitorFlags.ProcessMonitor;
            if (networkOn) flags |= MonitorFlags.NetworkMonitor;

            NetworkService.Instance.SendPacket(PacketType.MonitorStatusReport,
                MonitorStatusPayload.Encode(flags, _monitorFailReason));
        }

        // 감시를 켜지 못한 이유. 교수 화면에 그대로 보인다.
        private string _monitorFailReason = string.Empty;

        // DNS 를 127.0.0.1 로 돌려 놓아야 조회가 감시로 들어온다.
        // 전환에 실패하면 감시를 켜 봐야 아무것도 잡히지 않으므로 시작하지 않는다.
        private bool StartNetworkMonitoring()
        {
            NetworkControlService? networkControl = EnsureNetworkControl();
            if (networkControl == null)
            {
                _monitorFailReason = "감시 모듈을 불러오지 못함 (NetworkControl.dll)";
                return false;
            }

            string? upstream = DnsRedirectService.Apply();
            if (upstream == null)
            {
                _monitorFailReason = DnsRedirectService.IsAdministrator()
                    ? "DNS 전환 실패 (네트워크 어댑터를 찾지 못함)"
                    : "관리자 권한 없음 — DNS 를 바꿀 수 없음";
                System.Diagnostics.Debug.WriteLine("DNS 전환에 실패해 네트워크 감시를 켜지 않습니다.");
                return false;
            }

            // 조회를 원래 DNS 로 넘겨 준다. 감시는 하되 인터넷은 그대로 되게 하기 위함이다.
            networkControl.SetUpstream(upstream);
            networkControl.SetTargetDomains(string.Join("|", DefaultBlockedDomains));

            if (!networkControl.StartMonitoring())
            {
                // 켜지 못했으면 DNS 를 그대로 둘 수 없다. 되돌리지 않으면 인터넷이 끊긴다.
                System.Diagnostics.Debug.WriteLine("네트워크 감시를 켜지 못해 DNS 를 되돌립니다.");
                DnsRedirectService.Restore();
                _monitorFailReason = "감시를 시작하지 못함 (127.0.0.1:53 을 다른 프로그램이 쓰는 중)";
                return false;
            }

            _monitorFailReason = string.Empty;
            return true;
        }

        // ── ③ 시험 종료 → 감시 중지 ──
        // 이걸 하지 않으면 시험이 끝난 뒤에도 감시가 계속 돌아,
        // 학생이 메모장을 켜는 것마저 강제 종료된다.
        private void ApplyPhaseChange(IntPtr payload, uint payloadLen)
        {
            // 형식이 깨졌거나 모르는 단계면 무시한다.
            // 잘못 읽고 시험 도중에 감시를 꺼버리는 것이 더 위험하다.
            if (!ExamPhasePayload.TryDecode(payload, payloadLen, out ExamPhase phase)) return;

            // 아직 시험 중이라는 알림이면 감시를 건드리지 않는다.
            if (phase < ExamPhase.SubmitRequested) return;

            // EnsureProcessControl을 쓰지 않는다 — 감시를 켠 적도 없는 PC에서
            // 멈추자고 네이티브 DLL을 새로 불러올 이유가 없다.
            _processControl?.StopMonitoring();

            // 네트워크 감시를 멈추고 DNS 를 원래대로 돌린다.
            // 이걸 빠뜨리면 시험이 끝난 뒤에도 학생 PC 의 DNS 가 127.0.0.1 로 남는다.
            _networkControl?.StopMonitoring();
            DnsRedirectService.Restore();
        }

        // 네이티브 DLL은 여기서 처음 불린다.
        // 앱 시작 때가 아니라 시험이 시작될 때 만들어, DLL이 없더라도
        // 로그인·대기 화면은 정상 동작하게 한다.
        private ProcessControlService? EnsureProcessControl()
        {
            if (_processControl != null) return _processControl;

            try
            {
                ProcessControlService processControl = new ProcessControlService();
                processControl.CheatDetected += OnCheatDetected;
                _processControl = processControl;
            }
            catch (Exception ex)
            {
                // 이 메서드는 네트워크 수신 스레드에서 불린다.
                // DLL 로드 실패를 여기서 잡지 않으면 예외가 그대로 올라가 앱이 통째로 죽는다.
                System.Diagnostics.Debug.WriteLine($"프로세스 감시를 시작하지 못했습니다: {ex.Message}");
            }

            return _processControl;
        }

        // 프로세스 감시와 같은 이유로 시험이 시작될 때 처음 만든다.
        private NetworkControlService? EnsureNetworkControl()
        {
            if (_networkControl != null) return _networkControl;

            try
            {
                NetworkControlService networkControl = new NetworkControlService();
                networkControl.UnauthorizedDomainDetected += OnDomainDetected;
                _networkControl = networkControl;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"네트워크 감시를 시작하지 못했습니다: {ex.Message}");
            }

            return _networkControl;
        }

        // 금지 도메인 조회를 잡았을 때. 프로세스 적발과 같은 경로로 보고한다.
        // 조회를 막지는 않으므로 학생 쪽 화면 안내도 같은 문구를 쓴다.
        private void OnDomainDetected(string domain)
        {
            string description = $"금지된 사이트 접속 시도: {domain}";

            NetworkService.Instance.SendPacket(PacketType.CheatingAlert,
                BuildAlertPayload(CheatingAlertType.NetworkAccessAttempt, description));
            CheatWarning?.Invoke(description);
        }

        // ── ③ 적발 내용을 교수 PC로 보고하고, 학생 화면에도 알린다 ──
        // 네이티브 감시 스레드에서 불린다. 화면은 직접 건드리지 않고 알리기만 한다.
        private void OnCheatDetected(int type, string processName)
        {
            (CheatingAlertType alertType, string description) = Describe(type, processName);

            NetworkService.Instance.SendPacket(PacketType.CheatingAlert, BuildAlertPayload(alertType, description));
            CheatWarning?.Invoke(description);
        }

        // 적발 종류를 사람이 읽을 문구로 바꾼다.
        // 교수와 학생이 같은 문구를 보도록 여기 한 곳에서만 만든다.
        private static (CheatingAlertType Type, string Description) Describe(int type, string processName)
            => type switch
            {
                0 => (CheatingAlertType.BlacklistedProcessLaunched, $"금지된 프로그램 실행: {processName}"),
                1 => (CheatingAlertType.RequiredProcessTerminated, $"시험에 필요한 프로그램 종료: {processName}"),
                _ => (CheatingAlertType.UnauthorizedProcess, $"목록에 없는 프로그램 실행: {processName}"),
            };

        // Protocol.h의 CheatingAlertPayload 형식으로 만든다.
        //   [studentId 16][studentName 64][alertType 4][description 256] = 340바이트
        // 학번·이름 칸은 비워 둔다 — 교수 PC는 로그인 때 등록된 세션으로 누가 보냈는지 이미 안다.
        private static byte[] BuildAlertPayload(CheatingAlertType alertType, string description)
        {
            byte[] payload = new byte[340];
            BitConverter.GetBytes((uint)alertType).CopyTo(payload, 80);

            // 설명이 길면 잘라 담는다. 마지막 1바이트는 문자열 끝 표시로 남겨 둔다.
            byte[] text = Encoding.UTF8.GetBytes(description);
            Array.Copy(text, 0, payload, 84, Math.Min(text.Length, 255));

            return payload;
        }

        public void Dispose()
        {
            // 감시 스레드를 먼저 멈춘다. 앱이 내려가는 중에 콜백이 올라오면 안 되기 때문이다.
            _processControl?.Dispose();
            _processControl = null;

            _networkControl?.Dispose();
            _networkControl = null;

            // 정상 종료 경로에서도 DNS 를 반드시 되돌린다.
            // 시험 종료 신호를 못 받고 앱이 닫히는 경우가 여기에 걸린다.
            DnsRedirectService.Restore();
        }
    }
}
