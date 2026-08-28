#include "pch.h"
#include "ProcessMonitor.h"

#include <tlhelp32.h>
#include <chrono>

#pragma comment(lib, "version.lib")

// 파일명에서 마지막 확장자 하나를 제거한다.
// 교수 UI는 .NET Process.ProcessName(확장자 없음)을 보내지만 스냅샷은
// szExeFile(확장자 포함)을 주므로, 양쪽을 같은 규칙으로 맞춰야 매칭된다.
// 점이 없는 이름("System", "Registry", "Secure System" 등 시스템 프로세스)은 그대로 둔다.
static std::wstring StripExtension(const std::wstring& name)
{
    size_t dot = name.rfind(L'.');
    if (dot == std::wstring::npos) return name;
    return name.substr(0, dot);
}

// PID로 실행 파일의 전체 경로와 프로세스 생성 시각을 얻는다.
// 핸들을 두 번 열지 않도록 한 번에 처리한다.
// System, Registry 같은 시스템 프로세스는 열 수 없으므로 실패가 정상 결과이며,
// 이때 path는 빈 문자열, creation은 건드리지 않고 false를 돌려준다.
static bool QueryProcessDetail(DWORD pid, std::wstring& path, FILETIME& creation)
{
    HANDLE h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, pid);
    if (!h) return false;

    wchar_t buffer[MAX_PATH];
    DWORD size = MAX_PATH;
    if (QueryFullProcessImageNameW(h, 0, buffer, &size))
        path.assign(buffer, size);

    FILETIME exitTime, kernelTime, userTime;
    bool gotCreation = GetProcessTimes(h, &creation, &exitTime, &kernelTime, &userTime) != FALSE;

    CloseHandle(h);
    return gotCreation;
}

// 실행 파일의 버전 리소스에서 OriginalFilename을 읽어 확장자를 뗀 채 돌려준다.
// 이 값은 컴파일 시점에 박히므로 파일 이름을 바꿔도 따라 변하지 않는다.
// 버전 리소스가 아예 없는 실행 파일이 흔하므로 빈 문자열도 정상 결과다.
//
// FILE_VER_GET_NEUTRAL이 반드시 필요하다. 이 플래그가 없으면 MUI(다국어 리소스)
// 리다이렉션이 일어나 System32\ko-KR\ping.exe.mui 쪽 값을 읽고,
// OriginalFilename이 "ping.exe.mui"로 나와 매칭에 실패한다.
static std::wstring QueryOriginalName(const std::wstring& path)
{
    if (path.empty()) return std::wstring();

    DWORD ignored = 0;
    DWORD size = GetFileVersionInfoSizeExW(FILE_VER_GET_NEUTRAL, path.c_str(), &ignored);
    if (size == 0) return std::wstring();

    std::vector<BYTE> data(size);
    if (!GetFileVersionInfoExW(FILE_VER_GET_NEUTRAL, path.c_str(), 0, size, data.data()))
        return std::wstring();

    // 문자열 블록은 언어/코드페이지별로 나뉘어 있어, 번역 테이블을 먼저 읽어야
    // 조회할 경로를 만들 수 있다. 첫 번째로 값이 나오는 언어를 쓴다.
    struct LangCodePage { WORD language; WORD codePage; };
    LangCodePage* translations = nullptr;
    UINT translationBytes = 0;

    if (!VerQueryValueW(data.data(), L"\\VarFileInfo\\Translation",
                        reinterpret_cast<void**>(&translations), &translationBytes))
        return std::wstring();

    for (UINT i = 0; i < translationBytes / sizeof(LangCodePage); i++)
    {
        wchar_t subBlock[64];
        swprintf_s(subBlock, L"\\StringFileInfo\\%04x%04x\\OriginalFilename",
                   translations[i].language, translations[i].codePage);

        wchar_t* value = nullptr;
        UINT valueLen = 0;

        if (VerQueryValueW(data.data(), subBlock, reinterpret_cast<void**>(&value), &valueLen) && valueLen > 0)
            return StripExtension(std::wstring(value, wcsnlen(value, valueLen)));
    }

    return std::wstring();
}

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
    // 매 검사마다 정규화하지 않도록 보관 시점에 한 번만 확장자를 뗀다.
    std::vector<std::wstring> normalized;
    normalized.reserve(list.size());
    for (const auto& item : list)
        normalized.push_back(StripExtension(item));

    std::lock_guard<std::mutex> lock(m_listMutex);
    m_blacklist = std::move(normalized);
}

