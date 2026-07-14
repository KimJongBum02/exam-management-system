#pragma once

#ifdef NETWORKCONTROL_EXPORTS
#define NC_API __declspec(dllexport)
#else
#define NC_API __declspec(dllimport)
#endif

extern "C" {

    // 허가되지 않은 도메인 조회 감지 콜백
    // domain: 감지된 도메인 (예: "chatgpt.com")
    typedef void(__stdcall* DetectCallback)(const wchar_t* domain);

    // 감시 대상 도메인 설정 ("chatgpt.com|openai.com|gemini.google.com" 형식)
    NC_API bool __stdcall NC_SetTargetDomains(const wchar_t* domains);

    // 상위 DNS(업스트림) 설정 — 조회를 전달할 실제 DNS 서버 IP
    // (예: "8.8.8.8", 또는 원래 PC의 DNS 를 권장). 미설정 시 기본 8.8.8.8
    NC_API void __stdcall NC_SetUpstream(const wchar_t* upstreamIp);

    // 콜백 등록 (호출자가 알림 전송 담당)
    NC_API void __stdcall NC_RegisterCallback(DetectCallback callback);

    // 감시 시작/중지
    NC_API bool __stdcall NC_Start();
    NC_API void __stdcall NC_Stop();

}  // extern "C"