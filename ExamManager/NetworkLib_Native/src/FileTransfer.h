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

private:
    std::mutex contextsMutex_;
    std::map<std::string, FileReceiveContext> contexts_;  // transferId → context

    void AbortTransfer(const std::string& transferId, const std::string& reason);
};
