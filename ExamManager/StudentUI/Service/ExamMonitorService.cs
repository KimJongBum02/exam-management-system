using ExamManager.Shared;
using NetworkLib;
using System;
using System.Collections.Generic;
using System.Text;

namespace StudentUI.Service
{
    // 학생 화면에 보이는 네트워크 차단 상태.
    public enum NetworkBlockState
    {
        NotApplied, // 시험 시작 전
        Blocked,    // 교수 PC 연결만 남기고 막는 중
        Failed,     // 막지 못함 — 사유는 교수 화면에 보고됨
        Released,   // 답안 제출 뒤 풀림
    }

    // 교수 PC의 명령에 맞춰 프로세스 감시를 켜고, 적발 내용을 교수 PC로 되돌려 보낸다.
    //
    // 교수 PC에서 오는 패킷 세 가지를 순서대로 처리한다.
    //   ① ProcessListUpdate — 이번 시험의 감시 목록. 받아서 네이티브에 넣는다.
    //   ② ExtractArchive    — 시험 시작. 압축 해제가 끝난 뒤에 감시를 켠다.
    //   ③ ExamPhaseChange   — 시험 종료. 감시를 멈춘다.
    //
    // 프로세스 감시와 네트워크 차단을 같은 자리에서 켠다.
    // 교수가 따로 누를 것이 없도록 시험 시작 신호에 그대로 묶었다.
    // 푸는 시점은 다르다. 프로세스 감시는 시험 종료(③)나 답안 제출 중 먼저 오는 때에, 네트워크 차단은 답안 제출이 끝난 뒤에 푼다.
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

        // 마지막으로 받은 감시 목록. 이어 받기 기록에 함께 남긴다 — 다시 켜면 교수가 목록을 다시 보내지 않는다.
        private List<string> _whitelist = new();
        private List<string> _blacklist = new();

        // 다시 켠 프로그램이 이어 받을 시험. 로그인이 끝나면 감시와 차단을 다시 건다(OnLoggedIn).
        // 로그인 화면은 이 값으로 같은 학번인지 확인한다.
        private ExamSessionStore.ExamSession? _resumeSession;
        public ExamSessionStore.ExamSession? PendingResume => _resumeSession;

        // 로그인한 학생. 이어 받기 기록에 함께 남긴다.
        private string _studentNumber = string.Empty;
        private string _studentName = string.Empty;

        // 현재 로그인된 학생 정보 (답안 파일명 생성 등에 사용)
        public string StudentNumber => _studentNumber;
        public string StudentName => _studentName;

        // 네트워크 차단에서 뺄 교수 PC 주소. 로그인 때 접속한 주소를 그대로 쓴다.
        private string _professorIp = string.Empty;

        // 학생 화면의 '네트워크 차단' 줄이 보여 줄 상태. 사이트가 안 열리는 이유를 학생이 알 수 있게 한다.
        public NetworkBlockState NetworkState { get; private set; } = NetworkBlockState.NotApplied;
        public event Action? NetworkStateChanged;

        private void SetNetworkState(NetworkBlockState state)
        {
            NetworkState = state;
            NetworkStateChanged?.Invoke();
        }

        private ExamMonitorService() { }

