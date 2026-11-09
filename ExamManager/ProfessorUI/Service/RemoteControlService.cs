using ExamManager.Shared;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using NetworkLib;

namespace ProfessorUI.Service
{
    // 원격 제어의 '말 거는 쪽'. 화면을 그리는 일은 RemoteControlWindow 가 한다.
    //
    // 흐름: 교수가 요청 → 학생이 잠금을 풀고 공유 세션을 열어 접속 문자열을 보내 줌 → 그 문자열로 접속.
    // 학생 앱이 오래된 판이거나 공유 세션을 못 열면 답이 영영 오지 않으므로 기다리는 시간을 둔다.
    public class RemoteControlService
    {
        public static RemoteControlService Instance { get; } = new();

        // 학생이 공유 세션을 열고 초대를 만드는 데 걸리는 시간. 넘으면 포기하고 교수에게 알린다.
        private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);

        private bool _started;
        private string? _waitingSessionId;
        private DispatcherTimer? _waitTimer;

        /// <summary>접속 문자열이 왔다 (sessionId, 접속 문자열, 암호). 화면 스레드에서 알린다.</summary>
        public event Action<string, string, string>? InvitationReceived;

        /// <summary>기다리다 못 받았다 (sessionId). 화면 스레드에서 알린다.</summary>
        public event Action<string>? RequestTimedOut;

        /// <summary>학생 PC 가 원격 제어를 하지 못했다 (sessionId, 이유). 화면 스레드에서 알린다.</summary>
        public event Action<string, string>? Failed;

        private RemoteControlService() { }

        public void Start()
        {
            if (_started) return;
            _started = true;

            NetworkService.Instance.PacketReceived += OnPacketReceived;
        }

        /// <summary>그 학생에게 원격 제어를 시작하자고 알린다. 화면 스레드에서 호출한다.</summary>
        public void Request(string sessionId)
        {
            _waitingSessionId = sessionId;

            _waitTimer?.Stop();
            _waitTimer = new DispatcherTimer { Interval = WaitLimit };
            _waitTimer.Tick += (_, _) =>
            {
                try
                {
                    // 학생은 요청을 받자마자 잠금을 풀고 공유 세션을 열고 안내 배너를 띄운다.
                    // 답이 늦거나 유실되면 그 상태가 그대로 남아 학생 화면에 "원격 제어 중" 이
                    // 영구히 떠 있게 된다. 포기할 때 반드시 치우게 한다.
                    Stop(sessionId);
                    RequestTimedOut?.Invoke(sessionId);
                }
                catch (Exception ex) { Debug.WriteLine($"[RemoteControl] 대기 시간 처리 중 예외: {ex}"); }
            };
            _waitTimer.Start();

            NetworkService.Instance.SendToSession(sessionId, PacketType.RemoteControlStart, Array.Empty<byte>());
        }

        /// <summary>한/영·한자 키를 그 학생 PC 에서 누르게 한다. 화면 스레드에서 호출한다.</summary>
        public void SendImeKey(string sessionId, bool hanja)
            => NetworkService.Instance.SendToSession(sessionId, PacketType.RemoteControlImeKey,
                                                    new byte[] { (byte)(hanja ? 1 : 0) });

        /// <summary>그 학생의 공유 세션을 닫게 한다. 창을 닫을 때 부른다.</summary>
        public void Stop(string sessionId)
        {
            StopWaiting();
            NetworkService.Instance.SendToSession(sessionId, PacketType.RemoteControlStop, Array.Empty<byte>());
        }

        private void OnPacketReceived(string sessionId, string studentId, string name,
                                      PacketType type, IntPtr payload, uint payloadLen)
        {
            if (type == PacketType.RemoteControlFailed)
            {
                // 실패는 창이 열린 뒤에도 온다(조작 권한 부여 실패 등).
                // 대기 중인지 따지지 않고 그대로 전한다 — 교수가 모르면 안 되는 내용이다.
                string reason = (Marshal.PtrToStringUTF8(payload, (int)payloadLen) ?? "").Split('\0')[0];
                UiThread.Post(() =>
                {
                    try
                    {
                        StopWaiting();
                        Failed?.Invoke(sessionId, reason);
                    }
                    catch (Exception ex) { Debug.WriteLine($"[RemoteControl] 실패 처리 중 예외: {ex}"); }
                });
                return;
            }

            if (type != PacketType.RemoteControlInvitation) return;
            if (sessionId != _waitingSessionId) return;   // 기다리지 않은 학생의 응답은 버린다
            // 수신 버퍼는 이 콜백이 끝나면 사라지므로 화면 스레드로 넘기기 전에 읽는다.
            if (!RemoteControlPayload.TryDecode(payload, payloadLen, out string connectionString, out string password))
                return;

            // 이 알림을 받는 쪽에서 터진 예외가 화면 스레드로 새어나가면
            // 교수 앱이 통째로 죽고 서버가 닫혀 학생 전원이 끊긴다.
            UiThread.Post(() =>
            {
                try
                {
                    StopWaiting();
                    InvitationReceived?.Invoke(sessionId, connectionString, password);
                }
                catch (Exception ex) { Debug.WriteLine($"[RemoteControl] 초대 처리 중 예외: {ex}"); }
            });
        }

        private void StopWaiting()
        {
            _waitTimer?.Stop();
            _waitTimer = null;
            _waitingSessionId = null;
        }
    }
}
