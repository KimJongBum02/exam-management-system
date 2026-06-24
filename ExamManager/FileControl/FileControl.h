#pragma once

#ifdef FILECONTROL_EXPORTS
#define FC_API __declspec(dllexport)
#else
#define FC_API __declspec(dllimport)
#endif

extern "C" {

    // [교수 PC] 폴더(또는 파일)를 7za로 압축 + AES-256 암호화하여 단일 .7z 생성
    // 반환값: 7za.exe 종료 코드 (0 = 성공)
    //   -1 = 인자 누락, -2 = 프로세스 생성 실패(7za.exe 경로 확인)
    FC_API int __stdcall FC_CompressEncrypt(
        const wchar_t* sevenZaPath,    // 7za.exe 전체 경로
        const wchar_t* sourcePath,     // 압축할 폴더 또는 파일 경로
        const wchar_t* outputArchive,  // 출력 .7z 경로
        const wchar_t* password);      // 암호 (빈 문자열 금지)

    // [학생 PC] .7z를 복호화 + 압축 해제하여 지정 폴더에 원본 복원
    // 반환값: 7za.exe 종료 코드 (0 = 성공)
    //   -1 = 인자 누락, -2 = 프로세스 생성 실패(7za.exe 경로 확인)
    FC_API int __stdcall FC_ExtractDecrypt(
        const wchar_t* sevenZaPath,    // 7za.exe 전체 경로
        const wchar_t* archivePath,    // 입력 .7z 경로
        const wchar_t* outputFolder,   // 해제 대상 폴더
        const wchar_t* password);      // 암호

}  // extern "C"
