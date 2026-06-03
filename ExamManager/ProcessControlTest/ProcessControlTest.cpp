#include <iostream>
#include <string>
#include <Windows.h>
#include <io.h>        // 41, 42 line 인코딩 위해 사용
#include <fcntl.h>     // 41, 42 line 인코딩 위해 사용
#include "ProcessControl.h"

// 팝업 띄우는 헬퍼 (별도 스레드 — 감지 흐름 안 막게)
void ShowPopup(int type, const std::wstring& processName)
{
    std::wstring* msg = new std::wstring();
    if (type == 0)
        *msg = L"부정 행위 감지 !\n\n금지된 프로그램 실행: " + processName;
    else
        *msg = L"부정 행위 감지 !\n\n허용된 프로그램 종료: " + processName;

    CreateThread(NULL, 0, [](LPVOID p) -> DWORD {
        std::wstring* m = static_cast<std::wstring*>(p);
        MessageBoxW(NULL, m->c_str(), L"부정 행위 감지",
            MB_OK | MB_ICONWARNING | MB_TOPMOST);
        delete m;
        return 0;
        }, msg, 0, NULL);
}

void __stdcall OnCheat(int type, const wchar_t* processName)
{
    if (type == 0)
        std::wcout << L"\n[감지] 블랙리스트 실행: " << processName << std::endl;
    else
        std::wcout << L"\n[감지] 화이트리스트 종료: " << processName << std::endl;

    ShowPopup(type, processName);

    std::wcout << L"선택: ";
}

int main()
{
    // UTF-16으로 강제 인코딩인데, 이전에 콘솔 테스트할 때 한글 깨져서 넣어놨습니다.
    _setmode(_fileno(stdout), _O_U16TEXT);
    _setmode(_fileno(stdin), _O_U16TEXT);

    std::wcout << L"===== ProcessControl 테스트 =====" << std::endl;

    PM_RegisterCallback(OnCheat);

    PM_SetBlacklist(L"notepad.exe");
    std::wcout << L"\n블랙리스트: notepad.exe" << std::endl;
    std::wcout << L"  -> 메모장 실행하면 종료 + 경고" << std::endl;

    PM_SetWhitelist(L"mspaint.exe");
    std::wcout << L"\n화이트리스트: mspaint.exe (그림판)" << std::endl;
    std::wcout << L"  -> 그림판 켰다가 끄면 경고" << std::endl;

    std::wcout << L"\n감시 시작! (q 입력 시 종료)" << std::endl;
    PM_Start();

    std::wstring input;
    while (true)
    {
        std::wcout << L"선택: ";
        std::getline(std::wcin, input);
        if (input == L"q" || input == L"Q") break;
    }

    PM_Stop();
    std::wcout << L"테스트 종료" << std::endl;
    return 0;
}