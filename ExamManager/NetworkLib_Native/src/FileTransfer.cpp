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
// C# 은 경로를 UTF-8 로 넘긴다(LPUTF8Str). 그런데 CreateFileA 는 받은 바이트를
// 시스템 ANSI 코드페이지(한국어 Windows 는 CP949)로 해석하므로, 경로에 한글이
// 들어가면 엉뚱한 이름을 찾다가 실패한다.
// 경로가 전부 ASCII 일 때만 우연히 맞아떨어졌던 것이라, 파일을 열 때는 항상
// UTF-8 을 UTF-16 으로 바꿔 W 계열 API 를 쓴다.
static std::wstring Utf8ToWide(const std::string& utf8)
{
    if (utf8.empty()) return std::wstring();

    int len = MultiByteToWideChar(CP_UTF8, 0, utf8.c_str(), (int)utf8.size(), nullptr, 0);
    if (len <= 0) return std::wstring();

    std::wstring wide(len, L'\0');
    MultiByteToWideChar(CP_UTF8, 0, utf8.c_str(), (int)utf8.size(), &wide[0], len);
    return wide;
}

// 반대 방향. 시스템에서 받은 경로를 UTF-8 문자열로 통일해 두기 위함이다.
static std::string WideToUtf8(const std::wstring& wide)
{
    if (wide.empty()) return std::string();

    int len = WideCharToMultiByte(CP_UTF8, 0, wide.c_str(), (int)wide.size(), nullptr, 0, nullptr, nullptr);
    if (len <= 0) return std::string();

    std::string utf8(len, '\0');
    WideCharToMultiByte(CP_UTF8, 0, wide.c_str(), (int)wide.size(), &utf8[0], len, nullptr, nullptr);
    return utf8;
}

// 경로를 UTF-8 로 받아 여는 CreateFile. 아래 파일 접근은 모두 이것을 쓴다.
static HANDLE OpenFileUtf8(const std::string& path, DWORD access, DWORD share,
                           DWORD creation, DWORD flags)
{
    return CreateFileW(Utf8ToWide(path).c_str(), access, share, nullptr, creation, flags, nullptr);
}

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
    HANDLE hFile = OpenFileUtf8(filePath, GENERIC_READ, FILE_SHARE_READ,
                               OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL);
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
    HANDLE hFile = OpenFileUtf8(filePath, GENERIC_READ, FILE_SHARE_READ,
                               OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL);
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
    hFile = OpenFileUtf8(filePath, GENERIC_READ, FILE_SHARE_READ,
                        OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL);
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
    // 사용자 이름에 한글이 들어가면 ANSI 로 받은 경로가 UTF-8 문자열과 섞여 깨진다.
    wchar_t tempDirW[MAX_PATH];
    GetTempPathW(MAX_PATH, tempDirW);
    std::string tempDir = WideToUtf8(tempDirW);

    std::string tempPath = tempDir
        + "NL_" + SanitizeFileName(transferId) + "_" + SanitizeFileName(fileName);

    auto ctx = std::make_shared<FileReceiveContext>();
    ctx->transferId      = transferId;
    ctx->senderId        = senderId;
    ctx->fileName        = fileName;
    ctx->tempPath        = tempPath;
    ctx->sha256Hash      = FixedToString(p->sha256Hash,      sizeof(p->sha256Hash));
    ctx->archivePassword = FixedToString(p->archivePassword, sizeof(p->archivePassword));
    ctx->totalSize       = p->totalSize;
    ctx->totalChunks     = p->totalChunks;
    ctx->receivedChunks  = 0;

    // 임시 파일 생성
    ctx->hFile = OpenFileUtf8(tempPath, GENERIC_WRITE, 0,
                             CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL);
    if (ctx->hFile == INVALID_HANDLE_VALUE)
    {
        if (onFileError) onFileError(transferId, "임시 파일 생성 실패: " + tempPath);
        return;
    }

    // 같은 번호의 기록이 이미 있으면 그 핸들이 닫히지 않은 채 버려진다. 먼저 치운다.
    // 번호는 GUID 라 실제로 겹칠 일은 없지만, 겹쳤을 때 핸들이 새는 쪽으로 두지 않는다.
    std::shared_ptr<FileReceiveContext> replaced;
    {
        std::lock_guard<std::mutex> lock(contextsMutex_);
        auto it = contexts_.find(transferId);
        if (it != contexts_.end()) replaced = std::move(it->second);
        contexts_[transferId] = std::move(ctx);
    }

    if (replaced)
    {
        std::lock_guard<std::mutex> lock(replaced->writeMutex);
        if (replaced->hFile != INVALID_HANDLE_VALUE) CloseHandle(replaced->hFile);
        replaced->hFile = INVALID_HANDLE_VALUE;
    }
}

