using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using NetworkLib;

namespace ProfessorUI.Service
{
    // 진행 중이던 시험을 파일로 기억해, 교수 프로그램이 도중에 꺼져도 다시 켜면 이어서 운영하게 한다.
    //
    // 기억하지 않으면 다시 켠 프로그램은 단계가 '시험 전'으로 돌아가, 시험 종료·답안 수집 메뉴가 열리지 않는다.
    // 학생들은 자동 재연결로 붙어 시험을 계속 치고 있는데 교수는 답안을 걷을 방법이 없어진다.
    // 묶음 경로와 암호도 메모리에만 있어 재배포도 못 한다.
    //
    // 학생 쪽 같은 이름의 저장소(StudentUI/Service/ExamSessionStore)와 짝이다. 형식과 유효기간을 맞췄다.
    public static class ExamSessionStore
    {
        // 이보다 오래된 기록은 버린다. 지난 수업의 기록으로 다음 수업이 시험 중으로 열리지 않게 한다.
        private static readonly TimeSpan MaxAge = TimeSpan.FromHours(3);

        private static readonly string SessionPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ExamManager", "professor-session.json");

        public sealed class ProfessorSession
        {
            public ExamPhase Phase { get; set; }              // 시험 단계 (메뉴가 열리고 닫히는 기준)
            public DateTime? EndedAt { get; set; }            // 시험을 끝낸 시각
            public string? ExamId { get; set; }               // 묶음 이름 (T 번호)
            public string? PackagePath { get; set; }          // 배포한 묶음 경로
            public string? Password { get; set; }             // 그 묶음의 암호 — 답안을 걷을 때 같은 값을 쓴다
            public bool IsFilePrepared { get; set; }
            public bool IsFileDistributed { get; set; }
            public DateTime SavedAtUtc { get; set; }
        }

        // 앱 시작 때 한 번 부른다. 기록이 있으면 되살리고, 그 뒤로는 상태가 바뀔 때마다 남긴다.
        public static void Start()
        {
            Restore();

            ExamState.StateChanged += Save;
            SendFileState.StateChanged += Save;
        }

        // 되살릴 것이 있으면 되살린다. 되살렸으면 true.
        private static bool Restore()
        {
            ProfessorSession? session = Load();
            if (session == null) return false;

            // 묶음 파일이 지워졌으면 배포·수집에 쓸 수 없다. 단계만 되살리고 묶음은 다시 만들게 한다.
            bool packageAlive = !string.IsNullOrEmpty(session.PackagePath) && File.Exists(session.PackagePath);

            SendFileState.ExamId = session.ExamId;
            SendFileState.PackagePath = packageAlive ? session.PackagePath : null;
            SendFileState.Password = packageAlive ? session.Password : null;
            SendFileState.IsFileDistributed = session.IsFileDistributed;
            SendFileState.IsFilePrepared = packageAlive && session.IsFilePrepared;

            ExamState.Restore(session.Phase, session.EndedAt);
            return true;
        }

        private static ProfessorSession? Load()
        {
            try
            {
                if (!File.Exists(SessionPath)) return null;

                var session = JsonSerializer.Deserialize<ProfessorSession>(File.ReadAllText(SessionPath));

                // 시험 전 단계로 남은 기록은 되살릴 것이 없다.
                if (session != null && session.Phase > ExamPhase.Waiting &&
                    DateTime.UtcNow - session.SavedAtUtc <= MaxAge)
                    return session;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"시험 기록을 읽지 못했습니다: {ex.Message}");
            }

            Clear();
            return null;
        }

        // 단계나 묶음 상태가 바뀔 때마다 불린다.
        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SessionPath)!);
                File.WriteAllText(SessionPath, JsonSerializer.Serialize(new ProfessorSession
                {
                    Phase = ExamState.CurrentPhase,
                    EndedAt = ExamState.EndedAt,
                    ExamId = SendFileState.ExamId,
                    PackagePath = SendFileState.PackagePath,
                    Password = SendFileState.Password,
                    IsFilePrepared = SendFileState.IsFilePrepared,
                    IsFileDistributed = SendFileState.IsFileDistributed,
                    SavedAtUtc = DateTime.UtcNow,
                }));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"시험 기록을 남기지 못했습니다: {ex.Message}");
            }
        }

        public static void Clear()
        {
            try { File.Delete(SessionPath); } catch { }
        }
    }
}
