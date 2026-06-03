#include "pch.h"
#include "ProcessMonitor.h"

#include <tlhelp32.h>
#include <chrono>

ProcessMonitor::ProcessMonitor()
    : m_running(false)
    , m_myPid(GetCurrentProcessId())
{}

ProcessMonitor::~ProcessMonitor()
{
    Stop();
}

void ProcessMonitor::SetBlacklist(const std::vector<std::wstring>& list)
{
    std::lock_guard<std::mutex> lock(m_listMutex);
    m_blacklist = list;
}

void ProcessMonitor::SetWhitelist(const std::vector<std::wstring>& list)
{
    std::lock_guard<std::mutex> lock(m_listMutex);
    m_whitelist = list;
}

void ProcessMonitor::SetDetectCallback(DetectCallback callback)
{
    std::lock_guard<std::mutex> lock(m_callbackMutex);
    m_detectCallback = callback;
}

void ProcessMonitor::Start()
{
    if (m_running) return;

    m_running = true;
    m_prevRunningWhitelist.clear();
    m_monitorThread = std::thread(&ProcessMonitor::MonitorThreadFunc, this);
}

void ProcessMonitor::Stop()
{
    m_running = false;
    if (m_monitorThread.joinable())
    {
        m_monitorThread.join();
    }
}

void ProcessMonitor::MonitorThreadFunc()
{
    while (m_running)
    {
        CheckOnce();
        std::this_thread::sleep_for(std::chrono::milliseconds(500));
    }
}

std::vector<std::wstring> ProcessMonitor::GetRunningProcesses()
{
    std::vector<std::wstring> running;

    HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (snapshot == INVALID_HANDLE_VALUE) return running;

    PROCESSENTRY32W pe;
    pe.dwSize = sizeof(PROCESSENTRY32W);

    if (Process32FirstW(snapshot, &pe))
    {
        do
        {
            if (pe.th32ProcessID == m_myPid) continue;
            running.emplace_back(pe.szExeFile);
        } while (Process32NextW(snapshot, &pe));
    }

    CloseHandle(snapshot);
    return running;
}

bool ProcessMonitor::IsInList(const std::wstring& name, const std::vector<std::wstring>& list)
{
    for (const auto& item : list)
    {
        if (_wcsicmp(name.c_str(), item.c_str()) == 0)
        {
            return true;
        }
    }
    return false;
}

void ProcessMonitor::CheckOnce()
{
    std::vector<std::wstring> running = GetRunningProcesses();

    std::vector<std::wstring> blacklist, whitelist;
    {
        std::lock_guard<std::mutex> lock(m_listMutex);
        blacklist = m_blacklist;
        whitelist = m_whitelist;
    }

    DetectCallback cb;
    {
        std::lock_guard<std::mutex> lock(m_callbackMutex);
        cb = m_detectCallback;
    }

    // ===== 1. 블랙리스트 실행 감지 → 종료 + 콜백 =====
    for (const auto& proc : running)
    {
        if (IsInList(proc, blacklist))
        {
            HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
            if (snapshot != INVALID_HANDLE_VALUE)
            {
                PROCESSENTRY32W pe;
                pe.dwSize = sizeof(PROCESSENTRY32W);
                if (Process32FirstW(snapshot, &pe))
                {
                    do
                    {
                        if (_wcsicmp(pe.szExeFile, proc.c_str()) == 0)
                        {
                            HANDLE h = OpenProcess(PROCESS_TERMINATE, FALSE, pe.th32ProcessID);
                            if (h)
                            {
                                TerminateProcess(h, 1);
                                CloseHandle(h);
                            }
                        }
                    } while (Process32NextW(snapshot, &pe));
                }
                CloseHandle(snapshot);
            }

            if (cb) cb(0, proc);  // type 0 = 블랙리스트 실행
        }
    }

    // ===== 2. 화이트리스트 종료 감지 → 콜백 =====
    std::vector<std::wstring> currentWhitelist;
    for (const auto& proc : running)
    {
        if (IsInList(proc, whitelist))
        {
            currentWhitelist.push_back(proc);
        }
    }

    for (const auto& prev : m_prevRunningWhitelist)
    {
        if (!IsInList(prev, currentWhitelist))
        {
            if (cb) cb(1, prev);  // type 1 = 화이트리스트 종료
        }
    }

    m_prevRunningWhitelist = currentWhitelist;
}