void ProcessMonitor::SetWhitelist(const std::vector<std::wstring>& list)
{
    std::vector<std::wstring> normalized;
    normalized.reserve(list.size());
    for (const auto& item : list)
        normalized.push_back(StripExtension(item));

    std::lock_guard<std::mutex> lock(m_listMutex);
    m_whitelist = std::move(normalized);
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
    m_reportedNew.clear();

    // 이 시각이 신규 판정의 기준선이다. 스레드를 띄우기 전에 찍어야
    // 첫 검사와 기준선 사이에 실행된 프로세스를 놓치지 않는다.
    GetSystemTimeAsFileTime(&m_startTime);

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

            // 비교용 이름도 여기서 한 번만 만들어 둔다(프로세스당 1회).
            std::wstring name = pe.szExeFile;
            std::wstring path;
            FILETIME creation = {};
            bool gotCreation = QueryProcessDetail(pe.th32ProcessID, path, creation);

            // 경로 조회는 커널 호출이라 매번 해도 싸지만, OriginalFilename은
            // 디스크를 읽으므로 경로를 키로 캐시한다. 처음 보는 파일일 때만 실제로 읽는다.
            std::wstring originalName;
            if (!path.empty())
            {
                auto cached = m_originalNameCache.find(path);
                if (cached == m_originalNameCache.end())
                    cached = m_originalNameCache.emplace(path, QueryOriginalName(path)).first;

                originalName = cached->second;
            }

            // 생성 시각을 못 얻으면 신규로 보지 않는다. 핸들을 못 여는 건 SYSTEM 권한
            // 프로세스뿐이고, 이를 신규로 처리하면 시험 중 시작되는 윈도우 서비스가
            // 전부 오탐으로 올라온다. 학생이 띄운 프로세스는 항상 열 수 있다.
            bool isNew = gotCreation && CompareFileTime(&creation, &m_startTime) > 0;

            running.push_back({ name, StripExtension(name), path, originalName, pe.th32ProcessID, isNew });
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

// 파일명과 OriginalFilename 중 하나만 걸려도 차단 대상으로 본다. 파일명을 바꿔도
// OriginalFilename은 남으므로, cheat.exe로 위장한 notepad도 여기서 잡힌다.
// 종료 판정과 신규 보고 제외가 같은 기준을 써야 하므로 함수로 묶는다.
bool ProcessMonitor::IsBlacklisted(const ProcessInfo& proc, const std::vector<std::wstring>& blacklist)
{
    return IsInList(proc.matchName, blacklist)
        || (!proc.originalName.empty() && IsInList(proc.originalName, blacklist));
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
        // 리스트와 비교할 때만 정규화된 이름을 쓴다.
        // 아래 currentBlacklist는 스냅샷끼리의 비교라 원본 이름 그대로 다뤄도 일관된다.
        if (IsBlacklisted(proc, blacklist))
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
        if (IsInList(proc.matchName, whitelist))
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

    // ===== 3. 어느 목록에도 없는 신규 프로세스 → 콜백 =====
    // 블랙리스트는 '알려진 것'만 막으므로, 목록에 없는 프로그램은 그대로 통과한다.
    // 시험 시작 후 새로 실행됐다는 사실 자체를 근거로 삼아 이 구멍을 메운다.
    // 종료하지 않고 알리기만 한다. 판단은 교수 몫이다.
    for (const auto& proc : running)
    {
        if (!proc.isNew) continue;
        if (IsBlacklisted(proc, blacklist)) continue;        // 이미 type 0으로 보고됨
        if (IsInList(proc.matchName, whitelist)) continue;   // 시험에 필요한 프로그램
        if (m_reportedNew.count(proc.pid)) continue;         // PID당 한 번만 알린다

        m_reportedNew.insert(proc.pid);
        if (cb) cb(2, proc.name);  // type 2 = 목록에 없는 신규 프로세스
    }
}