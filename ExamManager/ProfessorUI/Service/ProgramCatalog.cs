using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ProfessorUI.Service
{
    public enum ProgramSource
    {
        Installed,   // 시작 메뉴 바로가기에서 찾음
        Running,     // 지금 실행 중인 프로세스
        Picked,      // 교수가 파일 선택창에서 직접 고름
        Known,       // 사전에 적어 둔 이름 — 이 PC 에는 없다
        BuiltIn,     // 윈도우 기본 앱 — C:\Windows 안이라 위 경로에서는 숨기고 표로 보여 준다
    }

    // 감시 목록에 넣을 후보 하나.
    //
    // 교수는 "카카오톡"으로 찾고, 학생 PC의 감시는 "KakaoTalk.exe"로 한다.
    // 그래서 보여 주는 이름과 실제로 저장할 이름을 따로 들고 있는다.
    public class ProgramEntry : INotifyPropertyChanged
    {
        public string DisplayName { get; init; } = string.Empty;
        public string ExecutableName { get; init; } = string.Empty;
        public string ExecutablePath { get; init; } = string.Empty;
        public ProgramSource Source { get; set; }
        public ImageSource? Icon { get; init; }

        // 실행 파일에 박혀 있는 원래 이름. 파일 이름을 바꿔도 따라 변하지 않는다.
        // 학생 PC의 감시는 허용 목록을 판정할 때 이 이름까지 요구하므로
        // (허용된 이름으로 위장하는 것을 막기 위함) 함께 목록에 넣어야 한다.
        // 버전 리소스가 없는 실행 파일도 흔해서 빈 문자열이 정상 결과다.
        public string OriginalName { get; init; } = string.Empty;

        // 이 프로그램을 찾을 때 쓰일 다른 이름들("챗지피티", "VSCode" 처럼).
        // 파일에 박힌 설명과 사전의 별칭이 여기 함께 담긴다.
        public string Aliases { get; set; } = string.Empty;

        // 실행 파일 이름과 원래 이름이 달라 목록에 두 개를 넣어야 하는 경우
        public bool HasDistinctOriginalName =>
            OriginalName.Length > 0 &&
            !string.Equals(Path.GetFileNameWithoutExtension(OriginalName),
                           Path.GetFileNameWithoutExtension(ExecutableName),
                           StringComparison.OrdinalIgnoreCase);

        public string SourceText => Source switch
        {
            ProgramSource.Running => "실행 중",
            ProgramSource.Picked => "직접 선택",
            ProgramSource.Known => "미설치",
            ProgramSource.BuiltIn => "윈도우 기본",
            _ => "설치됨",
        };

        // 이미 감시 목록에 들어 있는 것. 다시 고를 수 없게 하고 그렇다고 알려 준다.
        // 조용히 무시하면 눌리지 않은 것처럼 보이기 때문이다.
        public bool IsAlreadyAdded { get; set; }

        // 허용 목록을 고르는 중인데 원래 이름이 없는 경우.
        // 넣어도 감시가 허용으로 인정하지 않으므로 미리 알려 준다.
        public bool CannotBeAllowed { get; set; }

        public string StatusText =>
            IsAlreadyAdded ? "추가됨" :
            CannotBeAllowed ? "허용 불가" : string.Empty;

        // 선택창에서 고른 상태. 목록의 체크 표시가 이 값을 따라간다.
        private bool _isChosen;
        public bool IsChosen
        {
            get => _isChosen;
            set
            {
                if (_isChosen == value) return;
                _isChosen = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChosen)));
            }
        }

        // 견주기 전에 공백을 모두 없앤다.
        private static string Squeeze(string text) =>
            new string(text.Where(c => !char.IsWhiteSpace(c)).ToArray());

        public event PropertyChangedEventHandler? PropertyChanged;

        // 한글 이름과 실행 파일명 어느 쪽으로 쳐도 걸리게 한다.
        // "카카오"로도 "kakao"로도 찾을 수 있어야 하기 때문이다.
        //
        // 띄어쓰기는 무시한다. "비주얼 스튜디오"와 "비주얼스튜디오"를 다르게 볼 이유가 없고,
        // 프로그램 이름의 띄어쓰기는 사람마다 다르게 기억한다.
        public bool Matches(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword)) return true;

            string k = Squeeze(keyword);
            return Squeeze(DisplayName).Contains(k, StringComparison.OrdinalIgnoreCase)
                || Squeeze(ExecutableName).Contains(k, StringComparison.OrdinalIgnoreCase)
                || Squeeze(Aliases).Contains(k, StringComparison.OrdinalIgnoreCase);
        }
    }

    // 감시 목록에 넣을 프로그램 후보를 모은다.
    //
    // 한글 이름을 별도 표로 관리하지 않는다. 시작 메뉴 바로가기의 이름이 곧
    // 사용자가 부르는 이름이고("카카오톡" → KakaoTalk.exe), 그 안에 실행 파일 경로가
    // 들어 있어 Windows 가 이미 매핑을 갖고 있는 셈이기 때문이다.
    // 표를 두면 새 프로그램이 나올 때마다 우리가 채워 넣어야 한다.
    public static class ProgramCatalog
    {
        // 부를 때마다 새로 훑는다. 1초 남짓이면 끝나고,
        // 모아 두면 그 사이에 켜고 끈 프로그램의 "실행 중" 표시가 실제와 어긋난다.
        public static IReadOnlyList<ProgramEntry> Load()
        {
            var byExe = new Dictionary<string, ProgramEntry>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in ReadStartMenu())
                byExe[entry.ExecutableName] = entry;

            // 실행 중인 것은 확실히 그 이름으로 도는 프로세스라 더 믿을 만하다.
            // 같은 실행 파일이 이미 있으면 보기 좋은 설치 이름은 남기고 출처만 올린다.
            foreach (var entry in ReadRunning())
            {
                if (byExe.TryGetValue(entry.ExecutableName, out var found))
                    found.Source = ProgramSource.Running;
                else
                    byExe[entry.ExecutableName] = entry;
            }

            // 이 PC 에 없는 것도 고를 수 있어야 한다.
            //
            // 목록을 짜는 곳은 교수 PC 이고 감시가 도는 곳은 학생 PC 라, 학생 PC 에만
            // 깔린 프로그램은 위 두 경로로는 영영 나타나지 않는다. 사전에 적어 둔 이름을
            // 후보로 함께 올려 이름만으로도 고를 수 있게 한다.
            foreach (var known in KnownPrograms.All())
            {
                // 이미 설치돼 찾은 것은 실제 경로와 아이콘을 갖고 있으므로 덮지 않는다.
                // 다만 사전에 적어 둔 한글 이름과 별칭은 그쪽에 없으므로 보태 준다.
                // 이것을 빼먹으면 설치된 VS Code 를 "브이에스코드" 로 찾지 못하고,
                // 실행 중인 Store 메모장(버전 정보가 없어 "Notepad" 로만 잡힘)을 "메모장" 으로 찾지 못한다.
                if (byExe.TryGetValue(known.ExecutableName, out var installed))
                {
                    installed.Aliases = $"{installed.Aliases} {known.DisplayName} {known.Aliases}".Trim();
                    continue;
                }

                byExe[known.ExecutableName] = new ProgramEntry
                {
                    DisplayName = known.DisplayName,
                    ExecutableName = known.ExecutableName,
                    OriginalName = known.EffectiveOriginalName,
                    Aliases = known.Aliases,
                    Source = known.Kind == KnownProgramKind.WindowsApp ? ProgramSource.BuiltIn : ProgramSource.Known,
                };
            }

            // 현재 문화권(ko-KR) 기준이라 한글 가나다가 먼저, 그다음 영문 순으로 놓인다.
            return byExe.Values
                .OrderBy(e => e.DisplayName, StringComparer.CurrentCulture)
                .ToList();
        }

        // ── 시작 메뉴 바로가기 ────────────────────────────────────────

        private static IEnumerable<ProgramEntry> ReadStartMenu()
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

                    yield return new ProgramEntry
                    {
                        DisplayName = name,
                        ExecutableName = exe,
                        ExecutablePath = target,
                        OriginalName = ReadOriginalName(target),
                        // 바로가기 이름이 늘 교수가 떠올리는 이름은 아니다.
                        // ("VS Code" 로 만들어 둔 바로가기를 "Visual Studio Code" 로 찾는 식)
                        // 파일에 박힌 설명을 검색어로 더해 둔다. 보이는 이름은 그대로 둔다 —
                        // 시작 메뉴에 뜨는 이름이 교수가 실제로 보아 온 이름이기 때문이다.
                        Aliases = ReadDescription(target, name),
                        Source = ProgramSource.Installed,
                        Icon = LoadIcon(target),
                    };
                }
            }
        }

        // 파일 선택창에서 고른 것 하나를 실행 파일 이름으로 바꾼다.
        // 목록에 없는 프로그램을 넣는 길이며, 교수가 Program Files 를 뒤지지 않아도 되도록
        // 바탕화면·시작 메뉴의 바로가기(.lnk)를 골라도 대상 실행 파일을 풀어 준다.
        public static ProgramEntry? ResolvePickedFile(string path)
        {
            string? target = path;

            if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                if (!path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return null;

                object? shell = CreateShell();
                if (shell == null) return null;

                target = ResolveShortcut(shell, path);
                if (target == null || !target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    return null;
            }

            return new ProgramEntry
            {
                DisplayName = Path.GetFileNameWithoutExtension(target),
                ExecutableName = Path.GetFileName(target),
                ExecutablePath = target,
                OriginalName = ReadOriginalName(target),
                Source = ProgramSource.Picked,
                Icon = LoadIcon(target),
            };
        }

        // 바로가기를 읽는 데 WScript.Shell 을 쓴다. 참조를 늘리지 않으려고 늦은 바인딩으로 부른다.
        private static object? CreateShell()
        {
            try
            {
                Type? type = Type.GetTypeFromProgID("WScript.Shell");
                return type == null ? null : Activator.CreateInstance(type);
            }
            catch { return null; }
        }

        private static string? ResolveShortcut(object shell, string linkPath)
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

        private static bool IsWindowsComponent(string path)
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

        // ── 실행 중인 프로세스 ────────────────────────────────────────

        private static IEnumerable<ProgramEntry> ReadRunning()
        {
            Process[] all;
            try { all = Process.GetProcesses(); }
            catch { yield break; }

            foreach (var p in all)
            {
                string? path = null;
                string? description = null;
                try
                {
                    path = p.MainModule?.FileName;
                    description = p.MainModule?.FileVersionInfo.FileDescription;
                }
                catch
                {
                    // 권한이 모자라 못 읽는 시스템 프로세스가 있다. 그런 것은 건너뛴다.
                }
                if (string.IsNullOrEmpty(path)) continue;

                // 윈도우가 알아서 돌리는 구성요소(svchost, RuntimeBroker 등)는 목록을 덮기만 한다.
                // 교수가 고를 만한 윈도우 앱은 KnownPrograms.WindowsApps 로 따로 보여 준다.
                if (IsWindowsComponent(path)) continue;

                yield return new ProgramEntry
                {
                    DisplayName = string.IsNullOrWhiteSpace(description) ? p.ProcessName : description!,
                    ExecutableName = Path.GetFileName(path),
                    ExecutablePath = path,
                    OriginalName = ReadOriginalName(path),
                    Source = ProgramSource.Running,
                    Icon = LoadIcon(path),
                };
            }
        }

        // ── 아이콘 ────────────────────────────────────────────────────
        // 이름을 몰라도 아이콘으로 알아보는 경우가 많아 함께 보여 준다.
        // System.Drawing 을 끌어오지 않으려고 셸 API 로 직접 뽑는다.

        private static ImageSource? LoadIcon(string path)
        {
            IntPtr handle = IntPtr.Zero;
            try
            {
                var info = new SHFILEINFO();
                if (SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf(info), SHGFI_ICON | SHGFI_SMALLICON) == IntPtr.Zero)
                    return null;

                handle = info.hIcon;
                if (handle == IntPtr.Zero) return null;

                var image = Imaging.CreateBitmapSourceFromHIcon(
                    handle, System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                image.Freeze();   // 다른 스레드에서 만들어도 화면에서 쓸 수 있게 얼려 둔다
                return image;
            }
            catch { return null; }
            finally { if (handle != IntPtr.Zero) DestroyIcon(handle); }
        }

        // ── 원래 이름 ─────────────────────────────────────────────────
        // 학생 PC의 ProcessMonitor 가 읽는 것과 같은 값이어야 목록이 맞아떨어진다.
        // 그래서 .NET 의 FileVersionInfo 대신 네이티브와 같은 방식으로 직접 읽는다.
        //
        // FILE_VER_GET_NEUTRAL 이 반드시 필요하다. 이 플래그가 없으면 다국어 리소스로
        // 리다이렉션되어 "ping.exe.mui" 같은 값이 나오고, 네이티브 쪽 값과 달라진다.

        private static string ReadOriginalName(string path)
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

        private const uint SHGFI_ICON = 0x000000100;
        private const uint SHGFI_SMALLICON = 0x000000001;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SHGetFileInfo(string path, uint attributes,
                                                   ref SHFILEINFO info, uint size, uint flags);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);
    }
}
