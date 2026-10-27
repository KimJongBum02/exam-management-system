using ExamManager.Shared;
using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Windows.Threading;
using NetworkLib;

namespace StudentUI.Service
{
    // 교수가 이 PC 를 1:1 로 원격 조작할 수 있게 화면을 내어 준다.
    // 실습을 따라오지 못하는 학생을 교수가 직접 만져 주는 용도라 '자유' 상태에서 쓴다.
    //
    // 화면 전송·입력 주입·커서는 우리가 만들지 않는다 — 윈도우에 들어 있는
    // Desktop Sharing(RDPSession, rdpsharercom.dll)이 전부 한다. 우리가 하는 일은 네 가지뿐이다.
    //   1. 입력 잠금을 풀고   2. 공유 세션을 열어   3. 접속 문자열을 교수에게 넘기고
    //   4. 붙은 교수에게 '조작' 권한을 준다
    //
    // 연결 방향: 이 PC 가 받는 쪽(리스너)이고 교수가 접속해 온다.
    // 공유 세션은 NIC 마다 포트를 하나씩, 그것도 매번 다른 번호로 연다. 그래서 방화벽은
    // 포트가 아니라 '이 프로그램' 기준으로 인바운드를 열어 줘야 한다(AllowInbound).
    //
    // COM 호출은 dynamic 이 아니라 리플렉션(InvokeMember)으로 한다.
    // dynamic 은 Attendees 처럼 반환된 인터페이스에서 'System.__ComObject has no definition' 으로 실패한다.
    public class RemoteControlService
    {
        public static RemoteControlService Instance { get; } = new();

        // rdpsharercom.dll 의 RDPSession. ProgID 는 Rdpcomapi.RDPSession.1 이지만,
        // ProgID 는 다른 프로그램이 덮어쓸 수 있어 CLSID 로 직접 만든다.
        private static readonly Guid SharerClsid = new("9b78f0e6-3e05-4a5b-b2e8-e743a8956b65");

        // 이 이름의 규칙만 만들고 지운다. 학교가 넣어 둔 다른 규칙은 건드리지 않는다.
        private const string AllowRuleName = "ExamManager-Allow-RemoteControl";

        // CTRL_LEVEL (Windows SDK rdpencomapi.idl): 1=NONE, 2=VIEW, 3=INTERACTIVE, 4·5=요청 상태.
        // 2 를 넣으면 '보기 전용'이라 화면만 가고 입력이 안 들어간다(2026-10-27 실기에서 겪었다).
        private const int ControlLevelInteractive = 3;

        // 조작 권한 주기를 몇 번까지 조용히 재시도할지. 300ms × 10 = 3초.
        private const int GrantFailureLimit = 10;

        private object? _session;
        private DispatcherTimer? _grantTimer;
        private int _grantFailures;
        private bool _inboundAllowed;
        private View.Shared.RemoteControlBannerWindow? _banner;

        /// <summary>원격 제어가 켜져 있는지. 교수는 1:1 로만 하므로 이 PC 에는 하나뿐이다.</summary>
        public bool IsRunning => _session != null;

        private RemoteControlService() { }

        public void Start()
        {
            NetworkService.Instance.PacketReceived += OnPacketReceived;
            // 교수 앱이 꺼지면 공유를 계속 열어 둘 이유가 없다.
            NetworkService.Instance.Disconnected += _ => Release();

            // 지난 실행이 원격 중에 꺼졌으면 인바운드 규칙이 남는다. 훅과 달리 규칙은
            // 프로세스가 죽어도 사라지지 않으므로, 켤 때 한 번 치운다(FirewallPolicyService 와 같은 이유).
            //
            // 방화벽 COM 은 생각보다 느리다 — 없는 규칙 하나 찾는 데만 80ms, 처음 호출은 0.5초다(실측).
            // 앱이 뜨는 순간에 화면 스레드를 그만큼 잡으면 로그인 창이 그만큼 늦게 뜨다.
            // 지난 흔적을 치우는 일은 급하지 않으니 배경으로 돌린다.
            // (시작·종료 때는 순서가 중요해 — 규칙이 먼저 깔려야 교수가 붙는다 — 그대로 둔다.)
            Task.Run(RemoveInbound);
        }

        private void OnPacketReceived(PacketType type, IntPtr payload, uint payloadLen)
        {
            // 네이티브 수신 스레드에서 올라온다. 창과 COM 은 화면 스레드에서만 다룬다.
            if (type == PacketType.RemoteControlStart) UiThread.Post(BeginSafely);
            else if (type == PacketType.RemoteControlStop) UiThread.Post(EndSafely);
        }

