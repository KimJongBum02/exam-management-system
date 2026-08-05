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

// 실행 중 프로세스 1개 (이름 + PID)
// PID를 함께 보관해, 블랙리스트 종료 시 스냅샷을 다시 뜨지 않도록 한다.
struct ProcessInfo
{
    std::wstring name;
    DWORD pid;
};

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

    std::vector<ProcessInfo> GetRunningProcesses();
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

    // 블랙리스트 알림 중복 방지용: 이전 검사 때 실행 중이던 블랙리스트 프로그램
    // (금지 프로세스가 계속 살아있어도 새로 등장했을 때만 한 번 알린다)
    std::vector<std::wstring> m_prevRunningBlacklist;
};