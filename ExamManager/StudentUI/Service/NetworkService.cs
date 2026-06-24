using System;
using NetworkLib;

namespace StudentUI.Service
{
    public class NetworkService : IDisposable
    {
        private StudentClient? _client;

        public event Action<string, int>? Connected;
        public event Action<DisconnectReason>? Disconnected;
        public event Action<PacketType, IntPtr, uint>? PacketReceived;
        public event Action<string, string, string, string, long, string>? FileReceived;
        public event Action<string, string, int>? FileProgress;
        public event Action<string, string>? FileError;
        public event Action<string>? Error;

        public NetworkService()
        {
            NetworkLibrary.Initialize();
            _client = new StudentClient();

            _client.Connected += (ip, port) => Connected?.Invoke(ip, port);
            _client.Disconnected += reason => Disconnected?.Invoke(reason);
            _client.PacketReceived += (t, p, l) => PacketReceived?.Invoke(t, p, l);
            _client.FileReceived += (tid, sid, fn, tp, sz, pw) => FileReceived?.Invoke(tid, sid, fn, tp, sz, pw);
            _client.FileProgress += (tid, fn, pct) => FileProgress?.Invoke(tid, fn, pct);
            _client.FileError += (tid, msg) => FileError?.Invoke(tid, msg);
            _client.Error += msg => Error?.Invoke(msg);
        }

        public bool Connect(string serverIp, int port = 9000) => _client?.Connect(serverIp, port) ?? false;
        
        public void Disconnect() => _client?.Disconnect();

        public bool IsConnected => _client?.IsConnected ?? false;

        public void SendPacket(PacketType type, byte[]? payload = null) => _client?.SendPacket(type, payload);

        public void SendFile(string filePath, string archivePassword = "") => _client?.SendFile(filePath, archivePassword);

        public void Dispose()
        {
            _client?.Dispose();
            NetworkLibrary.Cleanup();
        }
    }
}
