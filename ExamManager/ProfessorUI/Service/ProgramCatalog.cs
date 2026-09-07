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

        public string SourceText => Source switch
        {
            ProgramSource.Running => "실행 중",
            ProgramSource.Picked => "직접 선택",
            _ => "설치됨",
        };

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
        private static List<ProgramEntry>? _cache;

        // 목록을 읽는다. 처음 한 번만 훑고 그 뒤로는 모아 둔 것을 준다.
        // 다시 읽으려면 refresh 를 준다 — 그 사이에 켜진 프로그램을 잡을 때 쓴다.
        public static IReadOnlyList<ProgramEntry> Load(bool refresh = false)
        {
            if (_cache != null && !refresh) return _cache;

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

            _cache = byExe.Values
                .OrderBy(e => e.DisplayName, StringComparer.CurrentCulture)
                .ToList();

            return _cache;
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
                        Source = ProgramSource.Installed,
                        Icon = LoadIcon(target),
                    };
                }
            }
        }

        // 파일 선택창에서 고른 것 하나를 실행 파일 이름으로 바꾼다.
        // 목록에 없는 프로그램을 넣는 길이며, 교수가 Program Files 를 뒤지지 않아도 되도록
        // 바탕화면·시작 메뉴의 바로가기(.lnk)를 골라도 대상 실행 파일을 풀어 준다.
        public static string? ResolveExecutableName(string path)
        {
            if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                return Path.GetFileName(path);

            if (!path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                return null;

            object? shell = CreateShell();
            if (shell == null) return null;

            string? target = ResolveShortcut(shell, path);
            if (target == null || !target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                return null;

            return Path.GetFileName(target);
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