void FileTransferReceiver::HandleChunk(const uint8_t* payload, uint32_t payloadLen)
{
    if (payloadLen < sizeof(NlFileChunkHeader)) return;
    const auto* hdr  = reinterpret_cast<const NlFileChunkHeader*>(payload);
    const uint8_t* data = payload + sizeof(NlFileChunkHeader);
    uint32_t       dataSize = hdr->dataSize;

    if (payloadLen < sizeof(NlFileChunkHeader) + dataSize) return;

    std::string transferId = FixedToString(hdr->transferId, sizeof(hdr->transferId));

    // 찾을 때만 공용 자물쇠를 잡고 바로 놓는다.
    //
    // 예전에는 이 자물쇠를 쥔 채로 디스크 쓰기와 진행률 콜백(C# 으로 넘어간다)까지 했다.
    // 그러면 받는 학생이 몇 명이든 조각 쓰기가 한 줄로 선다 — 30명이 100MB 씩 내면
    // 48,000번의 쓰기가 차례를 기다린다. 그렇게 느려지면 학생 쪽 60초 정지 판정과
    // 10초 보내기 제한을 동시에 끌어당겨, 멀쩡한 전송이 '끊겼습니다'로 끝난다.
    std::shared_ptr<FileReceiveContext> ctx;
    {
        std::lock_guard<std::mutex> lock(contextsMutex_);
        auto it = contexts_.find(transferId);
        if (it == contexts_.end()) return;
        ctx = it->second;
    }

    uint32_t done    = 0;
    bool     written = false;
    {
        // 쓰는 동안은 이 전송의 자물쇠만 잡는다. 다른 학생은 기다리지 않는다.
        std::lock_guard<std::mutex> lock(ctx->writeMutex);
        if (ctx->hFile == INVALID_HANDLE_VALUE) return;   // 그사이 연결이 끊겨 치워졌다

        DWORD wrote = 0;
        written = WriteFile(ctx->hFile, data, dataSize, &wrote, nullptr) != FALSE;
        if (written) done = ++ctx->receivedChunks;
    }

    // 치우는 일과 콜백은 모두 자물쇠 밖에서 한다.
    // AbortTransfer 는 두 자물쇠를 다시 잡고, 콜백은 C# 으로 넘어가 얼마나 걸릴지 알 수 없다.
    if (!written)
    {
        AbortTransfer(transferId, "청크 파일 쓰기 실패");
        return;
    }

    if (onFileProgress && ctx->totalChunks > 0)
    {
        int pct = static_cast<int>(done * 100ULL / ctx->totalChunks);
        onFileProgress(ctx->transferId, ctx->fileName, pct < 99 ? pct : 99);
    }
}

