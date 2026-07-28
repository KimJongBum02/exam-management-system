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
    m_prevRunningBlacklist.clear();
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

std::vector<ProcessInfo> ProcessMonitor::GetRunningProcesses()
{
    std::vector<ProcessInfo> running;

    HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (snapshot == INVALID_HANDLE_VALUE) return running;

    PROCESSENTRY32W pe;
    pe.dwSize = sizeof(PROCESSENTRY32W);

    if (Process32FirstW(snapshot, &pe))
    {
        do
        {
            if (pe.th32ProcessID == m_myPid) continue;
            running.push_back({ pe.szExeFile, pe.th32ProcessID });
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
    std::vector<ProcessInfo> running = GetRunningProcesses();

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

    // ===== 1. 블랙리스트: 실행 중이면 종료, 새로 등장했을 때만 알림 =====
    // 종료는 매 검사마다 시도하되(못 죽인 프로세스는 계속 재시도),
    // 콜백은 '없다→있다'로 새로 등장한 경우에만 발화해 500ms 반복 알림을 막는다.
    std::vector<std::wstring> currentBlacklist;
    for (const auto& proc : running)
    {
        if (IsInList(proc.name, blacklist))
        {
            // PID를 이미 알고 있으므로 재스냅샷 없이 바로 종료
            HANDLE h = OpenProcess(PROCESS_TERMINATE, FALSE, proc.pid);
            if (h)
            {
                TerminateProcess(h, 1);
                CloseHandle(h);
            }

            if (!IsInList(proc.name, currentBlacklist))
                currentBlacklist.push_back(proc.name);
        }
    }

    for (const auto& name : currentBlacklist)
    {
        if (!IsInList(name, m_prevRunningBlacklist))
        {
            if (cb) cb(0, name);  // type 0 = 블랙리스트 실행 (신규 등장)
        }
    }

    m_prevRunningBlacklist = currentBlacklist;

    // ===== 2. 화이트리스트 종료 감지 → 콜백 =====
    std::vector<std::wstring> currentWhitelist;
    for (const auto& proc : running)
    {
        if (IsInList(proc.name, whitelist))
        {
            currentWhitelist.push_back(proc.name);
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