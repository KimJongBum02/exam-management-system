#include "pch.h"
#include "NetworkControl.h"
#include "NetworkMonitor.h"

#include <memory>

namespace {
    std::unique_ptr<NetworkMonitor> g_monitor;
    DetectCallback g_callback = nullptr;
}

// "A|B|C" → vector 파싱
static std::vector<std::wstring> ParsePipeSeparated(const wchar_t* input)
{
    std::vector<std::wstring> result;
    if (!input) return result;

    const wchar_t* start = input;
    const wchar_t* current = input;

    while (*current != L'\0')
    {
        if (*current == L'|')
        {
            if (current > start) result.emplace_back(start, current);
            start = current + 1;
        }
        current++;
    }
    if (current > start) result.emplace_back(start, current);

    return result;
}

// 내부 콜백: std::wstring → const wchar_t* 로 변환해서 외부 콜백 호출
static void InternalCallback(const std::wstring& domain)
{
    if (g_callback)
        g_callback(domain.c_str());
}

bool __stdcall NC_SetTargetDomains(const wchar_t* domains)
{
    if (!g_monitor) g_monitor = std::make_unique<NetworkMonitor>();
    g_monitor->SetTargetDomains(ParsePipeSeparated(domains));
    return true;
}

void __stdcall NC_SetUpstream(const wchar_t* upstreamIp)
{
    if (!g_monitor) g_monitor = std::make_unique<NetworkMonitor>();
    g_monitor->SetUpstream(upstreamIp ? std::wstring(upstreamIp) : std::wstring());
}

void __stdcall NC_RegisterCallback(DetectCallback callback)
{
    g_callback = callback;
}

bool __stdcall NC_Start()
{
    if (!g_monitor) g_monitor = std::make_unique<NetworkMonitor>();
    g_monitor->SetDetectCallback(InternalCallback);
    return g_monitor->Start();
}

void __stdcall NC_Stop()
{
    if (!g_monitor) return;
    g_monitor->Stop();
}