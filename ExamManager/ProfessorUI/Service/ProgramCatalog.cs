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

        public event PropertyChangedEventHandler? PropertyChanged;

        // 한글 이름과 실행 파일명 어느 쪽으로 쳐도 걸리게 한다.
        // "카카오"로도 "kakao"로도 찾을 수 있어야 하기 때문이다.
        public bool Matches(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword)) return true;

            string k = keyword.Trim();
            return DisplayName.Contains(k, StringComparison.OrdinalIgnoreCase)
                || ExecutableName.Contains(k, StringComparison.OrdinalIgnoreCase);
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

                    yield return new ProgramEntry
                    {
                        DisplayName = name,
                        ExecutableName = exe,
                        ExecutablePath = target,
                        OriginalName = ReadOriginalName(target),
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
