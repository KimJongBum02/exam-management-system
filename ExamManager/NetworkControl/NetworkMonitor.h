#pragma once

#include <string>
#include <vector>
#include <thread>
#include <atomic>
#include <mutex>
#include <set>
#include <functional>
#include <cstdint>

// 콜백: (감지된 도메인)
using DomainCallback = std::function<void(const std::wstring& domain)>;

class NetworkMonitor
{
public:
    NetworkMonitor();
    ~NetworkMonitor();

    void SetTargetDomains(const std::vector<std::wstring>& list);
    void SetUpstream(const std::wstring& upstreamIp);
    void SetDetectCallback(DomainCallback callback);

    bool Start();
    void Stop();

private:
    void CaptureThreadFunc();

    // DNS 메시지에서 질의 도메인(QNAME) 추출. 성공 시 true + outDomain 설정
    bool ExtractQname(const unsigned char* msg, int len, std::wstring& outDomain);

    // domain 이 대상 목록의 도메인을 접미사로 포함하는지 검사 (chat.openai.com ⊃ openai.com)
    bool MatchTarget(const std::wstring& domain, std::wstring& outMatched);

private:
    std::vector<std::wstring> m_targets;
    std::mutex m_targetMutex;

    std::string m_upstreamIp;        // 상위 DNS (기본 "8.8.8.8")
    std::mutex m_upstreamMutex;

    std::atomic<bool> m_running;
    std::thread m_captureThread;

    DomainCallback m_detectCallback;
    std::mutex m_callbackMutex;

    // 실제 타입은 SOCKET. 헤더에 winsock2.h 의존성 노출 방지용으로 uintptr_t 로 은닉
    std::uintptr_t m_listenSocket;

    // 중복 알림 방지: 이미 보고한 도메인 (브라우저는 같은 도메인을 반복 질의함)
    std::set<std::wstring> m_reported;
};