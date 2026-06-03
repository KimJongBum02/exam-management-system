#include "pch.h"
#include "ProcessControl.h"
#include "ProcessMonitor.h"

#include <memory>

namespace {
    std::unique_ptr<ProcessMonitor> g_monitor;
    CheatCallback g_callback = nullptr;
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
static void InternalCallback(int type, const std::wstring& processName)
{
    if (g_callback)
    {
        g_callback(type, processName.c_str());
    }
}

bool __stdcall PM_SetBlacklist(const wchar_t* blacklist)
{
    if (!g_monitor) g_monitor = std::make_unique<ProcessMonitor>();
    g_monitor->SetBlacklist(ParsePipeSeparated(blacklist));
    return true;
}

bool __stdcall PM_SetWhitelist(const wchar_t* whitelist)
{
    if (!g_monitor) g_monitor = std::make_unique<ProcessMonitor>();
    g_monitor->SetWhitelist(ParsePipeSeparated(whitelist));
    return true;
}

void __stdcall PM_RegisterCallback(CheatCallback callback)
{
    g_callback = callback;
}

bool __stdcall PM_Start()
{
    if (!g_monitor) g_monitor = std::make_unique<ProcessMonitor>();
    g_monitor->SetDetectCallback(InternalCallback);
    g_monitor->Start();
    return true;
}

void __stdcall PM_Stop()
{
    if (!g_monitor) return;
    g_monitor->Stop();
}