        // 앱 시작 시 한 번 호출 — 구독만 해두고 감시는 아직 켜지 않는다.
        public void Start()
        {
            NetworkService.Instance.PacketReceived += OnPacketReceived;
            NetworkService.Instance.Connected += (ip, _) => _professorIp = ip;
            ExamFileStore.Instance.ExamStartHandled += OnExamStartHandled;
            AnswerSubmitService.Instance.StateChanged += OnSubmitStateChanged;
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

            _whitelist = whitelist;
            _blacklist = blacklist;

            // 시험 중에 목록이 바뀌면(프로그램 관리 [적용]) 이어 받기 기록도 맞춘다.
            if (ExamSessionStore.Load() is { } session)
            {
                session.Whitelist = whitelist;
                session.Blacklist = blacklist;
                ExamSessionStore.Save(session);
            }

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
            bool networkOn = ApplyNetworkBlock();

            ReportMonitorStatus(processOn, networkOn);

            // 도중에 프로그램이 꺼져도 이어서 치를 수 있게 남겨 둔다.
            // 압축이 풀린 뒤라 암호를 적어 둬도 먼저 볼 수 있는 것이 없다.
            var files = ExamFileStore.Instance;
            if (files.HasExamFolder && files.IsExtracted)
            {
                ExamSessionStore.Save(new ExamSessionStore.ExamSession
                {
                    StudentNumber = _studentNumber,
                    StudentName = _studentName,
                    ArchiveName = files.ArchiveName,
                    Password = files.ExamPassword,
                    DeliveredFiles = new List<string>(files.DeliveredFiles),
                    Whitelist = _whitelist,
                    Blacklist = _blacklist,
                    StartedAtUtc = ExamTimeStore.Instance.IsRunning ? ExamTimeStore.Instance.StartedAtUtc : DateTime.UtcNow,
                });
            }
        }

        // ── 이어 받기 ── (ExamSessionStore 참고)
        // 앱이 뜰 때 부른다. 차단은 켜질 때 이미 풀었다(App.OnStartup).
        // 감시와 차단은 같은 학번으로 교수에게 다시 로그인한 뒤에 새로 건다.
        public void ResumeFromSession(ExamSessionStore.ExamSession session)
        {
            _resumeSession = session;
            _whitelist = session.Whitelist;
            _blacklist = session.Blacklist;
        }

