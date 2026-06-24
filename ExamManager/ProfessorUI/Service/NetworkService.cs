using System;
using NetworkLib;

namespace ProfessorUI.Service
{
    public class NetworkService : IDisposable
    {
        private ProfessorServer? _server;

        public event Action<string, string, string, string>? StudentConnected;
        public event Action<string, string, string, DisconnectReason>? StudentDisconnected;
        public event Action<string, string, string, PacketType, IntPtr, uint>? PacketReceived;
        public event Action<string, string, string, string, long, string>? FileReceived;
        public event Action<string, string, int>? FileProgress;
        public event Action<string, string>? FileError;

        public NetworkService(int port = 9000)
        {
            // NetworkLibrary 초기화는 앱 수명 동안 한 번만 필요합니다.
            NetworkLibrary.Initialize();
            
            _server = new ProfessorServer(port);

            _server.StudentConnected += (sid, stid, name, ip) => StudentConnected?.Invoke(sid, stid, name, ip);
            _server.StudentDisconnected += (sid, stid, name, reason) => StudentDisconnected?.Invoke(sid, stid, name, reason);
            _server.PacketReceived += (sid, stid, name, type, payload, len) => PacketReceived?.Invoke(sid, stid, name, type, payload, len);
            _server.FileReceived += (tid, senderId, fn, tp, sz, pw) => FileReceived?.Invoke(tid, senderId, fn, tp, sz, pw);
            _server.FileProgress += (tid, fn, pct) => FileProgress?.Invoke(tid, fn, pct);
            _server.FileError += (tid, msg) => FileError?.Invoke(tid, msg);
        }

        public bool StartServer() => _server?.Start() ?? false;
        
        public void StopServer() => _server?.Stop();

        public int GetConnectedCount() => _server?.GetConnectedCount() ?? 0;

        public void Broadcast(PacketType type, byte[] payload) => _server?.Broadcast(type, payload);
        
        public void SendToSession(string sessionId, PacketType type, byte[] payload) => _server?.SendToSession(sessionId, type, payload);
        
        public void BroadcastFile(string filePath, string archivePassword = "") => _server?.BroadcastFile(filePath, archivePassword);
        
        public void SendFileToSession(string sessionId, string filePath, string archivePassword = "") => _server?.SendFileToSession(sessionId, filePath, archivePassword);

        public void Dispose()
        {
            _server?.Dispose();
            // NetworkLibrary.Cleanup(); 은 애플리케이션 종료 시 호출해야 하므로, Service가 여러번 생성/소멸될 수 있다면 주의가 필요합니다.
            // 현재 구조에서는 MainWindow와 라이프사이클을 함께 하므로 Dispose 시 호출합니다.
            NetworkLibrary.Cleanup();
        }
    }
}
