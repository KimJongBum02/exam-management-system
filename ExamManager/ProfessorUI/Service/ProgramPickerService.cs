using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using ExamManager.Shared;
using ProfessorUI.Model;

namespace ProfessorUI.Service
{
    public static class ProgramPickerService
    {
        // 부를 때마다 새로 훑는다. 1초 남짓이면 끝나고,
        // 모아 두면 그 사이에 켜고 끈 프로그램의 "실행 중" 표시가 실제와 어긋난다.
        // 프로그램 선택창이 쓸 목록을 만든다.
        //   already      — 이미 감시 목록에 들어 있는 실행 파일들("추가됨"으로 표시)
        //   forWhiteList — 허용 목록을 고르는 중인지. 허용은 원래 이름까지 있어야 인정된다.
        //
        // 바로가기를 읽는 WScript.Shell 은 STA 스레드에서 부르는 것이 안전하다.
        // 그렇다고 화면 스레드에서 돌리면 목록을 훑는 동안 창이 멈춘 것처럼 보여 전용 스레드를 하나 띄운다.
        public static async Task<List<ProgramEntry>> LoadForPickerAsync(
            ISet<string> already, bool forWhiteList, ISet<string> alreadyChosen)
        {
            List<ProgramEntry> programs = await LoadOnStaThread();

            // 다시 읽으면 목록이 새 객체로 바뀌므로, 이미 골라 둔 것에 표시를 다시 입힌다.
            foreach (var program in programs)
            {
                program.IsAlreadyAdded = already.Contains(program.ExecutableName);
                program.CannotBeAllowed = forWhiteList && program.OriginalName.Length == 0;
                program.IsChosen = alreadyChosen.Contains(program.ExecutableName);
            }

            // 아직 목록에 없어 지금 고를 수 있는 것을 위로 올린다.
            // 이미 추가됐거나(추가됨) 넣을 수 없는(허용 불가) 항목은 손댈 수 없으니 아래로 내린다.
            // OrderBy 는 안정 정렬이라 같은 그룹 안에서는 원래의 가나다 순서가 그대로 유지된다.
            return programs.OrderBy(p => (p.IsAlreadyAdded || p.CannotBeAllowed) ? 1 : 0).ToList();
        }

        private static Task<List<ProgramEntry>> LoadOnStaThread()
        {
            var done = new TaskCompletionSource<List<ProgramEntry>>();

            var worker = new Thread(() =>
            {
                try { done.SetResult(Load().ToList()); }
                catch (Exception) { done.SetResult(new List<ProgramEntry>()); }
            });
            worker.SetApartmentState(ApartmentState.STA);
            worker.IsBackground = true;
            worker.Start();

            return done.Task;
        }

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
            foreach (var program in ClassroomProgramStore.All())
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
                };
            }
        }
    }
}
