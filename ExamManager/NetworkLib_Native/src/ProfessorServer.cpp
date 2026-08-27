#include "pch.h"
#include "ProfessorServer.h"
#include "PacketIO.h"

// ─── 생성자 ────────────────────────────────────────────────────────
ProfessorServer::ProfessorServer(int port) : port_(port)
{
    // FileTransferReceiver 콜백 연결
    fileReceiver_.onFileReceived = [this](const std::string& tid, const std::string& sid,
        const std::string& fn, const std::string& tp,
        int64_t sz, const std::string& pw)
        {
            if (onFileReceived) onFileReceived(tid.c_str(), sid.c_str(), fn.c_str(), tp.c_str(), sz, pw.c_str());
        };
    fileReceiver_.onFileProgress = [this](const std::string& tid, const std::string& fn, int pct)
        {
            if (onFileProgress) onFileProgress(tid.c_str(), fn.c_str(), pct);
        };
    fileReceiver_.onFileError = [this](const std::string& tid, const std::string& msg)
        {
            if (onFileError) onFileError(tid.c_str(), msg.c_str());
        };
}

// ─── 소멸자 ────────────────────────────────────────────────────────
ProfessorServer::~ProfessorServer() { Stop(); }

// ─── 서버 시작 ─────────────────────────────────────────────────────
bool ProfessorServer::Start()
{
    // 리슨 소켓 생성
    listenSock_ = ::socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
    if (listenSock_ == INVALID_SOCKET) return false;

    // SO_REUSEADDR — 빠른 재시작 시 포트 재사용
    int opt = 1;
    ::setsockopt(listenSock_, SOL_SOCKET, SO_REUSEADDR,
        reinterpret_cast<const char*>(&opt), sizeof(opt));

    sockaddr_in addr{};
    addr.sin_family = AF_INET;
    addr.sin_addr.s_addr = INADDR_ANY;
    addr.sin_port = htons(static_cast<u_short>(port_));

    if (::bind(listenSock_, reinterpret_cast<sockaddr*>(&addr), sizeof(addr)) == SOCKET_ERROR)
    {
        ::closesocket(listenSock_);
        return false;
    }
    if (::listen(listenSock_, SOMAXCONN) == SOCKET_ERROR)
    {
        ::closesocket(listenSock_);
        return false;
    }

    running_ = true;
    acceptThread_ = std::thread(&ProfessorServer::AcceptLoop, this);
    heartbeatThread_ = std::thread(&ProfessorServer::HeartbeatLoop, this);
    return true;
}

// ─── 서버 중지 ─────────────────────────────────────────────────────
void ProfessorServer::Stop()
{
    running_ = false;

    // 리슨 소켓 닫기 → AcceptLoop의 accept() 차단 해제
    if (listenSock_ != INVALID_SOCKET)
    {
        ::closesocket(listenSock_);
        listenSock_ = INVALID_SOCKET;
    }

    if (acceptThread_.joinable())    acceptThread_.join();
    if (heartbeatThread_.joinable()) heartbeatThread_.join();

    // 모든 세션 종료
    std::vector<std::shared_ptr<ClientSession>> snapshot = GetSessionSnapshot();
    for (auto& s : snapshot)
        s->Close(static_cast<int>(DisconnectReason::ServerShutdown));

    std::lock_guard<std::mutex> lock(sessionsMutex_);
    sessions_.clear();
}

