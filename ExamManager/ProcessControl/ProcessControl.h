#pragma once

#ifdef PROCESSCONTROL_EXPORTS
#define PC_API __declspec(dllexport)
#else
#define PC_API __declspec(dllimport)
#endif

extern "C" {

    // 부정행위 감지 콜백
    // type: 0 = 블랙리스트 실행, 1 = 화이트리스트 종료,
    //       2 = 목록에 없는 신규 프로세스 (감시 시작 이후 실행됨, 종료하지 않고 알리기만 함)
    typedef void(__stdcall* CheatCallback)(int type, const wchar_t* processName);

    // 리스트 설정 ("notepad.exe|chrome.exe" 형식)
    PC_API bool __stdcall PM_SetBlacklist(const wchar_t* blacklist);
    PC_API bool __stdcall PM_SetWhitelist(const wchar_t* whitelist);

    // 콜백 등록 (호출자가 팝업/처리 담당)
    PC_API void __stdcall PM_RegisterCallback(CheatCallback callback);

    // 감시 시작/중지
    PC_API bool __stdcall PM_Start();
    PC_API void __stdcall PM_Stop();

}  // extern "C"