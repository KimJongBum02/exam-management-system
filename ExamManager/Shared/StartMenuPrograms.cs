using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using NetworkLib;

namespace ExamManager.Shared
{
    // 시작 메뉴 바로가기에서 이 PC 에 설치된 프로그램을 찾는다.
    //
    // 교수 PC 의 프로그램 선택창(ProgramCatalog)과 학생 PC 의 설치 목록 보고(InstalledProgramReport)가
    // 함께 쓴다. 두 벌로 두지 않는 이유는 원래 이름을 읽는 방식이 학생 PC 의 감시(ProcessMonitor)와
    // 똑같아야 하기 때문이다. 한쪽만 고치면 선택창에서 고른 허용 항목을 감시가 인정하지 않는다.
    public static class StartMenuPrograms
    {
        // 바로가기를 읽는 WScript.Shell 은 STA 스레드에서 부르는 것이 안전하다.
        // Path 는 실행 파일의 전체 경로다. 교수 선택창이 아이콘을 뽑을 때 쓴다.
        public static IEnumerable<(InstalledProgram Program, string Path)> Read()
        {
            string[] roots =
            {
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            };

            object? shell = CreateShell();
            if (shell == null) yield break;

            foreach (string root in roots)
            {
                if (!Directory.Exists(root)) continue;

                // 시작 메뉴 밑에는 접근이 막힌 옛 폴더("...\Start Menu\프로그램")가 있다.
                // 그냥 훑으면 거기서 예외가 나 뒤쪽 진짜 목록까지 통째로 놓치므로
                // 못 읽는 폴더는 건너뛰도록 일러 준다.
                var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };

                string[] links;
                try { links = Directory.GetFiles(root, "*.lnk", options); }
                catch { continue; }

                foreach (string link in links)
                {
                    string name = Path.GetFileNameWithoutExtension(link);
                    if (IsUninstaller(name)) continue;   // "카카오톡 제거" 같은 항목은 감시 대상이 아니다

                    string? target = ResolveShortcut(shell, link);
                    if (target == null || !target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;

                    string exe = Path.GetFileName(target);
                    if (IsUninstaller(exe)) continue;
                    if (IsWindowsComponent(target)) continue;   // 고를 만한 것은 KnownPrograms.WindowsApps 로 보여 준다

                    var program = new InstalledProgram(
                        DisplayName: name,
                        ExecutableName: exe,
                        OriginalName: ReadOriginalName(target),
                        // 바로가기 이름이 늘 교수가 떠올리는 이름은 아니다.
                        // ("VS Code" 로 만들어 둔 바로가기를 "Visual Studio Code" 로 찾는 식)
                        // 파일에 박힌 설명을 검색어로 쓰도록 함께 담는다. 보이는 이름은 그대로 둔다 —
                        // 시작 메뉴에 뜨는 이름이 교수가 실제로 보아 온 이름이기 때문이다.
                        Description: ReadDescription(target, name));

                    yield return (program, target);
                }
            }
        }

        // 바로가기를 읽는 데 WScript.Shell 을 쓴다. 참조를 늘리지 않으려고 늦은 바인딩으로 부른다.
        public static object? CreateShell()
        {
            try
            {
                Type? type = Type.GetTypeFromProgID("WScript.Shell");
                return type == null ? null : Activator.CreateInstance(type);
            }
            catch { return null; }
        }

