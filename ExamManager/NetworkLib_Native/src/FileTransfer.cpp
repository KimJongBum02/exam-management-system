#include "pch.h"
#include "FileTransfer.h"
#include "PacketIO.h"

// ══════════════════════════════════════════════════════════════════════
//  와이어에서 온 데이터를 다루는 헬퍼
//
//  패킷의 문자 배열은 고정 길이일 뿐 종료 문자가 보장되지 않습니다.
//  그대로 std::string 으로 만들면 수신 버퍼 밖까지 읽어 프로세스가 죽습니다.
// ══════════════════════════════════════════════════════════════════════

// 고정 길이 문자 배열을 배열 밖으로 나가지 않고 문자열로 만든다
static std::string FixedToString(const char* field, size_t maxLen)
{
    size_t len = 0;
    while (len < maxLen && field[len] != '\0') len++;
    return std::string(field, len);
}

// 원격이 보낸 이름에서 경로 요소를 제거해 파일 이름만 남긴다.
// ("..\..\" 같은 값이 섞여 있으면 임시 폴더 밖에 파일을 쓸 수 있다)
static std::string SanitizeFileName(const std::string& name)
{
    size_t pos = name.find_last_of("\\/:");
    std::string base = (pos == std::string::npos) ? name : name.substr(pos + 1);

    // 파일 이름에 쓸 수 없는 문자는 밑줄로 바꾼다
    for (char& c : base)
        if (c == '<' || c == '>' || c == '"' || c == '|' || c == '?' || c == '*' ||
            static_cast<unsigned char>(c) < 0x20)
            c = '_';

    // 이름이 통째로 사라졌거나 점만 남은 경우의 대비책
    if (base.empty() || base.find_first_not_of('.') == std::string::npos)
        base = "received";

    return base;
}

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
    {
        if (errorCb) errorCb(transferId, "전송 시작 패킷을 보내지 못했습니다");
        return false;
    }

    // ── 2단계: 파일을 청크 단위로 전송 ───────────────────────────
    hFile = CreateFileA(filePath.c_str(), GENERIC_READ, FILE_SHARE_READ,
                        nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (hFile == INVALID_HANDLE_VALUE)
    {
        if (errorCb) errorCb(transferId, "파일을 다시 열 수 없습니다: " + filePath);
        return false;
    }

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
            if (errorCb) errorCb(transferId, "전송이 중단되었습니다 (연결 끊김)");
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
    {
        if (errorCb) errorCb(transferId, "전송 완료 패킷을 보내지 못했습니다");
        return false;
    }

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

    // 와이어에서 온 문자열은 길이를 제한해 읽는다
    std::string transferId = FixedToString(p->transferId, sizeof(p->transferId));
    std::string fileName   = FixedToString(p->fileName,   sizeof(p->fileName));

    // 임시 파일 경로 생성 — 경로에 들어가는 값은 파일 이름만 남기고 정리한다
    char tempDir[MAX_PATH];
    GetTempPathA(MAX_PATH, tempDir);
    std::string tempPath = std::string(tempDir)
        + "NL_" + SanitizeFileName(transferId) + "_" + SanitizeFileName(fileName);

    FileReceiveContext ctx;
    ctx.transferId      = transferId;
    ctx.senderId        = senderId;
    ctx.fileName        = fileName;
    ctx.tempPath        = tempPath;
    ctx.sha256Hash      = FixedToString(p->sha256Hash,      sizeof(p->sha256Hash));
    ctx.archivePassword = FixedToString(p->archivePassword, sizeof(p->archivePassword));
    ctx.totalSize       = p->totalSize;
    ctx.totalChunks     = p->totalChunks;
    ctx.receivedChunks  = 0;

    // 임시 파일 생성
    ctx.hFile = CreateFileA(tempPath.c_str(), GENERIC_WRITE, 0, nullptr,
                            CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (ctx.hFile == INVALID_HANDLE_VALUE)
    {
        if (onFileError) onFileError(transferId, "임시 파일 생성 실패: " + tempPath);
        return;
    }

    std::lock_guard<std::mutex> lock(contextsMutex_);
    contexts_[transferId] = std::move(ctx);
}

void FileTransferReceiver::HandleChunk(const uint8_t* payload, uint32_t payloadLen)
{
    if (payloadLen < sizeof(NlFileChunkHeader)) return;
    const auto* hdr  = reinterpret_cast<const NlFileChunkHeader*>(payload);
    const uint8_t* data = payload + sizeof(NlFileChunkHeader);
    uint32_t       dataSize = hdr->dataSize;

    if (payloadLen < sizeof(NlFileChunkHeader) + dataSize) return;

    std::string transferId = FixedToString(hdr->transferId, sizeof(hdr->transferId));

    std::lock_guard<std::mutex> lock(contextsMutex_);
    auto it = contexts_.find(transferId);
    if (it == contexts_.end()) return;

    FileReceiveContext& ctx = it->second;
    DWORD written = 0;
    if (!WriteFile(ctx.hFile, data, dataSize, &written, nullptr))
    {
        AbortTransfer(transferId, "청크 파일 쓰기 실패");
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
    auto it = contexts_.find(FixedToString(p->transferId, sizeof(p->transferId)));
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
