#pragma once
#include "pch.h"
#include "Protocol.h"
#include "FileTransfer.h"

// ─── 전방 선언 ─────────────────────────────────────────────────────
class FileTransferReceiver;

// ══════════════════════════════════════════════════════════════════════
//  ClientSession — 서버 측 개별 학생 연결 세션
//  ProfessorServer가 accept()로 얻은 소켓을 이 클래스로 래핑합니다.
// ══════════════════════════════════════════════════════════════════════
class ClientSession
{
public:
    // ── 식별 정보 ─────────────────────────────────────────────────
    std::string sessionId;    // UUID 문자열 (서버가 부여)
    std::string studentId;    // 학번 (로그인 후 설정)
    std::string studentName;  // 이름 (로그인 후 설정)
    std::string remoteAddr;   // IP:포트 문자열

    // ── 상태 ──────────────────────────────────────────────────────
    std::atomic<bool>           alive{ true };
    std::atomic<uint32_t>       status{ 0 };   // StudentStatus
    std::chrono::steady_clock::time_point lastHeartbeat;

    // ── 소켓 ──────────────────────────────────────────────────────
    SOCKET    sock;
    std::mutex sendMutex;

    // ── 콜백 (ProfessorServer가 설정) ─────────────────────────────
    std::function<void(ClientSession*, PacketType, const uint8_t*, uint32_t)> onPacket;
    std::function<void(ClientSession*, int reason)>                           onDisconnected;

    // ── 파일 수신 관리자 (ProfessorServer가 공유 인스턴스 주입) ───
    FileTransferReceiver* fileReceiver{ nullptr };

    // ─────────────────────────────────────────────────────────────
    explicit ClientSession(SOCKET s, const std::string& addr);
    ~ClientSession();

    // 수신 루프를 새 스레드에서 시작합니다
    void StartRecvLoop();

    // 이 세션에 패킷을 전송합니다 (스레드 안전)
    bool Send(PacketType type, const void* payload = nullptr, uint32_t payloadLen = 0);

    // Heartbeat 타임아웃 여부 확인
    bool IsHeartbeatExpired(int timeoutSecs = 15) const;

    // 연결을 종료합니다
    void Close(int reason = static_cast<int>(DisconnectReason::ServerShutdown));

private:
    std::thread recvThread_;

    void RecvLoop();
    bool HandleFileChunk(PacketType type, const uint8_t* payload, uint32_t payloadLen);
};
