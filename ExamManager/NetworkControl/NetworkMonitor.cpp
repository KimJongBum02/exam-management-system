#include "pch.h"
#include "NetworkMonitor.h"

#include <winsock2.h>
#include <ws2tcpip.h>
#include <cwctype>

// ws2_32.lib 를 링커에 자동 추가 (Windows 기본 제공, 별도 SDK 불필요)
#pragma comment(lib, "ws2_32.lib")

namespace {
    constexpr int DNS_PORT = 53;
    constexpr int DNS_HEADER_LEN = 12;
    constexpr int LISTEN_TIMEOUT_MS = 500;     // recvfrom 타임아웃 → Stop() 시 루프가 주기적으로 깨어남
    constexpr int UPSTREAM_TIMEOUT_MS = 3000;  // 상위 DNS 무응답 시 이 조회 포기
    constexpr int BUF_SIZE = 4096;             // EDNS0 대비 넉넉히

    // 조회를 상위 DNS로 전달하고 응답을 클라이언트에게 릴레이 (차단하지 않음)
    void ForwardQuery(SOCKET listener, const char* query, int queryLen,
        const sockaddr_in& client, const sockaddr_in& upstream)
    {
        SOCKET up = socket(AF_INET, SOCK_DGRAM, IPPROTO_UDP);
        if (up == INVALID_SOCKET) return;

        DWORD timeout = UPSTREAM_TIMEOUT_MS;
        setsockopt(up, SOL_SOCKET, SO_RCVTIMEO, (const char*)&timeout, sizeof(timeout));

        sendto(up, query, queryLen, 0, (const sockaddr*)&upstream, sizeof(upstream));

        char resp[BUF_SIZE];
        int n = recvfrom(up, resp, sizeof(resp), 0, nullptr, nullptr);
        closesocket(up);

        if (n > 0)
            sendto(listener, resp, n, 0, (const sockaddr*)&client, sizeof(client));
    }
}

NetworkMonitor::NetworkMonitor()
    : m_upstreamIp("8.8.8.8")
    , m_running(false)
    , m_listenSocket((std::uintptr_t)INVALID_SOCKET)
{}

NetworkMonitor::~NetworkMonitor()
{
    Stop();
}

void NetworkMonitor::SetTargetDomains(const std::vector<std::wstring>& list)
{
    std::lock_guard<std::mutex> lock(m_targetMutex);
    m_targets = list;
}

void NetworkMonitor::SetUpstream(const std::wstring& upstreamIp)
{
    // wstring(ASCII IP) → string
    std::lock_guard<std::mutex> lock(m_upstreamMutex);
    if (!upstreamIp.empty())
        m_upstreamIp.assign(upstreamIp.begin(), upstreamIp.end());
}

void NetworkMonitor::SetDetectCallback(DomainCallback callback)
{
    std::lock_guard<std::mutex> lock(m_callbackMutex);
    m_detectCallback = callback;
}

bool NetworkMonitor::Start()
{
    if (m_running) return true;

    WSADATA wsa;
    if (WSAStartup(MAKEWORD(2, 2), &wsa) != 0)
        return false;

    SOCKET s = socket(AF_INET, SOCK_DGRAM, IPPROTO_UDP);
    if (s == INVALID_SOCKET)
    {
        WSACleanup();
        return false;
    }

    // 127.0.0.1:53 에 바인딩 (DNS 를 127.0.0.1 로 지정하면 조회가 여기로 들어옴)
    sockaddr_in addr{};
    addr.sin_family = AF_INET;
    addr.sin_port = htons(DNS_PORT);
    inet_pton(AF_INET, "127.0.0.1", &addr.sin_addr);
    if (bind(s, (const sockaddr*)&addr, sizeof(addr)) == SOCKET_ERROR)
    {
        closesocket(s);
        WSACleanup();
        return false;   // 이미 다른 프로세스가 53 포트를 쓰는 경우 등
    }

    // recvfrom 타임아웃 → Stop() 시 루프가 최대 500ms 안에 깨어나 종료 확인
    DWORD timeout = LISTEN_TIMEOUT_MS;
    setsockopt(s, SOL_SOCKET, SO_RCVTIMEO, (const char*)&timeout, sizeof(timeout));

    m_listenSocket = (std::uintptr_t)s;

    {
        std::lock_guard<std::mutex> lock(m_targetMutex);
        m_reported.clear();
    }

    m_running = true;
    m_captureThread = std::thread(&NetworkMonitor::CaptureThreadFunc, this);
    return true;
}