        /// <summary>어느 스레드에서 불러도 된다. 원격을 끄고 공유 세션·방화벽 규칙을 되돌린다.</summary>
        public void Release()
        {
            UiThread.Post(EndSafely);

            // 앱이 끝나는 중이면 위 넘기기는 조용히 버려진다 — UiThread 는 종료가 시작된 뒤에는
            // 넘기지 않는다(그래야 종료 중 예외로 비정상 종료되지 않는다).
            // 창·타이머·공유 세션은 프로세스가 죽을 때 같이 사라지므로 그래도 괜찮지만,
            // 방화벽 규칙은 프로세스가 죽어도 남는다. 그것만 이 스레드에서 직접 지운다.
            // 방화벽 COM 은 화면 스레드가 아니어도 된다(Start 에서도 배경으로 돌린다).
            if (!_inboundAllowed) return;

            _inboundAllowed = false;
            RemoveInbound();
        }

        // 원격 제어가 안 되는 것과 학생 앱이 죽는 것은 무게가 다르다.
        // 시험 중이라면 앱이 죽는 순간 감시·차단이 풀리고 답안까지 위태로워진다.
        // 그래서 여기서 예외를 끝내고, 반쯤 켜진 상태로 남지 않게 정리한다.
        private void BeginSafely()
        {
            try { Begin(); }
            catch (Exception ex)
            {
                ReportFailure("학생 PC 에서 원격 제어를 시작하지 못했습니다.", ex);
                EndSafely();
            }
        }

        private void EndSafely()
        {
            try { End(); }
            catch (Exception ex) { Debug.WriteLine($"[RemoteControl] 종료 중 예외: {ex}"); }
        }

        // 학생 PC 에서 일어난 실패는 교수 화면에 그대로 띄워 준다.
        // 교수는 학생 PC 앞에 없고, 학생은 교수 화면을 볼 수 없다. 여기서 Debug 로만 남기면
        // 양쪽 모두 "눌렀는데 아무 일도 안 일어난다" 만 보게 되고, 그게 제일 불안하다.
        private static void ReportFailure(string reason, Exception? ex = null)
        {
            string message = ex == null ? reason : $"{reason}\n\n({(ex.InnerException ?? ex).Message})";
            Debug.WriteLine($"[RemoteControl] {message}");

            try
            {
                NetworkService.Instance.SendPacket(PacketType.RemoteControlFailed,
                                                   Encoding.UTF8.GetBytes(message));
            }
            catch (Exception sendEx) { Debug.WriteLine($"[RemoteControl] 실패를 알리지 못했습니다: {sendEx.Message}"); }
        }

