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
    // 잠그지 않으면 두 호출이 동시에 아래 if를 통과할 수 있다.
    // 그러면 감시 스레드가 2개 뜨고, 아직 살아있는 thread 객체를 덮어쓰면서
    // std::terminate로 앱이 즉사한다. (시험 시작 버튼 중복 클릭 등)
    std::lock_guard<std::mutex> lock(m_stateMutex);

    if (m_running) return;

    m_running = true;
    m_prevRunningWhitelist.clear();
    m_prevRunningBlacklist.clear();
    m_reportedNew.clear();

    // 감시를 켠 직후 첫 검사는 '정리'다.
    // 시험 전부터 켜져 있던 금지 프로그램을 끄기는 하되, 부정행위로 알리지는 않는다.
    // (이전 목록이 비어 있어 첫 검사에서는 실행 중인 것이 전부 신규로 보인다)
    m_firstCheck = true;

    // 이 시각이 신규 판정의 기준선이다. 스레드를 띄우기 전에 찍어야
    // 첫 검사와 기준선 사이에 실행된 프로세스를 놓치지 않는다.
    GetSystemTimeAsFileTime(&m_startTime);

    m_monitorThread = std::thread(&ProcessMonitor::MonitorThreadFunc, this);
}

void ProcessMonitor::Stop()
{
    // Start와 같은 자물쇠를 써서 시작/중지가 서로 엉키지 않게 한다.
    std::lock_guard<std::mutex> lock(m_stateMutex);

    {
        // m_wakeMutex를 잡고 바꿔야 한다. 안 그러면 감시 스레드가 조건을 확인한 뒤
        // 잠들기 직전에 알림이 지나가 버려서, 결국 500ms를 다 기다리게 된다.
        std::lock_guard<std::mutex> wake(m_wakeMutex);
        m_running = false;
    }
    m_wakeCv.notify_all();

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

        // sleep_for와 달리 Stop이 중간에 깨울 수 있다.
        // 그래서 UI 스레드가 최대 500ms를 기다리는 일이 없다.
        std::unique_lock<std::mutex> lock(m_wakeMutex);
        m_wakeCv.wait_for(lock, std::chrono::milliseconds(500),
                          [this] { return !m_running; });
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

// 화이트리스트는 블랙리스트와 반대로 엄격하게 본다.
// 차단은 의심스러우면 걸어도 되지만, 허용은 확실할 때만 줘야 하기 때문이다.
// 특히 화이트리스트는 신규 프로세스 보고를 면제하므로, 느슨하면 허용된 이름으로
// 위장하는 것만으로 완전 무탐지가 된다.
//
// 파일명과 OriginalFilename이 '둘 다' 목록으로 설명되어야 허용한다.
// OriginalFilename이 없으면 신원을 증명하지 못한 것이므로 면제하지 않는다.
// (리소스를 지우고 허용된 이름을 붙이는 우회를 막기 위함)
//
// 정상 소프트웨어도 같은 바이너리를 다른 이름으로 배포하므로
// (git.exe → bash.exe 등) 그런 경우는 두 이름을 모두 목록에 넣으면 된다.
// 학생의 행위로 볼 수 없는 Windows 자체 구성요소.
//
// explorer.exe는 바탕화면과 작업표시줄 그 자체라 로그인 시점부터 항상 떠 있다.
// 폴더를 열 때마다 새 프로세스가 잠깐 생기는데, 시험 폴더를 자동으로 띄우는 것도
// 우리 프로그램이라 알림이 계속 쌓인다.
//
// 게다가 알려 봐야 막을 수 없다. 학생은 이미 떠 있는 탐색기를 쓰면 되고,
// 그때는 새 프로세스가 생기지 않아 어차피 잡히지 않는다.
//
// type 2('모르는 프로그램') 판정에서만 뺀다.
// 교수가 블랙리스트에 직접 넣었다면 그건 명시적인 의사이므로 그대로 종료한다.
static bool IsSystemComponent(const std::wstring& matchName)
{
    static const wchar_t* kComponents[] = { L"explorer" };

    for (const wchar_t* name : kComponents)
        if (_wcsicmp(matchName.c_str(), name) == 0) return true;

    return false;
}
// EnumWindows가 창을 하나씩 넘겨줄 때마다 불린다.
// 학생이 보고 조작할 수 있는 창만 추린다.
static BOOL CALLBACK CollectWindowOwner(HWND hwnd, LPARAM lparam)
{
    // 숨겨진 창은 백그라운드 프로그램이 내부 용도로 만든 것이다.
    if (!IsWindowVisible(hwnd)) return TRUE;

    // 제목이 없는 창도 대부분 보조 창이다. 학생이 쓰는 프로그램은 제목이 있다.
    if (GetWindowTextLengthW(hwnd) == 0) return TRUE;

    DWORD pid = 0;
    GetWindowThreadProcessId(hwnd, &pid);
    if (pid != 0)
        reinterpret_cast<std::set<DWORD>*>(lparam)->insert(pid);

    return TRUE;   // 계속 순회
}

// 지금 화면에 창을 띄우고 있는 프로세스들의 PID를 모은다.
//
// RuntimeBroker.exe나 backgroundTaskHost.exe처럼 Windows가 알아서 띄우는 프로세스는
// 창이 없다. 학생이 직접 실행한 것과 구분하는 기준으로 쓴다.
std::set<DWORD> ProcessMonitor::GetPidsWithVisibleWindow()
{
    std::set<DWORD> pids;
    EnumWindows(CollectWindowOwner, reinterpret_cast<LPARAM>(&pids));
    return pids;
}
bool ProcessMonitor::IsWhitelisted(const ProcessInfo& proc, const std::vector<std::wstring>& whitelist)
{
    return IsInList(proc.matchName, whitelist)
        && !proc.originalName.empty()
        && IsInList(proc.originalName, whitelist);
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

    // 감시를 켠 뒤에 실행된 것만 따로 모은다.
    // 첫 검사에서 '시험 전부터 켜져 있던 것'과 구분하는 데 쓴다.
    std::vector<std::wstring> newlyStarted;

    for (const auto& proc : running)
    {
        // 리스트와 비교할 때만 정규화된 이름을 쓴다.
        // 아래 currentBlacklist는 스냅샷끼리의 비교라 원본 이름 그대로 다뤄도 일관된다.
        if (IsBlacklisted(proc, blacklist))
        {
            // Windows 자체 구성요소는 목록에 들어와도 종료하지 않는다.
            // explorer.exe를 죽이면 바탕화면과 작업표시줄이 통째로 사라진다.
            // 교수가 실수로 넣으면 학생 PC 전체가 시험 도중 화면을 잃게 되는데,
            // 그건 어떤 경우에도 의도한 결과가 아니다.
            if (IsSystemComponent(proc.matchName)) continue;

            // PID를 이미 알고 있으므로 재스냅샷 없이 바로 종료
            HANDLE h = OpenProcess(PROCESS_TERMINATE, FALSE, proc.pid);
            if (h)
            {
                TerminateProcess(h, 1);
                CloseHandle(h);
            }

            if (!IsInList(proc.name, currentBlacklist))
                currentBlacklist.push_back(proc.name);

            if (proc.isNew && !IsInList(proc.name, newlyStarted))
                newlyStarted.push_back(proc.name);
        }
    }

    for (const auto& name : currentBlacklist)
    {
        if (IsInList(name, m_prevRunningBlacklist)) continue;   // 이미 알린 것

        // 첫 검사에서는 감시를 켠 뒤에 실행된 것만 알린다.
        // 시험 전부터 켜져 있던 금지 프로그램은 끄기는 하되 부정행위가 아니라 정리 대상이다.
        // (두 번째 검사부터는 이전 목록과 비교하므로 이 구분이 필요 없다)
        if (m_firstCheck && !IsInList(name, newlyStarted)) continue;

        if (cb) cb(0, name);  // type 0 = 블랙리스트 실행 (신규 등장)
    }

    m_prevRunningBlacklist = currentBlacklist;

    // ===== 2. 화이트리스트 종료 감지 → 콜백 =====
    std::vector<std::wstring> currentWhitelist;
    for (const auto& proc : running)
    {
        if (IsWhitelisted(proc, whitelist))
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
    //
    // 단, 창이 있는 것만 본다. Windows가 알아서 띄우는 백그라운드 프로세스
    // (RuntimeBroker.exe 등)까지 잡으면 알림이 그런 것들로 가득 차 진짜 부정행위가 묻힌다.
    // 창 없이 도는 금지 프로그램은 블랙리스트가 창 여부와 무관하게 종료하므로 여기서 뺀다.
    std::set<DWORD> windowedPids = GetPidsWithVisibleWindow();

    for (const auto& proc : running)
    {
        if (!proc.isNew) continue;
        if (IsBlacklisted(proc, blacklist)) continue;        // 이미 type 0으로 보고됨
        if (IsWhitelisted(proc, whitelist)) continue;        // 시험에 필요한 프로그램
        if (IsSystemComponent(proc.matchName)) continue;     // Windows 자체 구성요소
        if (m_reportedNew.count(proc.pid)) continue;         // PID당 한 번만 알린다

        // 창이 아직 안 떴을 수도 있으므로 여기서는 기록하지 않고 넘긴다.
        // 다음 검사에서 창이 생기면 그때 알린다.
        if (!windowedPids.count(proc.pid)) continue;

        m_reportedNew.insert(proc.pid);
        if (cb) cb(2, proc.name);  // type 2 = 목록에 없는 신규 프로세스
    }

    // 첫 검사가 끝났다. 다음 검사부터는 실제 부정행위로 보고 알린다.
    m_firstCheck = false;
}