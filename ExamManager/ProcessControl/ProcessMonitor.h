#pragma once

#include <string>
#include <vector>
#include <thread>
#include <atomic>
#include <mutex>
#include <functional>

// 콜백: (종류, 프로세스 이름)
// 종류: 0 = 블랙리스트 실행, 1 = 화이트리스트 종료
using DetectCallback = std::function<void(int type, const std::wstring& processName)>;

class ProcessMonitor
{
public:
    ProcessMonitor();
    ~ProcessMonitor();

    void SetBlacklist(const std::vector<std::wstring>& list);
    void SetWhitelist(const std::vector<std::wstring>& list);
    void SetDetectCallback(DetectCallback callback);

    void Start();
    void Stop();

private:
    void MonitorThreadFunc();
    void CheckOnce();

    std::vector<std::wstring> GetRunningProcesses();
    bool IsInList(const std::wstring& name, const std::vector<std::wstring>& list);

private:
    std::vector<std::wstring> m_blacklist;
    std::vector<std::wstring> m_whitelist;
    std::mutex m_listMutex;

    std::atomic<bool> m_running;
    std::thread m_monitorThread;

    DetectCallback m_detectCallback;
    std::mutex m_callbackMutex;

    DWORD m_myPid;

    // 화이트리스트 종료 감지용: 이전 검사 때 실행 중이던 화이트리스트 프로그램
    std::vector<std::wstring> m_prevRunningWhitelist;
};