        // ── 시작 ─────────────────────────────────────────────────────────
        // 화면 스레드에서만 부른다.
        private void Begin()
        {
            // 같은 요청이 또 왔다면 지난 번이 교수에게 닿지 않은 것이다(초대 패킷 유실·시간 초과).
            // 그냥 돌아가면 교수는 또 10초를 기다렸다 실패하고, 이 PC 는 "원격 제어 중" 배너를
            // 띄운 채 묶여 재시도 자체가 불가능해진다. 깨끗이 접고 처음부터 다시 차린다.
            if (_session != null) End();

            // 1. 입력 잠금을 먼저 푼다.
            //    저수준 훅은 주입된 입력도 똑같이 버리므로, 잠긴 채로 두면 교수가 아무것도 못 한다.
            //    훅에 '주입은 통과' 예외를 두는 방법도 있지만, 그러면 학생도 주입 도구로 잠금을 넘을 수 있다.
            //    원격이 끝나도 다시 잠그지 않는다 — 실습을 이어가야 하고, 교수 화면의 표시도 함께 풀린다.
            KeyboardLockService.Instance.Unlock();
            ClassControlService.Instance.ReleaseAll();

            // 2. 교수가 접속해 올 수 있게 인바운드를 연다.
            _inboundAllowed = AllowInbound();

            // 3. 공유 세션을 열고 초대를 만든다.
            string password = Guid.NewGuid().ToString("N").Substring(0, 12);
            try
            {
                _session = Activator.CreateInstance(Type.GetTypeFromCLSID(SharerClsid)!)!;
                Call(_session, "Open");

                object invitations = Get(_session, "Invitations")!;
                // (인증 문구, 그룹 이름, 암호, 받을 사람 수) — 교수 한 명만 받는다.
                object invitation = Call(invitations, "CreateInvitation", "ExamManager", "exam", password, 1)!;
                string connectionString = (string)Get(invitation, "ConnectionString")!;

                NetworkService.Instance.SendPacket(PacketType.RemoteControlInvitation,
                    RemoteControlPayload.Encode(connectionString, password));
            }
            catch (Exception ex)
            {
                ReportFailure("학생 PC 에서 화면 공유를 시작하지 못했습니다.", ex);
                End();
                return;
            }

            // 4. 붙은 교수에게 조작 권한을 준다.
            //    참석자는 2(VIEW, 보기 전용)로 붙으므로 3으로 올려 주지 않으면 화면만 보이고 조작은 안 된다.
            //    COM 이벤트(OnAttendeeConnected)를 받으려면 연결점을 직접 구현해야 해서, 짧은 주기로 확인한다.
            _grantFailures = 0;
            _grantTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _grantTimer.Tick += (_, _) => GrantControl();
            _grantTimer.Start();

            // 5. 학생에게 알린다. 못 알리면 원격을 시작하지 않는다 —
            //    자기 PC 가 조작되는 줄 모르는 채로 남는 것이 원격이 안 되는 것보다 나쁘다.
            try
            {
                // 직전 원격의 '끝났습니다' 안내가 아직 떠 있으면 치운다(배너가 두 장 겹치지 않게).
                _banner?.Close();

                var banner = new View.Shared.RemoteControlBannerWindow();
                banner.Closed += (_, _) => { if (ReferenceEquals(_banner, banner)) _banner = null; };
                _banner = banner;
                banner.Show();
            }
            catch (Exception ex)
            {
                ReportFailure("학생 PC 에 원격 제어 안내를 띄우지 못해 원격을 시작하지 않았습니다.", ex);
                End();
            }
        }

        // 붙어 있는 참석자(교수)의 조작 권한을 3(INTERACTIVE)으로 올린다.
        private void GrantControl()
        {
            if (_session == null) return;

            string problem;
            try
            {
                bool anyAttendee = false, granted = true;

                // IRDPSRAPIAttendeeManager 에는 Count 가 없다 — 열거만 된다.
                foreach (object attendee in (IEnumerable)Get(_session, "Attendees")!)
                {
                    anyAttendee = true;
                    if (Convert.ToInt32(Get(attendee, "ControlLevel")) != ControlLevelInteractive)
                        Set(attendee, "ControlLevel", ControlLevelInteractive);

                    // 올렸다고 끝이 아니다 — 되읽어 확인한다. 조용히 안 올라가면 교수 화면은
                    // 멀쩡히 보이는데 입력만 안 들어가고, 아무도 이유를 모른다.
                    if (Convert.ToInt32(Get(attendee, "ControlLevel")) != ControlLevelInteractive)
                        granted = false;
                }

                if (!anyAttendee || granted) { _grantFailures = 0; return; }   // 아직 안 붙었거나, 다 올라갔다
                problem = "권한을 올렸는데 값이 그대로다";
            }
            catch (Exception ex)
            {
                problem = $"오류: {(ex.InnerException ?? ex).Message}";
            }

            // 붙는 중일 수 있으니 몇 번은 조용히 재시도한다. 3초를 넘기면 포기하고 이유를 알린다 —
            // 교수는 '화면은 보이는데 안 눌리는' 창을 이유도 모르고 붙잡고 있게 되기 때문이다.
            if (++_grantFailures < GrantFailureLimit) return;

            _grantTimer?.Stop();
            ReportFailure($"교수 PC 에 조작 권한을 주지 못했습니다. 화면만 보이고 입력은 들어가지 않습니다.\n({problem})");
        }

