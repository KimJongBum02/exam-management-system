#pragma once
#include "pch.h"
#include "Protocol.h"
#include "ClientSession.h"
#include "FileTransfer.h"

// ══════════════════════════════════════════════════════════════════════
//  ProfessorServer — 교수 PC에서 실행되는 TCP 서버
//  최대 40명의 학생 클라이언트 연결을 관리합니다.
// ══════════════════════════════════════════════════════════════════════
class ProfessorServer
{
public:
    // ── 이벤트 콜백 ───────────────────────────────────────────────
    std::function<void(const char*, const char*, const char*, const char*)>            onStudentConnected;
    std::function<void(const char*, const char*, const char*, int)>                    onStudentDisconnected;
    std::function<void(const char*, const char*, const char*, uint32_t, const uint8_t*, uint32_t)> onPacketReceived;

    // 파일 수신 콜백
    FileReceivedCb onFileReceived;
    FileProgressCb onFileProgress;
    FileErrorCb    onFileError;

    // ─────────────────────────────────────────────────────────────
    explicit ProfessorServer(int port);
    ~ProfessorServer();

    bool Start();
    void Stop();
    int  GetConnectedCount() const;

    // 패킷 브로드캐스트 / 개별 전송
    int Broadcast        (PacketType type, const void* payload, uint32_t payloadLen);
    int SendToSession    (const std::string& sessionId, PacketType type, const void* payload, uint32_t payloadLen);

    // 파일 전송 (비동기 — 내부적으로 스레드 생성 후 즉시 반환)
    int BroadcastFile    (const std::string& filePath, const std::string& password);
    int SendFileToSession(const std::string& sessionId, const std::string& filePath, const std::string& password);

private:
    int                   port_;
    SOCKET                listenSock_{ INVALID_SOCKET };
    std::atomic<bool>     running_{ false };
    std::thread           acceptThread_;
    std::thread           heartbeatThread_;

    mutable std::mutex                                       sessionsMutex_;
    std::map<std::string, std::shared_ptr<ClientSession>>    sessions_;

    FileTransferReceiver fileReceiver_;  // 수신 측 공유 인스턴스

    void AcceptLoop();
    void HeartbeatLoop();

    // 세션에서 올라온 이벤트 핸들러
    void OnSessionPacket     (ClientSession* s, PacketType type, const uint8_t* payload, uint32_t len);
    void OnSessionDisconnected(ClientSession* s, int reason);

    // 로그인 패킷 처리
    void HandleLogin(ClientSession* s, const uint8_t* payload, uint32_t len);

    // 스냅샷 (브로드캐스트 시 락 최소화)
    std::vector<std::shared_ptr<ClientSession>> GetSessionSnapshot() const;

    // 간이 UUID 생성
    static std::string NewUUID();
};
