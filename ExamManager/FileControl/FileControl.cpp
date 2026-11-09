#include "pch.h"
#include "FileControl.h"
#include <string>

// 경로/암호를 따옴표로 감싸 공백이 있어도 안전하게 명령줄 구성
static std::wstring Quote(const wchar_t* s)
{
    std::wstring q = L"\"";
    q += s;
    q += L"\"";
    return q;
}

// 7za.exe를 주어진 명령줄로 실행하고 종료 코드를 반환 (압축/해제 공용)
static int RunSevenZip(const std::wstring& commandLine)
{
    STARTUPINFOW si = { sizeof(si) };
    PROCESS_INFORMATION pi = {};

    // CreateProcessW는 lpCommandLine 버퍼를 수정할 수 있어 쓰기 가능한 복사본 필요
    std::wstring mutableCmd = commandLine;

    BOOL ok = CreateProcessW(
        nullptr,
        &mutableCmd[0],
        nullptr, nullptr, FALSE,
        CREATE_NO_WINDOW,   // 콘솔 창 깜빡임 방지
        nullptr, nullptr,
        &si, &pi);

    if (!ok)
        return -2;  // 7za.exe 경로 확인 필요

    WaitForSingleObject(pi.hProcess, INFINITE);

    DWORD exitCode = 0;
    GetExitCodeProcess(pi.hProcess, &exitCode);

    CloseHandle(pi.hProcess);
    CloseHandle(pi.hThread);

    return static_cast<int>(exitCode);
}

int __stdcall FC_CompressEncrypt(
    const wchar_t* sevenZaPath,
    const wchar_t* sourcePath,
    const wchar_t* outputArchive,
    const wchar_t* password)
{
    if (!sevenZaPath || !sourcePath || !outputArchive || !password)
        return -1;

    // 7za a -t7z -mx1 -mhe=on -p"암호" -y "출력.7z" "원본"
    //   a       : 아카이브에 추가
    //   -t7z    : 7z 포맷
    //   -mx1    : 압축 레벨 (기본값 -mx5 는 너무 느리다)
    //             133MB DLL 기준 실측: -mx5 = 59초/36.9MB, -mx1 = 2초/42.9MB.
    //             배포 대기 시간을 줄이는 쪽이 이득이라 속도를 택한다.
    //             (-mmt 멀티스레드는 이 조건에서 효과가 없어 넣지 않는다)
    //   -mhe=on : 헤더 암호화 (파일명까지 숨김)
    //   -p      : 암호 (AES-256)
    //   -y      : 모든 질문에 yes
    std::wstring cmd = Quote(sevenZaPath);
    cmd += L" a -t7z -mx1 -mhe=on -p";
    cmd += Quote(password);
    cmd += L" -y ";
    cmd += Quote(outputArchive);
    cmd += L" ";
    cmd += Quote(sourcePath);

    return RunSevenZip(cmd);
}

int __stdcall FC_ExtractDecrypt(
    const wchar_t* sevenZaPath,
    const wchar_t* archivePath,
    const wchar_t* outputFolder,
    const wchar_t* password)
{
    if (!sevenZaPath || !archivePath || !outputFolder || !password)
        return -1;

    // 7za x -p"암호" -o"출력폴더" -y "입력.7z"
    //   x   : 전체 경로 유지하며 해제
    //   -o  : 출력 폴더 (공백 없이 경로 붙임)
    std::wstring cmd = Quote(sevenZaPath);
    cmd += L" x -p";
    cmd += Quote(password);
    cmd += L" -o";
    cmd += Quote(outputFolder);
    cmd += L" -y ";
    cmd += Quote(archivePath);

    return RunSevenZip(cmd);
}