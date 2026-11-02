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
    // 로그인을 거절한 세션. 하트비트 감시가 닫는다 (ProfessorServer::HandleLogin 참고)
    std::atomic<bool>           loginRejected{ false };
    // 보내기가 실패한(타임아웃 포함) 세션. 여기서 바로 닫지 않고 하트비트 감시에 맡긴다 —
    // Send 는 sessionsMutex_ 를 쥔 SendToSession 안에서 불릴 수 있어, 그 자리에서 닫으면
    // 세션을 지우려고 같은 잠금을 다시 잡다 멈춘다 (loginRejected 와 같은 방식).
    std::atomic<bool>           sendFailed{ false };
    // 받은 답안을 저장하는 중인 세션. 하트비트 감시가 건너뛴다.
    //
    // 저장(복사 + 7za 해제)은 이 세션의 수신 스레드에서 그대로 돌고, 하트비트 시각은
    // 그 수신 루프 안에서만 갱신된다. 그래서 저장이 15초를 넘기면 학생은 멀쩡히 보내고 있는데도
    // 감시 스레드가 '응답 없음'으로 보고 세션을 닫아 버린다 — 확인 회신을 보내기 전에 끊기므로
    // 교수 PC 에는 답안이 저장됐는데 학생 화면에는 '제출 실패'가 뜬다. 학생은 다시 내고 부하는 더 는다.
    std::atomic<bool>           busy{ false };
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