// ─── Accept 루프 ───────────────────────────────────────────────────
void ProfessorServer::AcceptLoop()
{
    while (running_)
    {
        sockaddr_in clientAddr{};
        int addrLen = sizeof(clientAddr);

        SOCKET clientSock = ::accept(listenSock_,
            reinterpret_cast<sockaddr*>(&clientAddr), &addrLen);
        if (clientSock == INVALID_SOCKET) break; // 서버 중지 시 탈출

        // TCP_NODELAY — Nagle 알고리즘 비활성화 (응답성 향상)
        int nodelay = 1;
        ::setsockopt(clientSock, IPPROTO_TCP, TCP_NODELAY,
            reinterpret_cast<const char*>(&nodelay), sizeof(nodelay));

        // 클라이언트 IP:포트 문자열
        char ipBuf[INET_ADDRSTRLEN];
        inet_ntop(AF_INET, &clientAddr.sin_addr, ipBuf, sizeof(ipBuf));
        std::string remoteAddr = std::string(ipBuf) + ":" +
            std::to_string(ntohs(clientAddr.sin_port));

        // 세션 생성
        std::string sessionId = NewUUID();
        auto session = std::make_shared<ClientSession>(clientSock, remoteAddr);
        session->sessionId = sessionId;
        session->fileReceiver = &fileReceiver_;

        session->onPacket = [this](ClientSession* s, PacketType t,
            const uint8_t* p, uint32_t l)
            {
                OnSessionPacket(s, t, p, l);
            };
        session->onDisconnected = [this](ClientSession* s, int r)
            {
                OnSessionDisconnected(s, r);
            };

        {
            std::lock_guard<std::mutex> lock(sessionsMutex_);
            sessions_[sessionId] = session;
        }

        session->StartRecvLoop();
    }
}

// ─── Heartbeat 감시 루프 (5초마다 체크) ───────────────────────────
void ProfessorServer::HeartbeatLoop()
{
    while (running_)
    {
        std::this_thread::sleep_for(std::chrono::seconds(5));
        if (!running_) break;

        std::vector<std::shared_ptr<ClientSession>> snapshot = GetSessionSnapshot();
        for (auto& s : snapshot)
        {
            if (s->IsHeartbeatExpired(15))
                s->Close(static_cast<int>(DisconnectReason::HeartbeatTimeout));
        }
    }
}

// ─── 패킷 라우팅 ───────────────────────────────────────────────────
void ProfessorServer::OnSessionPacket(
    ClientSession* s, PacketType type, const uint8_t* payload, uint32_t len)
{
    // 로그인 전 학생의 첫 패킷
    if (type == PacketType::StudentLogin && s->studentId.empty())
    {
        HandleLogin(s, payload, len);
        return;
    }

    // 로그인 안 된 세션의 다른 패킷은 무시
    if (s->studentId.empty()) return;

    if (onPacketReceived)
        onPacketReceived(s->sessionId.c_str(), s->studentId.c_str(),
            s->studentName.c_str(),
            static_cast<uint32_t>(type), payload, len);
}

void ProfessorServer::HandleLogin(ClientSession* s, const uint8_t* payload, uint32_t len)
{
    if (len < sizeof(LoginPayload)) return;
    const auto* p = reinterpret_cast<const LoginPayload*>(payload);

    s->studentId = p->studentId;
    s->studentName = p->studentName;
    s->status = static_cast<uint32_t>(StudentStatus::Connected);

    // 로그인 승인 응답 전송
    LoginResponsePayload resp{};
    resp.success = 1;
    snprintf(resp.message, sizeof(resp.message),
        "접속 승인. 안녕하세요, %s님.", p->studentName);
    s->Send(PacketType::LoginResponse, &resp, sizeof(resp));

    // UI에 알림
    if (onStudentConnected)
        onStudentConnected(s->sessionId.c_str(), s->studentId.c_str(),
            s->studentName.c_str(), s->remoteAddr.c_str());
}

void ProfessorServer::OnSessionDisconnected(ClientSession* s, int reason)
{
    // 콜백에 넘길 식별 정보를 세션 파괴 전에 미리 복사한다.
    // (아래 erase가 마지막 shared_ptr를 없애 *s를 파괴할 수 있으므로,
    //  그 후 s를 역참조하면 use-after-free가 되어 프로세스가 죽는다)
    std::string sessionId = s->sessionId;
    std::string studentId = s->studentId;
    std::string studentName = s->studentName;

    // 세션 맵에서 제거
    {
        std::lock_guard<std::mutex> lock(sessionsMutex_);
        sessions_.erase(sessionId);
    }

    if (onStudentDisconnected)
        onStudentDisconnected(sessionId.c_str(), studentId.c_str(),
            studentName.c_str(), reason);
}