void FileTransferReceiver::HandleComplete(const uint8_t* payload, uint32_t payloadLen)
{
    if (payloadLen < sizeof(FileTransferCompletePayload)) return;
    const auto* p = reinterpret_cast<const FileTransferCompletePayload*>(payload);

    // 받던 기록만 잠금 안에서 꺼내고 바로 놓는다.
    //
    // 아래의 SHA-256 검사와 수신 완료 콜백은 가볍지 않다 — 교수 앱은 그 콜백 안에서
    // 답안 묶음을 복사하고 7za 로 풀어 본 뒤에야 돌아온다(AnswerCollectService).
    // 잠금을 쥔 채로 하면 30명이 동시에 답안을 낼 때 한 명씩만 처리되고, 그동안 다른 학생들의
    // 청크 쓰기(HandleChunk)까지 같은 잠금에서 멈춘다. 전송이 멈춘 것으로 보여
    // 학생 쪽이 '전송이 끊겼습니다'로 판정할 수 있다(진행률 60초 정지 기준).
    //
    // 기록을 꺼낸 뒤에는 이 전송을 아는 것이 이 함수뿐이라 잠금 없이 다뤄도 안전하다.
    std::shared_ptr<FileReceiveContext> ctx;
    {
        std::lock_guard<std::mutex> lock(contextsMutex_);
        auto it = contexts_.find(FixedToString(p->transferId, sizeof(p->transferId)));
        if (it == contexts_.end()) return;

        ctx = std::move(it->second);
        contexts_.erase(it);
    }

    // 파일 핸들 닫기. 끊김 처리(AbortBySender)가 같은 핸들을 닫으러 올 수 있어 자물쇠 안에서 한다.
    {
        std::lock_guard<std::mutex> lock(ctx->writeMutex);
        if (ctx->hFile == INVALID_HANDLE_VALUE) return;   // 이미 치워진 전송
        CloseHandle(ctx->hFile);
        ctx->hFile = INVALID_HANDLE_VALUE;
    }

    // SHA-256 검증
    std::string computedHash = ComputeSHA256(ctx->tempPath);
    if (_stricmp(computedHash.c_str(), ctx->sha256Hash.c_str()) != 0)
    {
        DeleteFileW(Utf8ToWide(ctx->tempPath).c_str());
        if (onFileError) onFileError(ctx->transferId, "SHA-256 불일치 — 파일 손상");
        return;
    }

    // 수신 완료 이벤트
    if (onFileProgress) onFileProgress(ctx->transferId, ctx->fileName, 100);

    LARGE_INTEGER fileSize{};
    HANDLE hTmp = OpenFileUtf8(ctx->tempPath, GENERIC_READ, FILE_SHARE_READ,
                              OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL);
    if (hTmp != INVALID_HANDLE_VALUE) { GetFileSizeEx(hTmp, &fileSize); CloseHandle(hTmp); }

    if (onFileReceived)
        onFileReceived(ctx->transferId, ctx->senderId, ctx->fileName,
                       ctx->tempPath, fileSize.QuadPart, ctx->archivePassword);
}

void FileTransferReceiver::AbortTransfer(const std::string& transferId, const std::string& reason)
{
    std::shared_ptr<FileReceiveContext> ctx;
    {
        std::lock_guard<std::mutex> lock(contextsMutex_);
        auto it = contexts_.find(transferId);
        if (it == contexts_.end()) return;

        ctx = std::move(it->second);
        contexts_.erase(it);
    }

    {
        std::lock_guard<std::mutex> lock(ctx->writeMutex);
        if (ctx->hFile != INVALID_HANDLE_VALUE)
        {
            CloseHandle(ctx->hFile);
            ctx->hFile = INVALID_HANDLE_VALUE;
        }
    }
    DeleteFileW(Utf8ToWide(ctx->tempPath).c_str());

    if (onFileError) onFileError(transferId, reason);
}

// 연결이 끊긴 쪽이 받던 중이던 전송을 모두 치운다.
//
// 전에는 끊겨도 받던 기록이 그대로 남아, 열린 파일 핸들과 반쯤 받은 임시 파일이 쌓였다.
// 재제출은 새 번호로 돌아 동작에는 지장이 없었지만, 끊김이 반복되면 계속 늘어나기만 했다.
//
// 알림(onFileError)은 보내지 않는다. 이 전송의 주인은 방금 끊긴 연결이라 전할 곳이 없고,
// 교수 화면에는 '연결 끊김'이 이미 따로 뜬다.
void FileTransferReceiver::AbortBySender(const std::string& senderId)
{
    std::vector<std::shared_ptr<FileReceiveContext>> dropped;
    {
        std::lock_guard<std::mutex> lock(contextsMutex_);
        for (auto it = contexts_.begin(); it != contexts_.end(); )
        {
            if (it->second->senderId == senderId)
            {
                dropped.push_back(std::move(it->second));
                it = contexts_.erase(it);
            }
            else ++it;
        }
    }

    // 핸들을 닫는 일은 공용 자물쇠 밖에서 한다 — 다른 학생의 조각 쓰기를 세우지 않기 위해서다.
    for (auto& ctx : dropped)
    {
        {
            std::lock_guard<std::mutex> lock(ctx->writeMutex);
            if (ctx->hFile != INVALID_HANDLE_VALUE)
            {
                CloseHandle(ctx->hFile);
                ctx->hFile = INVALID_HANDLE_VALUE;
            }
        }
        DeleteFileW(Utf8ToWide(ctx->tempPath).c_str());
    }
}
