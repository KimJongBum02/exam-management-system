using System.Collections.Generic;

namespace ProfessorUI.Service
{
    public enum KnownProgramKind
    {
        Ai,         // 생성형 AI — 금지 목록 기본값
        DevTool,    // 개발 도구 — 허용 목록 기본값
        WindowsApp, // 윈도우 기본 앱 — 기본값 없이 선택창에만 보인다
    }

    // 이름만 알고 있는 프로그램들의 사전.
    //
    // 시작 메뉴 훑기는 '이 PC 에 설치된 것'만 찾는다. 그런데 감시 목록을 짜는 곳은
    // 교수 PC 이고 감시가 도는 곳은 학생 PC 라, 학생 PC 에만 깔린 프로그램은
    // 교수 화면에 뜨지 않는다. 그것을 메우려고 자주 쓰이는 이름을 미리 적어 둔다.
    //
    // OriginalName 은 실행 파일에 박힌 원래 이름이다. 비워 두면 ExecutableName 과
    // 같다고 본다. 허용 목록은 두 이름이 모두 있어야 인정되므로(위장 방지),
    // 다른 경우에는 반드시 적어야 한다. 금지 목록은 둘 중 하나만 걸려도 막는다.
    public record KnownProgram(
        string DisplayName,
        string ExecutableName,
        KnownProgramKind Kind,
        string Aliases = "",
        string OriginalName = "")
    {
        // 적지 않았으면 실행 파일 이름과 같다고 본다. 대부분 그렇다.
        public string EffectiveOriginalName =>
            OriginalName.Length > 0 ? OriginalName : ExecutableName;
    }

    public static class KnownPrograms
    {
        // 금지 목록 기본값 — 생성형 AI.
        //
        // 웹으로만 쓰는 것(브라우저 안의 ChatGPT·Gemini 등)은 여기서 막지 못한다.
        // 프로세스가 브라우저이기 때문이다. 그쪽은 네트워크 감시가 도메인으로 막는다.
        // 그래서 이 표에는 설치해서 켜는 앱과 명령줄 도구만 넣는다.
        //
        // Cursor 와 Windsurf 는 코드 편집기지만 AI 가 코드를 대신 써 주므로 금지로 둔다.
        // 개발 도구라고 허용에 넣으면 시험에서 AI 를 그대로 쓰게 된다.
        //
        // Gemini CLI 와 Codex CLI 는 넣지 않는다. npm 으로 깔면 node.exe 가 스크립트를
        // 돌리는 방식이라(2026-09-14 npm 패키지 정보에서 확인) gemini.exe 같은 프로세스가
        // 생기지 않는다. 이름을 넣어 두면 막히는 것처럼 보이기만 한다.
        // 그쪽은 학생 PC 의 네트워크 감시가 API 주소로 막는다(ExamMonitorService).
        public static readonly IReadOnlyList<KnownProgram> Ai = new List<KnownProgram>
        {
            new("ChatGPT",           "ChatGPT.exe",    KnownProgramKind.Ai, "챗지피티 챗GPT 지피티 오픈AI OpenAI GPT"),
            new("Claude",            "Claude.exe",     KnownProgramKind.Ai, "클로드 앤트로픽 Anthropic 클로드코드 ClaudeCode"),
            new("Microsoft Copilot", "mscopilot.exe",  KnownProgramKind.Ai, "코파일럿 마이크로소프트"),
            new("GitHub Copilot CLI", "copilot.exe",   KnownProgramKind.Ai, "코파일럿 깃허브 GitHub"),
            new("Perplexity",        "Perplexity.exe", KnownProgramKind.Ai, "퍼플렉시티"),
            new("Cursor",            "Cursor.exe",     KnownProgramKind.Ai, "커서 AI편집기"),
            new("Windsurf",          "Windsurf.exe",   KnownProgramKind.Ai, "윈드서프 AI편집기"),
            new("뤼튼",              "wrtn.exe",       KnownProgramKind.Ai, "뤼튼 wrtn"),
        };

        // 금지 목록 기본값 — 제품명 키워드.
        //
        // 위 표는 실행 파일 이름이라 철자가 하나만 틀려도 걸리지 않는다. 실제로
        // Microsoft Copilot 은 Copilot.exe 가 아니라 mscopilot.exe 였다(2026-09-14 설치본에서 확인).
        // 그래서 학생 PC 는 실행 파일에 적힌 제품명·설명 안에 이 단어가 들어 있는지도 본다.
        // "Copilot" 한 단어로 mscopilot.exe, copilotapphost.exe, GitHub Copilot CLI 가 함께 걸린다.
        //
        // 흔한 단어는 넣지 않는다. 포함 여부로 보므로 "Code" 같은 단어는 관계없는
        // 프로그램까지 끈다(개발 PC 실행 파일 2,538개 중 41개가 걸렸다).
        // Copilot 외에는 설치본으로 확인하지 못했지만, 상표명이라 제품명에 그대로 들어간다고 본다.
        public static readonly IReadOnlyList<string> AiProductKeywords = new[]
        {
            "ChatGPT", "Claude", "Copilot", "Perplexity", "Cursor", "Windsurf", "Gemini", "wrtn", "뤼튼",
        };