// ─── 브로드캐스트 / 개별 전송 ─────────────────────────────────────
int ProfessorServer::Broadcast(PacketType type, const void* payload, uint32_t len)
{
    int count = 0;
    for (auto& s : GetSessionSnapshot())
    {
        if (!s->studentId.empty() && s->Send(type, payload, len))
            count++;
    }
    return count;
}

int ProfessorServer::SendToSession(
    const std::string& sessionId, PacketType type, const void* payload, uint32_t len)
{
    std::lock_guard<std::mutex> lock(sessionsMutex_);
    auto it = sessions_.find(sessionId);
    if (it == sessions_.end()) return 0;
    return it->second->Send(type, payload, len) ? 1 : 0;
}

// ─── 파일 브로드캐스트 (비동기) ────────────────────────────────────
int ProfessorServer::BroadcastFile(const std::string& filePath, const std::string& password)
{
    auto snapshot = GetSessionSnapshot();
    if (snapshot.empty()) return 0;

    // 비동기: 각 세션마다 별도 스레드에서 전송
    std::thread([this, snapshot, filePath, password]() mutable
        {
            std::vector<std::thread> threads;
            threads.reserve(snapshot.size());

            for (auto& session : snapshot)
            {
                if (session->studentId.empty()) continue;
                threads.emplace_back([this, session, &filePath, &password]()
                    {
                        FileTransferSender::SendFile(
                            session->sock,
                            session->sendMutex,
                            filePath,
                            password,
                            [this, session](const std::string& tid, const std::string& fn, int pct)
                            { if (onSendProgress) onSendProgress(session->sessionId, session->studentId, tid, fn, pct); },
                            [this, session](const std::string& tid, const std::string& msg)
                            { if (onSendError) onSendError(session->sessionId, session->studentId, tid, msg); });
                    });
            }
            for (auto& t : threads) t.join();
        }).detach();

    return 1;
}

int ProfessorServer::SendFileToSession(
    const std::string& sessionId, const std::string& filePath, const std::string& password)
{
    std::shared_ptr<ClientSession> session;
    {
        std::lock_guard<std::mutex> lock(sessionsMutex_);
        auto it = sessions_.find(sessionId);
        if (it == sessions_.end()) return 0;
        session = it->second;
    }

    std::thread([this, session, filePath, password]()
        {
            FileTransferSender::SendFile(
                session->sock,
                session->sendMutex,
                filePath, password,
                [this, session](const std::string& tid, const std::string& fn, int pct)
                { if (onSendProgress) onSendProgress(session->sessionId, session->studentId, tid, fn, pct); },
                [this, session](const std::string& tid, const std::string& msg)
                { if (onSendError) onSendError(session->sessionId, session->studentId, tid, msg); });
        }).detach();

    return 1;
}

// ─── 유틸리티 ──────────────────────────────────────────────────────
int ProfessorServer::GetConnectedCount() const
{
    std::lock_guard<std::mutex> lock(sessionsMutex_);
    int cnt = 0;
    for (auto& [id, s] : sessions_)
        if (!s->studentId.empty()) cnt++;
    return cnt;
}

std::vector<std::shared_ptr<ClientSession>> ProfessorServer::GetSessionSnapshot() const
{
    std::lock_guard<std::mutex> lock(sessionsMutex_);
    std::vector<std::shared_ptr<ClientSession>> v;
    v.reserve(sessions_.size());
    for (auto& [id, s] : sessions_) v.push_back(s);
    return v;
}

std::string ProfessorServer::NewUUID()
{
    GUID guid;
    CoCreateGuid(&guid);
    char buf[37];
    snprintf(buf, sizeof(buf),
        "%08lx-%04x-%04x-%02x%02x-%02x%02x%02x%02x%02x%02x",
        guid.Data1, guid.Data2, guid.Data3,
        guid.Data4[0], guid.Data4[1], guid.Data4[2], guid.Data4[3],
        guid.Data4[4], guid.Data4[5], guid.Data4[6], guid.Data4[7]);
    return buf;
}
