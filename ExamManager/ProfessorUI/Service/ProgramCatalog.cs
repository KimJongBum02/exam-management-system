using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ExamManager.Shared;

namespace ProfessorUI.Service
{
    public enum ProgramSource
    {
        Installed,   // 시작 메뉴 바로가기에서 찾음
        Running,     // 지금 실행 중인 프로세스
        Picked,      // 교수가 파일 선택창에서 직접 고름
        Known,       // 사전에 적어 둔 이름 — 이 PC 에는 없다
        BuiltIn,     // 윈도우 기본 앱 — C:\Windows 안이라 위 경로에서는 숨기고 표로 보여 준다
        Classroom,   // 강의실 PC 에 설치된 것 — 학생 PC 가 보내 온 목록(ClassroomPrograms)
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

        // 디지털 서명 게시자(예: "Google LLC"). 서명이 없으면 빈 문자열.
        // 직접 찾아보기로 파일을 고를 때만 읽는다. 게시자 차단 규칙("서명:")을 제안하는 데 쓴다.
        public string Publisher { get; init; } = string.Empty;

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
            ProgramSource.Classroom => "강의실 PC",
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

            // 강의실 PC 에 설치된 것. 학생 PC 가 로그인할 때 보내 와 저장해 둔 목록이다.
            // 이 PC 에도 있으면 실제 경로와 아이콘을 가진 위의 것을 남긴다.
            foreach (var program in ClassroomPrograms.All())
            {
                if (byExe.ContainsKey(program.ExecutableName)) continue;

                byExe[program.ExecutableName] = new ProgramEntry
                {
                    DisplayName = program.DisplayName,
                    ExecutableName = program.ExecutableName,
                    OriginalName = program.OriginalName,
                    Aliases = program.Description,
                    Source = ProgramSource.Classroom,
                };
            }

            // 이 PC 에 없는 것도 고를 수 있어야 한다.
            //
            // 목록을 짜는 곳은 교수 PC 이고 감시가 도는 곳은 학생 PC 라, 학생 PC 에만
            // 깔린 프로그램은 학생이 한 번도 접속하지 않았으면 위 경로로는 나타나지 않는다.
            // 사전에 적어 둔 이름을 후보로 함께 올려 이름만으로도 고를 수 있게 한다.
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
        // 읽는 방식은 학생 PC 와 같아야 해서 StartMenuPrograms 에 한 벌만 둔다.

        private static IEnumerable<ProgramEntry> ReadStartMenu()
        {
            foreach (var (program, path) in StartMenuPrograms.Read())
            {
                yield return new ProgramEntry
                {
                    DisplayName = program.DisplayName,
                    ExecutableName = program.ExecutableName,
                    ExecutablePath = path,
                    OriginalName = program.OriginalName,
                    Aliases = program.Description,
                    Source = ProgramSource.Installed,
                    Icon = LoadIcon(path),
                };
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

                object? shell = StartMenuPrograms.CreateShell();
                if (shell == null) return null;

                target = StartMenuPrograms.ResolveShortcut(shell, path);
                if (target == null || !target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    return null;
            }

            return new ProgramEntry
            {
                DisplayName = Path.GetFileNameWithoutExtension(target),
                ExecutableName = Path.GetFileName(target),
                ExecutablePath = target,
                OriginalName = StartMenuPrograms.ReadOriginalName(target),
                Publisher = ReadPublisher(target),
                Source = ProgramSource.Picked,
                Icon = LoadIcon(target),
            };
        }

        // 실행 파일의 디지털 서명 게시자(CN)를 읽는다. 서명이 없으면 빈 문자열.
        // 교수 쪽은 게시자를 '추출'만 하고, 서명이 진짜 유효한지는 학생 PC 의 감시가 검증한다.
        // (학생 PC 의 QueryPublisher 가 CertGetNameString(SimpleDisplay)으로 얻는 값과 같은 CN 이다)
        private static string ReadPublisher(string path)
        {
            try
            {
                using var cert = new System.Security.Cryptography.X509Certificates.X509Certificate2(
                    System.Security.Cryptography.X509Certificates.X509Certificate.CreateFromSignedFile(path));
                return cert.GetNameInfo(
                    System.Security.Cryptography.X509Certificates.X509NameType.SimpleName, false) ?? string.Empty;
            }
            catch
            {
                // 서명이 없거나 못 읽는 파일이 흔하다. 그럴 땐 게시자 규칙을 제안하지 않을 뿐이다.
                return string.Empty;
            }
        }

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
                if (StartMenuPrograms.IsWindowsComponent(path)) continue;

                yield return new ProgramEntry
                {
                    DisplayName = string.IsNullOrWhiteSpace(description) ? p.ProcessName : description!,
                    ExecutableName = Path.GetFileName(path),
                    ExecutablePath = path,
                    OriginalName = StartMenuPrograms.ReadOriginalName(path),
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