        // ── 종료 ─────────────────────────────────────────────────────────
        // 화면 스레드에서만 부른다. 여러 번 불려도 안전하다.
        private void End()
        {
            _grantTimer?.Stop();
            _grantTimer = null;

            // 배너를 그냥 지우면 학생은 원격이 끝난 것인지 알림이 깨진 것인지 알 수 없다.
            // 끝났다는 말을 남기고 스스로 닫히게 한다(참조는 그 창의 Closed 에서 비워진다).
            try { _banner?.ShowEnded(); }
            catch (Exception ex)
            {
                // 문구를 못 바꿨으면 "원격 제어 중" 이 그대로 남아 거짓이 된다. 그럴 땐 그냥 닫는다.
                Debug.WriteLine($"[RemoteControl] 종료 안내를 띄우지 못했습니다: {ex.Message}");
                try { _banner?.Close(); }
                catch (Exception closeEx) { Debug.WriteLine($"[RemoteControl] 배너를 닫지 못했습니다: {closeEx.Message}"); }
                _banner = null;
            }

            if (_session != null)
            {
                try { Call(_session, "Close"); }
                catch (Exception ex) { Debug.WriteLine($"[RemoteControl] 세션을 닫지 못했습니다: {ex.Message}"); }
                _session = null;
            }

            // 열지도 않은 규칙을 지우려 들지 않는다. 방화벽 COM 은 한 번에 80~500ms 가 들고
            // 그동안 학생 화면이 멈춘다(실측). 교수 쪽 시간 초과로 온 Stop 처럼
            // 시작조차 안 된 원격에까지 그 값을 치를 이유가 없다.
            // Release 가 앱 종료 중에 먼저 지운 경우도 이 가드에 걸린다.
            if (!_inboundAllowed) return;

            _inboundAllowed = false;
            RemoveInbound();
        }

        // ── 방화벽 ───────────────────────────────────────────────────────
        // 공유 세션이 여는 포트는 매번 달라 포트 규칙을 쓸 수 없다. 프로그램 경로로 연다.
        // 시험 중 아웃바운드 차단(FirewallPolicyService)과는 방향도 대상도 겹치지 않는다.
        // 규칙을 넣었으면 true. 안 넣었는데 지우려 들면 학생 화면이 괜히 멈춘다.
        private static bool AllowInbound()
        {
            try
            {
                object policy = CreateCom("HNetCfg.FwPolicy2");
                RemoveRule(policy);

                object rule = CreateCom("HNetCfg.FWRule");
                Set(rule, "Name", AllowRuleName);
                Set(rule, "Description", "원격 제어 중 교수 PC 의 접속 허용. ExamManager 가 끝나면 지운다.");
                Set(rule, "ApplicationName", Environment.ProcessPath);
                Set(rule, "Direction", 1);            // NET_FW_RULE_DIR_IN
                Set(rule, "Action", 1);               // NET_FW_ACTION_ALLOW
                Set(rule, "Protocol", 6);             // TCP
                Set(rule, "Profiles", 0x7FFFFFFF);    // 도메인 · 개인 · 공용
                Set(rule, "Enabled", true);
                Call(Get(policy, "Rules")!, "Add", rule);
                return true;
            }
            catch (Exception ex)
            {
                // 규칙을 못 넣어도 원격은 계속 간다 — 이미 허용돼 있는 PC 라면 그대로 붙는다.
                // 안 붙으면 교수 창에 연결 실패가 그대로 보인다.
                Debug.WriteLine($"[RemoteControl] 인바운드 허용 실패: {ex.Message}");
                return false;
            }
        }

        private static void RemoveInbound()
        {
            try { RemoveRule(CreateCom("HNetCfg.FwPolicy2")); }
            catch (Exception ex) { Debug.WriteLine($"[RemoteControl] 인바운드 규칙 삭제 실패: {ex.Message}"); }
        }

        // 같은 이름의 규칙이 없으면 Item 이 '파일을 찾을 수 없음'을 던진다(FirewallPolicyService 와 같다).
        private static void RemoveRule(object policy)
        {
            object rules = Get(policy, "Rules")!;
            try { Call(rules, "Item", AllowRuleName); }
            catch (Exception ex) when (ex.InnerException is System.IO.FileNotFoundException) { return; }
            catch (System.IO.FileNotFoundException) { return; }

            Call(rules, "Remove", AllowRuleName);
        }

        // ── COM 호출 ─────────────────────────────────────────────────────
        private static object CreateCom(string progId)
            => Activator.CreateInstance(Type.GetTypeFromProgID(progId, throwOnError: true)!)!;

        private static object? Get(object target, string name)
            => target.GetType().InvokeMember(name, BindingFlags.GetProperty, null, target, null);

        private static void Set(object target, string name, object? value)
            => target.GetType().InvokeMember(name, BindingFlags.SetProperty, null, target, new[] { value });

        private static object? Call(object target, string name, params object?[] args)
            => target.GetType().InvokeMember(name, BindingFlags.InvokeMethod, null, target, args);
    }
}