        // 허용 목록 기본값 — 시험에서 실제로 쓰는 개발 도구.
        //
        // Visual Studio Code 는 Electron 으로 만들어져 원래 이름이 electron.exe 다.
        // (2026-09-09 설치본에서 확인) 이름만 넣으면 허용으로 인정되지 않으므로
        // 두 이름을 함께 넣는다. 다른 편집기가 덩달아 허용되지는 않는다 —
        // 허용은 실행 파일 이름까지 목록에 있어야 하기 때문이다.
        public static readonly IReadOnlyList<KnownProgram> DevTools = new List<KnownProgram>
        {
            new("Visual Studio",      "devenv.exe",         KnownProgramKind.DevTool, "비주얼스튜디오 VS 비스"),
            new("Visual Studio Code", "Code.exe",           KnownProgramKind.DevTool, "VSCode 브이에스코드 코드 비주얼스튜디오코드 에디터", "electron.exe"),
            new("Eclipse",            "eclipse.exe",        KnownProgramKind.DevTool, "이클립스"),
            new("IntelliJ IDEA",      "idea64.exe",         KnownProgramKind.DevTool, "인텔리제이 인텔리J 젯브레인 JetBrains"),
            new("PyCharm",            "pycharm64.exe",      KnownProgramKind.DevTool, "파이참 젯브레인"),
            new("Android Studio",     "studio64.exe",       KnownProgramKind.DevTool, "안드로이드스튜디오 안드로이드"),
            new("Dev-C++",            "devcpp.exe",         KnownProgramKind.DevTool, "데브씨 devc"),
            new("Notepad++",          "notepad++.exe",      KnownProgramKind.DevTool, "노트패드 메모장"),
            new("Sublime Text",       "sublime_text.exe",   KnownProgramKind.DevTool, "서브라임"),
            new("MySQL Workbench",    "MySQLWorkbench.exe", KnownProgramKind.DevTool, "마이에스큐엘 워크벤치 DB"),
        };

        // 선택창에 늘 보여 줄 윈도우 기본 앱.
        //
        // 선택창은 C:\Windows 안의 프로그램을 숨긴다. 윈도우가 알아서 돌리는 구성요소가
        // 목록을 덮어 헷갈리기 때문이다. 그중 교수가 허용·금지를 정할 만한 것만 여기 적어
        // 따로 보여 준다. 기본값으로 넣지는 않는다.
        //
        // 실행 파일 이름과 원래 이름은 2026-09-14 Windows 11 설치본에서 확인했다.
        // 메모장·그림판·캡처 도구(Store 판)는 버전 정보가 아예 없다. 대신 Microsoft Store 패키지
        // 폴더에 설치돼 있어, 학생 PC 의 감시가 설치 위치로 신원을 확인한다(ProcessMonitor 의
        // IsMicrosoftStorePackage). 그래서 실행 파일 이름 하나만으로 허용 목록에 넣을 수 있다.
        public static readonly IReadOnlyList<KnownProgram> WindowsApps = new List<KnownProgram>
        {
            new("메모장",             "Notepad.exe",         KnownProgramKind.WindowsApp, "Notepad 노트패드"),
            new("계산기",             "CalculatorApp.exe",   KnownProgramKind.WindowsApp, "Calculator calc"),
            new("그림판",             "mspaint.exe",         KnownProgramKind.WindowsApp, "Paint 페인트"),
            new("캡처 도구",          "SnippingTool.exe",    KnownProgramKind.WindowsApp, "Snipping Tool 스크린샷 화면캡처"),
            new("명령 프롬프트",      "cmd.exe",             KnownProgramKind.WindowsApp, "cmd 커맨드 콘솔"),
            new("Windows PowerShell", "powershell.exe",      KnownProgramKind.WindowsApp, "파워셸 파워쉘"),
            new("터미널",             "WindowsTerminal.exe", KnownProgramKind.WindowsApp, "Windows Terminal 윈도우터미널"),
            new("작업 관리자",        "Taskmgr.exe",         KnownProgramKind.WindowsApp, "Task Manager 작업관리자"),
            new("원격 데스크톱 연결", "mstsc.exe",           KnownProgramKind.WindowsApp, "Remote Desktop 원격 mstsc"),
        };

        public static IEnumerable<KnownProgram> All()
        {
            foreach (var p in Ai) yield return p;
            foreach (var p in DevTools) yield return p;
            foreach (var p in WindowsApps) yield return p;
        }
    }
}
