using NetworkLib;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ProfessorUI.Service
{
    // 강의실 PC 에 설치된 프로그램 목록. 학생 PC 가 로그인할 때 보내 온다(InstalledProgramsReport).
    //
    // 파일로 저장해 둔다. 목록을 짜는 것은 보통 학생이 접속하기 전(시험 준비 1단계)이라,
    // 받은 그 순간에만 들고 있으면 정작 필요할 때 비어 있다. 강의실 PC 는 대개 같은 설치 상태여서
    // 한 번 받아 두면 다음 시험에도 그대로 쓸 수 있다.
    //
    // 합치기만 하고 지우지 않는다. 여러 강의실에서 받은 것이 쌓여도 선택창의 후보가 늘 뿐 해가 없다.
    public static class ClassroomPrograms
    {
        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ExamManager", "classroom-programs.json");

        // 네트워크 수신 스레드와 선택창이 목록을 만드는 배경 스레드가 함께 쓴다.
        private static readonly object Gate = new();

        // 처음 쓸 때 파일에서 읽는다. 실행 파일 이름으로 찾는다.
        private static Dictionary<string, InstalledProgram>? _byExe;

        public static IReadOnlyList<InstalledProgram> All()
        {
            lock (Gate) return Loaded().Values.ToList();
        }

        public static void Merge(IEnumerable<InstalledProgram> programs)
        {
            lock (Gate)
            {
                var byExe = Loaded();
                bool changed = false;

                foreach (var program in programs)
                {
                    // 같은 설치 상태의 PC 수십 대가 같은 목록을 보내므로, 달라진 것이 있을 때만 저장한다.
                    if (byExe.TryGetValue(program.ExecutableName, out var known) && known == program) continue;

                    byExe[program.ExecutableName] = program;
                    changed = true;
                }

                if (changed) Save(byExe);
            }
        }

        private static Dictionary<string, InstalledProgram> Loaded()
        {
            if (_byExe != null) return _byExe;

            _byExe = new Dictionary<string, InstalledProgram>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(FilePath))
                    foreach (var program in JsonSerializer.Deserialize<List<InstalledProgram>>(File.ReadAllText(FilePath)) ?? new())
                        if (!string.IsNullOrEmpty(program.ExecutableName))
                            _byExe[program.ExecutableName] = program;
            }
            catch (Exception ex)
            {
                // 파일이 깨졌으면 빈 목록에서 다시 쌓는다. 선택창의 다른 후보는 그대로 쓸 수 있다.
                Debug.WriteLine($"강의실 프로그램 목록을 읽지 못했습니다: {ex.Message}");
            }
            return _byExe;
        }

        private static void Save(Dictionary<string, InstalledProgram> byExe)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath,
                    JsonSerializer.Serialize(byExe.Values.ToList(), new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                // 저장을 못 해도 이번 실행 동안은 들고 있으므로 선택창에는 보인다.
                Debug.WriteLine($"강의실 프로그램 목록을 저장하지 못했습니다: {ex.Message}");
            }
        }
    }
}