void NetworkMonitor::Stop()
{
    m_running = false;

    SOCKET s = (SOCKET)m_listenSocket;
    if (s != INVALID_SOCKET)
        closesocket(s);   // 블록된 recvfrom 을 즉시 깨움

    if (m_captureThread.joinable())
        m_captureThread.join();

    if (s != INVALID_SOCKET)
    {
        m_listenSocket = (std::uintptr_t)INVALID_SOCKET;
        WSACleanup();
    }
}

void NetworkMonitor::CaptureThreadFunc()
{
    SOCKET listener = (SOCKET)m_listenSocket;

    // 상위 DNS 주소 준비
    sockaddr_in upstream{};
    upstream.sin_family = AF_INET;
    upstream.sin_port = htons(DNS_PORT);
    {
        std::lock_guard<std::mutex> lock(m_upstreamMutex);
        inet_pton(AF_INET, m_upstreamIp.c_str(), &upstream.sin_addr);
    }

    char buf[BUF_SIZE];
    while (m_running)
    {
        sockaddr_in client{};
        int clientLen = sizeof(client);
        int n = recvfrom(listener, buf, sizeof(buf), 0, (sockaddr*)&client, &clientLen);

        if (n == SOCKET_ERROR)
        {
            if (WSAGetLastError() == WSAETIMEDOUT) continue;   // 타임아웃 → m_running 재확인
            break;                                             // 소켓 닫힘 등 → 종료
        }
        if (n <= 0) continue;

        // 1) 도메인 추출 → 금지 목록 검사 (차단하지 않고 감지만)
        std::wstring domain;
        if (ExtractQname((const unsigned char*)buf, n, domain))
        {
            std::wstring matched;
            if (MatchTarget(domain, matched))
            {
                bool first;
                {
                    std::lock_guard<std::mutex> lock(m_targetMutex);
                    first = m_reported.insert(matched).second;   // 처음 보는 도메인이면 true
                }
                if (first)
                {
                    DomainCallback cb;
                    {
                        std::lock_guard<std::mutex> lock(m_callbackMutex);
                        cb = m_detectCallback;
                    }
                    if (cb) cb(matched);
                }
            }
        }

        // 2) 상위 DNS로 전달하고 응답을 릴레이 (정상 조회 유지)
        ForwardQuery(listener, buf, n, client, upstream);
    }
}

bool NetworkMonitor::ExtractQname(const unsigned char* msg, int len, std::wstring& outDomain)
{
    // UdpClient/recvfrom 은 UDP 페이로드(=DNS 메시지)만 주므로 이더넷/IP/UDP 헤더 파싱이 불필요
    if (len < DNS_HEADER_LEN + 1) return false;

    int pos = DNS_HEADER_LEN;                        // DNS 헤더(12B) 건너뛰기
    std::string name;
    while (pos < len)
    {
        int labelLen = msg[pos++];
        if (labelLen == 0) break;                    // 0 = 이름 끝
        if ((labelLen & 0xC0) != 0) return false;    // 압축 포인터 (질의 QNAME 엔 없음)
        if (pos + labelLen > len) return false;      // 버퍼 범위 초과
        if (!name.empty()) name += '.';
        name.append(reinterpret_cast<const char*>(msg + pos), labelLen);
        pos += labelLen;
    }
    if (name.empty()) return false;

    outDomain.assign(name.begin(), name.end());      // DNS 이름은 ASCII
    return true;
}

bool NetworkMonitor::MatchTarget(const std::wstring& domain, std::wstring& outMatched)
{
    std::vector<std::wstring> targets;
    {
        std::lock_guard<std::mutex> lock(m_targetMutex);
        targets = m_targets;
    }

    std::wstring lower = domain;
    for (auto& c : lower) c = (wchar_t)towlower(c);

    for (const auto& t : targets)
    {
        std::wstring tl = t;
        for (auto& c : tl) c = (wchar_t)towlower(c);
        if (tl.empty()) continue;

        // 정확 일치
        if (lower == tl)
        {
            outMatched = t;
            return true;
        }
        // 서브도메인 접미사 일치: "chat.openai.com" 이 ".openai.com" 로 끝나는지
        if (lower.size() > tl.size() &&
            lower.compare(lower.size() - tl.size(), tl.size(), tl) == 0 &&
            lower[lower.size() - tl.size() - 1] == L'.')
        {
            outMatched = t;
            return true;
        }
    }
    return false;
}