        public static string? ResolveShortcut(object shell, string linkPath)
        {
            try
            {
                object? link = shell.GetType().InvokeMember(
                    "CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { linkPath });
                if (link == null) return null;

                object? target = link.GetType().InvokeMember(
                    "TargetPath", BindingFlags.GetProperty, null, link, null);

                string? path = target as string;
                return string.IsNullOrWhiteSpace(path) ? null : path;
            }
            catch { return null; }
        }

        // 실행 파일에 박힌 설명(FileDescription). "Visual Studio Code" 처럼
        // 사람이 부르는 이름이 들어 있다. 원래 이름(OriginalFilename)과는 다르다 —
        // VS Code 는 원래 이름이 electron.exe 지만 설명은 Visual Studio Code 다.
        //
        // 이미 보이는 이름과 같으면 검색어로 더할 필요가 없어 비워 둔다.
        private static string ReadDescription(string path, string displayName)
        {
            try
            {
                string? description = FileVersionInfo.GetVersionInfo(path).FileDescription;
                if (string.IsNullOrWhiteSpace(description)) return string.Empty;
                if (string.Equals(description.Trim(), displayName, StringComparison.OrdinalIgnoreCase))
                    return string.Empty;

                return description.Trim();
            }
            catch
            {
                // 버전 정보가 없거나 못 읽는 파일이 흔하다. 검색어가 하나 줄 뿐이다.
                return string.Empty;
            }
        }

        // C:\Windows 안의 실행 파일인지. 이런 것은 목록에 올리지 않는다.
        private static readonly string WindowsDir =
            Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\') + "\\";

        public static bool IsWindowsComponent(string path)
            => path.StartsWith(WindowsDir, StringComparison.OrdinalIgnoreCase);

        // Microsoft 가 배포한 Store 앱의 실행 파일인지(<Program Files>\WindowsApps\<패키지 폴더>_8wekyb3d8bbwe\...).
        // 규칙과 이유는 ProcessMonitor.cpp 의 IsMicrosoftStorePackage 에 적었다. 두 곳이 같아야 한다.
        private static readonly string WindowsAppsDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps") + "\\";

        private static bool IsMicrosoftStorePackage(string path)
        {
            if (!path.StartsWith(WindowsAppsDir, StringComparison.OrdinalIgnoreCase)) return false;

            int end = path.IndexOf('\\', WindowsAppsDir.Length);
            return end > 0 && path.Substring(WindowsAppsDir.Length, end - WindowsAppsDir.Length)
                                  .EndsWith("_8wekyb3d8bbwe", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsUninstaller(string text)
            => text.Contains("제거") || text.Contains("uninstall", StringComparison.OrdinalIgnoreCase);

        // ── 원래 이름 ─────────────────────────────────────────────────
        // 학생 PC의 ProcessMonitor 가 읽는 것과 같은 값이어야 목록이 맞아떨어진다.
        // 그래서 .NET 의 FileVersionInfo 대신 네이티브와 같은 방식으로 직접 읽는다.
        //
        // FILE_VER_GET_NEUTRAL 이 반드시 필요하다. 이 플래그가 없으면 다국어 리소스로
        // 리다이렉션되어 "ping.exe.mui" 같은 값이 나오고, 네이티브 쪽 값과 달라진다.

        public static string ReadOriginalName(string path)
        {
            // 버전 정보가 없는 Microsoft Store 앱(메모장·그림판·캡처 도구)은 설치 위치가 신원을 보증하므로
            // 실행 파일 이름을 대신 쓴다. 학생 PC 의 ProcessMonitor 도 같은 규칙으로 허용 판정을 한다.
            string original = ReadVersionOriginalName(path);
            return original.Length == 0 && IsMicrosoftStorePackage(path) ? Path.GetFileName(path) : original;
        }

        private static string ReadVersionOriginalName(string path)
        {
            if (path.Length == 0) return string.Empty;

            try
            {
                int size = GetFileVersionInfoSizeEx(FileVerGetNeutral, path, out _);
                if (size == 0) return string.Empty;

                var block = new byte[size];
                if (!GetFileVersionInfoEx(FileVerGetNeutral, path, 0, (uint)size, block))
                    return string.Empty;

                // 문자열은 언어별로 나뉘어 있어 번역 테이블을 먼저 읽어야 조회 경로를 만들 수 있다.
                if (!VerQueryValue(block, @"\VarFileInfo\Translation", out IntPtr table, out uint tableLen))
                    return string.Empty;

                for (int i = 0; i + 4 <= tableLen; i += 4)
                {
                    ushort language = (ushort)Marshal.ReadInt16(table, i);
                    ushort codePage = (ushort)Marshal.ReadInt16(table, i + 2);
                    string entry = $@"\StringFileInfo\{language:x4}{codePage:x4}\OriginalFilename";

                    if (VerQueryValue(block, entry, out IntPtr value, out uint valueLen) && valueLen > 0)
                        return (Marshal.PtrToStringUni(value, (int)valueLen) ?? "").TrimEnd('\0');
                }
            }
            catch { }

            return string.Empty;
        }

        private const uint FileVerGetNeutral = 0x02;

        [DllImport("version.dll", CharSet = CharSet.Unicode, EntryPoint = "GetFileVersionInfoSizeExW")]
        private static extern int GetFileVersionInfoSizeEx(uint flags, string file, out uint handle);

        [DllImport("version.dll", CharSet = CharSet.Unicode, EntryPoint = "GetFileVersionInfoExW")]
        private static extern bool GetFileVersionInfoEx(uint flags, string file, uint handle, uint length, byte[] data);

        [DllImport("version.dll", CharSet = CharSet.Unicode, EntryPoint = "VerQueryValueW")]
        private static extern bool VerQueryValue(byte[] block, string subBlock, out IntPtr buffer, out uint length);
    }
}
