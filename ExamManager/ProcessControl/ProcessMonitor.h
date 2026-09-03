#pragma once

#include <string>
#include <vector>
#include <map>
#include <set>
#include <thread>
#include <atomic>
#include <mutex>
#include <condition_variable>
#include <functional>

// 콜백: (종류, 프로세스 이름)
// 종류: 0 = 블랙리스트 실행, 1 = 화이트리스트 종료
using DetectCallback = std::function<void(int type, const std::wstring& processName)>;

// 실행 중 프로세스 1개
// PID를 함께 보관해, 블랙리스트 종료 시 스냅샷을 다시 뜨지 않도록 한다.
// name은 원본 파일명("notepad.exe", 표시/콜백용),
// matchName은 확장자를 뗀 이름("notepad", 리스트 비교 전용)이다.
// path와 originalName은 이름 위조 탐지용이며, 얻지 못하면 빈 문자열이다.
// (시스템 프로세스는 경로를, 버전 리소스가 없는 파일은 originalName을 얻을 수 없다)
// isNew는 감시 시작(= 시험 시작) 이후에 실행된 프로세스라는 뜻이다.
struct ProcessInfo
{
    std::wstring name;
    std::wstring matchName;
    std::wstring path;
    std::wstring originalName;  // 버전 리소스의 OriginalFilename, 확장자 제거된 상태
    DWORD pid;
    bool isNew;
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
    bool IsBlacklisted(const ProcessInfo& proc, const std::vector<std::wstring>& blacklist);
    bool IsWhitelisted(const ProcessInfo& proc, const std::vector<std::wstring>& whitelist);

private:
    // 확장자를 뗀 상태로 보관한다(Set*list에서 정규화).
    std::vector<std::wstring> m_blacklist;
    std::vector<std::wstring> m_whitelist;
    std::mutex m_listMutex;

    std::atomic<bool> m_running;
    std::thread m_monitorThread;

    // Start/Stop이 동시에 들어와도 한 번에 하나만 실행되게 한다.
    std::mutex m_stateMutex;

    // 검사 간격(500ms) 대기를 중간에 깨우기 위한 것. Stop이 즉시 반응하게 해준다.
    std::condition_variable m_wakeCv;
    std::mutex m_wakeMutex;

    DetectCallback m_detectCallback;
    std::mutex m_callbackMutex;

    DWORD m_myPid;

    // 감시를 시작한 시각(= 시험 시작 시각). 이보다 나중에 생성된 프로세스를 신규로 본다.
    // 시작 시점의 PID 목록을 따로 보관하지 않으므로 PID 재사용에 영향받지 않는다.
    FILETIME m_startTime;

    // 신규 프로세스 알림 중복 방지용: 이미 보고한 PID
    std::set<DWORD> m_reportedNew;

    // 감시를 켠 뒤 첫 검사인지. 첫 검사는 시험 전부터 켜져 있던 것을 정리하는 단계라
    // 종료만 하고 부정행위로 알리지 않는다.
    bool m_firstCheck = true;

    // 경로 → OriginalFilename(확장자 제거) 캐시.
    // 버전 리소스 조회는 디스크를 읽으므로 매 검사(500ms)마다 하면 부하가 크다.
    // 같은 파일의 값은 변하지 않으니 경로 기준으로 한 번만 읽는다.
    // 감시 스레드에서만 접근하므로 별도 락이 필요 없다.
    std::map<std::wstring, std::wstring> m_originalNameCache;

    // 화이트리스트 종료 감지용: 이전 검사 때 실행 중이던 화이트리스트 프로그램
    std::vector<std::wstring> m_prevRunningWhitelist;

    // 블랙리스트 알림 중복 방지용: 이전 검사 때 실행 중이던 블랙리스트 프로그램
    // (금지 프로세스가 계속 살아있어도 새로 등장했을 때만 한 번 알린다)
    std::vector<std::wstring> m_prevRunningBlacklist;
};