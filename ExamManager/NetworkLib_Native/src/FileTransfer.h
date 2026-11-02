#pragma once
#include "pch.h"
#include "Protocol.h"

// ══════════════════════════════════════════════════════════════════════
//  FileTransfer.h — 파일 청크 송수신
//
//  송신 흐름:
//    FileTransferStart → FileChunk × N → FileTransferComplete
//
//  수신 흐름:
//    HandleStart → HandleChunk(×N) → HandleComplete → 콜백
//
//  파일은 64KB 단위로 분할하여 raw 바이트로 전송합니다.
//  JSON/Base64 없음 — 33% 크기 절감, 파싱 오버헤드 없음.
// ══════════════════════════════════════════════════════════════════════

constexpr uint32_t FILE_CHUNK_SIZE = 64 * 1024; // 64KB

// ── 콜백 타입 ──────────────────────────────────────────────────────
using FileReceivedCb = std::function<void(
    const std::string& transferId,
    const std::string& senderId,
    const std::string& fileName,
    const std::string& tempPath,
    int64_t            fileSize,
    const std::string& archivePassword)>;

using FileProgressCb = std::function<void(
    const std::string& transferId,
    const std::string& fileName,
    int percent)>;

using FileErrorCb = std::function<void(
    const std::string& transferId,
    const std::string& message)>;

// ── Windows CNG로 SHA-256 계산 ─────────────────────────────────────
std::string ComputeSHA256(const std::string& filePath);

// ══════════════════════════════════════════════════════════════════════
//  FileTransferSender — 송신 측 (서버→학생, 학생→서버 양방향)
// ══════════════════════════════════════════════════════════════════════
class FileTransferSender
{
public:
    /// <summary>
    /// 파일을 읽어 청크 단위로 소켓에 전송합니다 (동기, 호출 스레드에서 실행).
    /// 브로드캐스트 시 각 세션마다 별도 스레드에서 호출하세요.
    /// </summary>
    static bool SendFile(
        SOCKET            sock,
        std::mutex&       sendMtx,
        const std::string& filePath,
        const std::string& archivePassword,
        FileProgressCb     progressCb = nullptr,
        FileErrorCb        errorCb    = nullptr);
};

// ══════════════════════════════════════════════════════════════════════
//  FileTransferReceiver — 수신 측 (수신 루프에서 호출)
// ══════════════════════════════════════════════════════════════════════

// 진행 중인 파일 수신 컨텍스트 (내부 사용)
struct FileReceiveContext
{
    std::string transferId;
    std::string senderId;
    std::string fileName;
    std::string tempPath;
    std::string sha256Hash;
    std::string archivePassword;
    int64_t     totalSize{ 0 };
    uint32_t    totalChunks{ 0 };
    uint32_t    receivedChunks{ 0 };
    HANDLE      hFile{ INVALID_HANDLE_VALUE };

    // 이 전송 하나의 파일 쓰기만 묶는다.
    //
    // 평소에는 한 전송을 한 수신 스레드만 다루므로 이 자물쇠를 기다리는 일이 없다.
    // 그래도 두는 이유는 AbortBySender 때문이다 — 그쪽은 하트비트 감시 스레드에서 불려 와
    // 이 핸들을 닫는데, 바로 그때 수신 스레드가 같은 핸들에 쓰고 있을 수 있다.
    std::mutex  writeMutex;
};

class FileTransferReceiver
{
public:
    FileReceivedCb onFileReceived;
    FileProgressCb onFileProgress;
    FileErrorCb    onFileError;

    // 수신 루프에서 파일 관련 패킷을 처리합니다
    void HandleStart   (const std::string& senderId, const uint8_t* payload, uint32_t payloadLen);
    void HandleChunk   (const uint8_t* payload, uint32_t payloadLen);
    void HandleComplete(const uint8_t* payload, uint32_t payloadLen);

    // 연결이 끊긴 쪽이 받던 중이던 전송을 모두 치웁니다 (연결을 닫을 때 호출).
    // 치우지 않으면 열린 파일 핸들과 반쯤 받은 임시 파일이 그대로 남습니다.
    void AbortBySender(const std::string& senderId);

private:
    std::mutex contextsMutex_;
    // shared_ptr 로 담는 이유: 찾을 때만 이 자물쇠를 잡고, 파일 쓰기는 자물쇠를 놓은 뒤에 한다.
    // 값으로 담으면 쓰는 동안 자물쇠를 쥐고 있어야 해서 학생 전원의 조각 쓰기가 한 줄로 선다.
    std::map<std::string, std::shared_ptr<FileReceiveContext>> contexts_;  // transferId → context

    // 받던 기록을 지우고 임시 파일을 버린다. 이 함수가 스스로 contextsMutex_ 를 잡는다.
    void AbortTransfer(const std::string& transferId, const std::string& reason);
};
