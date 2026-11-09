#pragma once
#include "pch.h"
#include "Protocol.h"
#include "FileTransfer.h"

// ══════════════════════════════════════════════════════════════════════
//  StudentClient — 학생 PC에서 실행되는 TCP 클라이언트
//  교수 서버에 연결하고 5초마다 Heartbeat를 전송합니다.
// ══════════════════════════════════════════════════════════════════════
class StudentClient
{
public:
    // ── 이벤트 콜백 ───────────────────────────────────────────────
    std::function<void(const char* serverIp, int port)>          onConnected;
    std::function<void(int reason)>                              onDisconnected;
    std::function<void(uint32_t type, const uint8_t* payload, uint32_t len)> onPacketReceived;
    std::function<void(const char* message)>                     onError;

    FileReceivedCb onFileReceived;
    FileProgressCb onFileProgress;
    FileErrorCb    onFileError;

    // ─────────────────────────────────────────────────────────────
    StudentClient();
    ~StudentClient();

    bool Connect   (const std::string& serverIp, int port);
    void Disconnect();
    bool IsConnected() const;

    bool SendPacket(PacketType type, const void* payload = nullptr, uint32_t payloadLen = 0);
    bool SendFile  (const std::string& filePath, const std::string& password);

private:
    SOCKET            sock_{ INVALID_SOCKET };
    std::mutex        sendMutex_;
    std::atomic<bool> connected_{ false };
    std::thread       recvThread_;
    std::thread       heartbeatThread_;

    FileTransferReceiver fileReceiver_;

    void RecvLoop();
    void HeartbeatLoop();
    void HandleDisconnect(int reason);
};
