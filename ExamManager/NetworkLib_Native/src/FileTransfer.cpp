#include "pch.h"
#include "FileTransfer.h"
#include "PacketIO.h"

// ══════════════════════════════════════════════════════════════════════
//  SHA-256 계산 (Windows CNG — BCrypt API)
// ══════════════════════════════════════════════════════════════════════
std::string ComputeSHA256(const std::string& filePath)
{
    // 파일 열기
    HANDLE hFile = CreateFileA(filePath.c_str(), GENERIC_READ, FILE_SHARE_READ,
                               nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (hFile == INVALID_HANDLE_VALUE) return "";

    BCRYPT_ALG_HANDLE  hAlg  = nullptr;
    BCRYPT_HASH_HANDLE hHash = nullptr;
    std::string result;

    do {
        // SHA-256 알고리즘 프로바이더 열기
        if (!BCRYPT_SUCCESS(BCryptOpenAlgorithmProvider(&hAlg, BCRYPT_SHA256_ALGORITHM, nullptr, 0)))
            break;

        // 해시 오브젝트 크기 조회
        DWORD hashObjSize = 0, dummy = 0;
        BCryptGetProperty(hAlg, BCRYPT_OBJECT_LENGTH,
                          reinterpret_cast<PUCHAR>(&hashObjSize), sizeof(DWORD), &dummy, 0);

        DWORD hashSize = 0;
        BCryptGetProperty(hAlg, BCRYPT_HASH_LENGTH,
                          reinterpret_cast<PUCHAR>(&hashSize), sizeof(DWORD), &dummy, 0);

        std::vector<BYTE> hashObj(hashObjSize);
        std::vector<BYTE> hashBuf(hashSize);

        // 해시 핸들 생성
        if (!BCRYPT_SUCCESS(BCryptCreateHash(hAlg, &hHash,
                                             hashObj.data(), hashObjSize,
                                             nullptr, 0, 0)))
            break;

        // 파일을 64KB 단위로 읽어 해시에 공급
        std::vector<BYTE> readBuf(FILE_CHUNK_SIZE);
        DWORD bytesRead = 0;
        while (ReadFile(hFile, readBuf.data(), FILE_CHUNK_SIZE, &bytesRead, nullptr) && bytesRead > 0)
            BCryptHashData(hHash, readBuf.data(), bytesRead, 0);

        BCryptFinishHash(hHash, hashBuf.data(), hashSize, 0);

        // hex 문자열로 변환
        std::ostringstream oss;
        for (BYTE b : hashBuf)
            oss << std::hex << std::setw(2) << std::setfill('0') << static_cast<int>(b);
        result = oss.str();

    } while (false);

    if (hHash) BCryptDestroyHash(hHash);
    if (hAlg)  BCryptCloseAlgorithmProvider(hAlg, 0);
    CloseHandle(hFile);
    return result;
}

// ══════════════════════════════════════════════════════════════════════
//  FileTransferSender::SendFile
// ══════════════════════════════════════════════════════════════════════
bool FileTransferSender::SendFile(
    SOCKET             sock,
    std::mutex&        sendMtx,
    const std::string& filePath,
    const std::string& archivePassword,
    FileProgressCb     progressCb,
    FileErrorCb        errorCb)
{
    // ── 파일 열기 ────────────────────────────────────────────────
    HANDLE hFile = CreateFileA(filePath.c_str(), GENERIC_READ, FILE_SHARE_READ,
                               nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (hFile == INVALID_HANDLE_VALUE)
    {
        if (errorCb) errorCb("", "파일을 열 수 없습니다: " + filePath);
        return false;
    }

    // ── 파일 크기 조회 ───────────────────────────────────────────
    LARGE_INTEGER fileSize{};
    GetFileSizeEx(hFile, &fileSize);
    int64_t totalSize = fileSize.QuadPart;

    uint32_t totalChunks = static_cast<uint32_t>(
        (totalSize + FILE_CHUNK_SIZE - 1) / FILE_CHUNK_SIZE);
    if (totalChunks == 0) totalChunks = 1;

    // ── 파일 이름 추출 ───────────────────────────────────────────
    std::string fileName = filePath;
    auto pos = filePath.find_last_of("\\/");
    if (pos != std::string::npos) fileName = filePath.substr(pos + 1);

    // ── SHA-256 계산 ─────────────────────────────────────────────
    CloseHandle(hFile); // SHA256 내부에서 다시 열므로 일단 닫기
    std::string sha256 = ComputeSHA256(filePath);
    if (sha256.empty())
    {
        if (errorCb) errorCb("", "SHA-256 계산 실패: " + filePath);
        return false;
    }

    // ── Transfer ID 생성 (UUID 간이 구현) ────────────────────────
    char transferId[37];
    GUID guid;
    CoCreateGuid(&guid);
    snprintf(transferId, sizeof(transferId),
             "%08lx-%04x-%04x-%02x%02x-%02x%02x%02x%02x%02x%02x",
             guid.Data1, guid.Data2, guid.Data3,
             guid.Data4[0], guid.Data4[1], guid.Data4[2], guid.Data4[3],
             guid.Data4[4], guid.Data4[5], guid.Data4[6], guid.Data4[7]);

    // ── 1단계: FileTransferStart 전송 ────────────────────────────
    FileTransferStartPayload startPayload{};
    strncpy_s(startPayload.transferId, transferId, _TRUNCATE);
    strncpy_s(startPayload.fileName,   fileName.c_str(), _TRUNCATE);
    startPayload.totalSize   = totalSize;
    startPayload.totalChunks = totalChunks;
    strncpy_s(startPayload.sha256Hash, sha256.c_str(), _TRUNCATE);
    strncpy_s(startPayload.archivePassword, archivePassword.c_str(), _TRUNCATE);

    if (!SendPacket(sock, sendMtx, PacketType::FileTransferStart,
                    &startPayload, sizeof(startPayload)))
        return false;

    // ── 2단계: 파일을 청크 단위로 전송 ───────────────────────────
    hFile = CreateFileA(filePath.c_str(), GENERIC_READ, FILE_SHARE_READ,
                        nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (hFile == INVALID_HANDLE_VALUE) return false;

    std::vector<uint8_t> chunkBuf(sizeof(NlFileChunkHeader) + FILE_CHUNK_SIZE);
    uint32_t chunkIndex = 0;
    DWORD    bytesRead  = 0;

    while (ReadFile(hFile, chunkBuf.data() + sizeof(NlFileChunkHeader),
                    FILE_CHUNK_SIZE, &bytesRead, nullptr) && bytesRead > 0)
    {
        // NlFileChunkHeader를 버퍼 앞에 씁니다
        auto* chunkHdr = reinterpret_cast<NlFileChunkHeader*>(chunkBuf.data());
        strncpy_s(chunkHdr->transferId, transferId, _TRUNCATE);
        chunkHdr->chunkIndex = chunkIndex;
        chunkHdr->dataSize   = bytesRead;

        uint32_t totalPayloadLen = sizeof(NlFileChunkHeader) + bytesRead;

        if (!SendPacket(sock, sendMtx, PacketType::FileChunk,
                        chunkBuf.data(), totalPayloadLen))
        {
            CloseHandle(hFile);
            return false;
        }

        chunkIndex++;
        if (progressCb)
        {
            int pct = static_cast<int>(chunkIndex * 100ULL / totalChunks);
            progressCb(transferId, fileName, pct < 99 ? pct : 99);
        }
    }
    CloseHandle(hFile);

    // ── 3단계: FileTransferComplete 전송 ─────────────────────────
    FileTransferCompletePayload completePayload{};
    strncpy_s(completePayload.transferId, transferId, _TRUNCATE);
    strncpy_s(completePayload.fileName,   fileName.c_str(), _TRUNCATE);

    if (!SendPacket(sock, sendMtx, PacketType::FileTransferComplete,
                    &completePayload, sizeof(completePayload)))
        return false;

    if (progressCb) progressCb(transferId, fileName, 100);
    return true;
}

// ══════════════════════════════════════════════════════════════════════
//  FileTransferReceiver
// ══════════════════════════════════════════════════════════════════════

void FileTransferReceiver::HandleStart(
    const std::string& senderId,
    const uint8_t*     payload,
    uint32_t           payloadLen)
{
    if (payloadLen < sizeof(FileTransferStartPayload)) return;
    const auto* p = reinterpret_cast<const FileTransferStartPayload*>(payload);

    // 임시 파일 경로 생성
    char tempDir[MAX_PATH];
    GetTempPathA(MAX_PATH, tempDir);
    std::string tempPath = std::string(tempDir) + "NL_" + p->transferId + "_" + p->fileName;

    FileReceiveContext ctx;
    ctx.transferId      = p->transferId;
    ctx.senderId        = senderId;
    ctx.fileName        = p->fileName;
    ctx.tempPath        = tempPath;
    ctx.sha256Hash      = p->sha256Hash;
    ctx.archivePassword = p->archivePassword;
    ctx.totalSize       = p->totalSize;
    ctx.totalChunks     = p->totalChunks;
    ctx.receivedChunks  = 0;

    // 임시 파일 생성
    ctx.hFile = CreateFileA(tempPath.c_str(), GENERIC_WRITE, 0, nullptr,
                            CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (ctx.hFile == INVALID_HANDLE_VALUE)
    {
        if (onFileError) onFileError(p->transferId, "임시 파일 생성 실패: " + tempPath);
        return;
    }

    std::lock_guard<std::mutex> lock(contextsMutex_);
    contexts_[p->transferId] = std::move(ctx);
}

void FileTransferReceiver::HandleChunk(const uint8_t* payload, uint32_t payloadLen)
{
    if (payloadLen < sizeof(NlFileChunkHeader)) return;
    const auto* hdr  = reinterpret_cast<const NlFileChunkHeader*>(payload);
    const uint8_t* data = payload + sizeof(NlFileChunkHeader);
    uint32_t       dataSize = hdr->dataSize;

    if (payloadLen < sizeof(NlFileChunkHeader) + dataSize) return;

    std::lock_guard<std::mutex> lock(contextsMutex_);
    auto it = contexts_.find(hdr->transferId);
    if (it == contexts_.end()) return;

    FileReceiveContext& ctx = it->second;
    DWORD written = 0;
    if (!WriteFile(ctx.hFile, data, dataSize, &written, nullptr))
    {
        AbortTransfer(hdr->transferId, "청크 파일 쓰기 실패");
        return;
    }

    ctx.receivedChunks++;
    if (onFileProgress && ctx.totalChunks > 0)
    {
        int pct = static_cast<int>(ctx.receivedChunks * 100ULL / ctx.totalChunks);
        onFileProgress(ctx.transferId, ctx.fileName, pct < 99 ? pct : 99);
    }
}

void FileTransferReceiver::HandleComplete(const uint8_t* payload, uint32_t payloadLen)
{
    if (payloadLen < sizeof(FileTransferCompletePayload)) return;
    const auto* p = reinterpret_cast<const FileTransferCompletePayload*>(payload);

    std::lock_guard<std::mutex> lock(contextsMutex_);
    auto it = contexts_.find(p->transferId);
    if (it == contexts_.end()) return;

    FileReceiveContext ctx = std::move(it->second);
    contexts_.erase(it);

    // 파일 핸들 닫기
    CloseHandle(ctx.hFile);
    ctx.hFile = INVALID_HANDLE_VALUE;

    // SHA-256 검증
    std::string computedHash = ComputeSHA256(ctx.tempPath);
    if (_stricmp(computedHash.c_str(), ctx.sha256Hash.c_str()) != 0)
    {
        DeleteFileA(ctx.tempPath.c_str());
        if (onFileError) onFileError(ctx.transferId, "SHA-256 불일치 — 파일 손상");
        return;
    }

    // 수신 완료 이벤트
    if (onFileProgress) onFileProgress(ctx.transferId, ctx.fileName, 100);

    LARGE_INTEGER fileSize{};
    HANDLE hTmp = CreateFileA(ctx.tempPath.c_str(), GENERIC_READ, FILE_SHARE_READ,
                              nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (hTmp != INVALID_HANDLE_VALUE) { GetFileSizeEx(hTmp, &fileSize); CloseHandle(hTmp); }

    if (onFileReceived)
        onFileReceived(ctx.transferId, ctx.senderId, ctx.fileName,
                       ctx.tempPath, fileSize.QuadPart, ctx.archivePassword);
}

void FileTransferReceiver::AbortTransfer(const std::string& transferId, const std::string& reason)
{
    // 반드시 contextsMutex_ 를 보유한 상태에서 호출해야 합니다
    auto it = contexts_.find(transferId);
    if (it == contexts_.end()) return;

    FileReceiveContext& ctx = it->second;
    if (ctx.hFile != INVALID_HANDLE_VALUE)
    {
        CloseHandle(ctx.hFile);
        ctx.hFile = INVALID_HANDLE_VALUE;
    }
    DeleteFileA(ctx.tempPath.c_str());
    contexts_.erase(it);

    if (onFileError) onFileError(transferId, reason);
}