        // 로그인 패킷을 보낸 뒤 부른다. 누가 로그인했는지 기억해 두고(이어 받기 기록용),
        // 이어 받을 시험이 있으면 감시와 차단을 다시 건다.
        // 교수 PC 는 로그인 전에 온 패킷을 버리므로 감시 보고는 이때 해야 한다.
        // 교수 PC 주소가 바뀌었을 수 있어 차단은 새 주소로 다시 건다.
        // 교수가 이미 시험을 끝냈으면 감시는 켜지 않고 차단만 유지한 채 답안 제출을 기다린다.
        public void OnLoggedIn(string studentNumber, string studentName)
        {
            _studentNumber = studentNumber;
            _studentName = studentName;

            var session = _resumeSession;
            if (session == null) return;
            _resumeSession = null;

            bool processOn = false;
            if (!session.ExamEnded)
            {
                ProcessControlService? processControl = EnsureProcessControl();
                processControl?.SetWhitelist(string.Join("|", _whitelist));
                processControl?.SetBlacklist(string.Join("|", _blacklist));
                processOn = processControl?.StartMonitoring() ?? false;
            }

            bool networkOn = ApplyNetworkBlock();
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

        // 교수 PC 연결만 남기고 바깥 통신을 막는다 (FirewallPolicyService 참고).
        private bool ApplyNetworkBlock()
        {
            string? failReason = FirewallPolicyService.Apply(_professorIp);
            _monitorFailReason = failReason ?? string.Empty;
            SetNetworkState(failReason == null ? NetworkBlockState.Blocked : NetworkBlockState.Failed);

            // 앱이 강제로 끝나도 차단이 남지 않게 지킴이를 띄운다(FirewallGuard 참고).
            if (failReason == null) FirewallGuard.Start();
            return failReason == null;
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

            // 시험이 종료되었으므로 남은 세션 기록을 지운다. (다시 켰을 때 종료 화면으로 잠기지 않도록)
            _resumeSession = null;
            ExamSessionStore.Clear();

            // 네트워크 차단은 여기서 풀지 않는다(OnSubmitStateChanged 참고).
        }

        // ── 답안 제출이 끝나면 감시를 멈추고 네트워크 차단을 푼다 ──
        // 시험 종료에서 풀면, 교수가 답안을 걷기 전까지 답안 폴더가 학생 PC 에 남은 채로 인터넷이 열린다.
        // 답안 전송은 교수 PC 로 가는 연결이라 차단 중에도 된다.
        //
        // 풀었으면 교수에게도 알린다. 교수 표의 '인터넷' 칸이 이 보고로 '해제됨'이 된다.
        // 제출 성공은 두 번 올라올 수 있어(시험 파일 삭제 실패 시) 막혀 있을 때만 푼다.
        //
        // 제출이 끝나면 이 학생의 시험도 끝이다. 이어 받을 기록도 지운다.
        private void OnSubmitStateChanged(AnswerSubmitState state, string message)
        {
            if (state != AnswerSubmitState.Succeeded) return;

            _resumeSession = null;
            ExamSessionStore.Clear();

            // 프로그램 감시도 여기서 멈춘다. 먼저 낸 학생은 시험 종료(③)를 기다리는 동안
            // 켜는 프로그램마다 교수에게 쓸데없는 경고가 올라간다. 답안은 이미 교수 PC 에 있다.
            // 교수에게 따로 보고하지 않는다 — 교수 쪽은 '감시 꺼짐' 보고를 감시 실패 경고로 올린다.
            // [제출] 버튼으로 낸 경우 화면 스레드에서 불리는데, 멈춤은 감시 스레드가 끝나길 기다리므로 따로 돌린다.
            ProcessControlService? processControl = _processControl;
            if (processControl != null) System.Threading.Tasks.Task.Run(processControl.StopMonitoring);

            if (NetworkState != NetworkBlockState.Blocked) return;
            if (!FirewallPolicyService.Restore()) return;

            SetNetworkState(NetworkBlockState.Released);
            NetworkService.Instance.SendPacket(PacketType.MonitorStatusReport,
                MonitorStatusPayload.Encode(MonitorFlags.NetworkReleased));
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

        // ── ③ 적발 내용을 교수 PC로 보고하고, 학생 화면에도 알린다 ──
        // 네이티브 감시 스레드에서 불린다. 화면은 직접 건드리지 않고 알리기만 한다.
        private void OnCheatDetected(int type, string processName)
        {
            // 학생 화면 경고와 교수 알림이 같은 이름을 보이도록, 아는 프로그램은 사람이 부르는 이름으로 바꾼다.
            // 네이티브가 준 이름("Calculator (CalculatorApp.exe)")에서 실행 파일을 찾아 사전에 있으면 "계산기"로.
            string friendlyName = KnownPrograms.FriendlyLabel(processName);
            (CheatingAlertType alertType, string description) = Describe(type, friendlyName);

            NetworkService.Instance.SendPacket(PacketType.CheatingAlert, BuildAlertPayload(alertType, description));

            // 허용 프로그램 종료는 학생이 스스로 닫은 것이라 학생 화면에는 띄우지 않는다.
            // 학생 화면의 감시 알림은 부정행위 경고로 보이며, 원래 '왜 꺼졌는지' 알려 주려는 용도다.
            if (alertType != CheatingAlertType.RequiredProcessTerminated)
                CheatWarning?.Invoke(description);
        }

        // 적발 종류를 사람이 읽을 문구로 바꾼다.
        // 교수와 학생이 같은 문구를 보도록 여기 한 곳에서만 만든다.
        private static (CheatingAlertType Type, string Description) Describe(int type, string processName)
            => type switch
            {
                0 => (CheatingAlertType.BlacklistedProcessLaunched, $"금지된 프로그램 실행: {processName}"),
                1 => (CheatingAlertType.RequiredProcessTerminated, $"[참고] 허용 프로그램 종료: {processName}"),
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

            // 어떤 경로로 끝나든 네트워크 차단을 푼다. 시험 중이어도 예외 없다 —
            // 차단이 남으면 그 PC 가 인터넷을 잃는다. 이어 치를 때는 같은 학번으로 다시 로그인하면 새로 건다.
            FirewallPolicyService.Restore();
        }
    }
}
