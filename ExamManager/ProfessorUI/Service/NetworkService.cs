using System;
using System.Net;
using System.Net.Sockets;
using NetworkLib;

namespace ProfessorUI.Service
{
    public class NetworkService : IDisposable
    {
        // ⭐ 앱 전체에서 공유하는 단일 서버 인스턴스
        public static NetworkService Instance { get; } = new NetworkService();

        // 학생에게 알려줄 이 PC의 LAN IPv4 주소를 구한다.
        public static string GetLocalIPv4()
        {
            // 외부로 나가는 경로의 인터페이스 IP를 고른다 (실제 패킷은 보내지 않음).
            // 가상 어댑터(VMware 등)가 섞여 있어도 실제 LAN NIC를 고르는 데 가장 안정적이다.
            try
            {
                using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                socket.Connect("8.8.8.8", 65530);
                if (socket.LocalEndPoint is IPEndPoint ep)
                    return ep.Address.ToString();
            }
            catch { }

            // 폴백: 첫 번째 비루프백 IPv4
            try
            {
                foreach (var ip in Dns.GetHostAddresses(Dns.GetHostName()))
                    if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                        return ip.ToString();
            }
            catch { }

            return "IP 확인 불가";
        }

        private ProfessorServer? _server;

        public event Action<string, string, string, string>? StudentConnected;
        public event Action<string, string, string, DisconnectReason>? StudentDisconnected;
        public event Action<string, string, string, PacketType, IntPtr, uint>? PacketReceived;
        public event Action<string, string, string, string, long, string>? FileReceived;
        public event Action<string, string, int>? FileProgress;
        public event Action<string, string>? FileError;

        // 교수 → 학생 파일 전송 진행률/오류 (sessionId, studentId, transferId, fileName/message, ...)
        public event Action<string, string, string, string, int>? SendProgress;
        public event Action<string, string, string, string>? SendError;

        public NetworkService(int port = ServerService.DefaultPort)
        {
            // NetworkLibrary 초기화는 앱 수명 동안 한 번만 필요합니다.
            NetworkLibrary.Initialize();

            CreateServer(port);
        }

        // 포트를 바꾸려면 서버를 새로 만들어야 한다. 네이티브 쪽은 서버 인스턴스마다
        // 콜백을 다시 걸어 주므로, 여기서 다시 만들어야 이벤트가 끊기지 않는다.
        // 바깥에서 이 NetworkService 를 구독한 쪽은 그대로 두어도 된다.
        public void RecreateServer(int port)
        {
            _server?.Dispose();
            CreateServer(port);
        }

        private void CreateServer(int port)
        {
            _server = new ProfessorServer(port);

            _server.StudentConnected += (sid, stid, name, ip) => StudentConnected?.Invoke(sid, stid, name, ip);
            _server.StudentDisconnected += (sid, stid, name, reason) => StudentDisconnected?.Invoke(sid, stid, name, reason);
            _server.PacketReceived += (sid, stid, name, type, payload, len) => PacketReceived?.Invoke(sid, stid, name, type, payload, len);
            _server.FileReceived += (tid, senderId, fn, tp, sz, pw) => FileReceived?.Invoke(tid, senderId, fn, tp, sz, pw);
            _server.FileProgress += (tid, fn, pct) => FileProgress?.Invoke(tid, fn, pct);
            _server.FileError += (tid, msg) => FileError?.Invoke(tid, msg);
            _server.SendProgress += (sid, stid, tid, fn, pct) => SendProgress?.Invoke(sid, stid, tid, fn, pct);
            _server.SendError += (sid, stid, tid, msg) => SendError?.Invoke(sid, stid, tid, msg);
        }

        public bool StartServer() => _server?.Start() ?? false;

        public void StopServer() => _server?.Stop();

        public int GetConnectedCount() => _server?.GetConnectedCount() ?? 0;

        // 새 학생 접속을 받고 있는지 (네이티브 접속 받기 루프가 살아 있는지)
        // 이 기능이 없는 예전 DLL 과 함께 배포되면 몇 초마다 앱이 죽는다. 그때는 감지만 포기하고 열려 있다고 본다.
        public bool IsServerListening
        {
            get
            {
                try { return _server?.IsListening ?? false; }
                catch (EntryPointNotFoundException) { return true; }
            }
        }

        public void Broadcast(PacketType type, byte[] payload) => _server?.Broadcast(type, payload);

        public void SendToSession(string sessionId, PacketType type, byte[] payload) => _server?.SendToSession(sessionId, type, payload);

        public void BroadcastFile(string filePath, string archivePassword = "") => _server?.BroadcastFile(filePath, archivePassword);

        // 전송을 시작했으면 true. 해당 세션이 없으면 false.
        public bool SendFileToSession(string sessionId, string filePath, string archivePassword = "") => _server?.SendFileToSession(sessionId, filePath, archivePassword) ?? false;

        // ── 채팅 ──────────────────────────────────────────────────────────
        public bool BroadcastChat(string message)    => _server?.BroadcastChat(message)    ?? false;
        public bool SendChatToSession(string sessionId, string message) => _server?.SendChatToSession(sessionId, message) ?? false;

        public void Dispose()
        {
            _server?.Dispose();
            // NetworkLibrary.Cleanup(); 은 애플리케이션 종료 시 호출해야 하므로, Service가 여러번 생성/소멸될 수 있다면 주의가 필요합니다.
            // 현재 구조에서는 MainWindow와 라이프사이클을 함께 하므로 Dispose 시 호출합니다.
            NetworkLibrary.Cleanup();
        }
    }
}
