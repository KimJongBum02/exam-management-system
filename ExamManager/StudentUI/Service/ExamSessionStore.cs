using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace StudentUI.Service
{
    // 진행 중인 시험을 파일로 기억해, 학생 프로그램이 도중에 꺼져도 다시 켜면 이어서 치르게 한다.
    //
    // 기억하지 않으면 다시 켠 프로그램은 어느 폴더가 이번 시험인지, 무슨 암호로 답안을 묶는지 모른다.
    // 그러면 교수가 재배포해야 하는데, 재배포는 학생이 고친 문제 파일을 원본으로 덮어쓴다.
    // 이 기록은 차단을 붙잡아 두지 않는다. 차단은 켤 때 풀고, 같은 학번으로 다시 로그인하면 새로 건다.
    //
    // 시험이 시작되어 압축이 풀린 뒤에만 남긴다. 파일을 받자마자 암호를 적어 두면
    // 시험 시작 전에 그 암호로 문제를 먼저 열어 볼 수 있기 때문이다.
    // 답안 제출에 성공하면 지운다.
    public static class ExamSessionStore
    {
        // 이보다 오래된 기록은 버린다. 끝난 시험의 기록으로 다음 수업에서 엉뚱하게
        // 이어받지 않게 한다. (시험 50분 + 답안을 걷는 시간 여유)
        private static readonly TimeSpan MaxAge = TimeSpan.FromHours(3);

        // 프로그램이 죽어도 남아 있어야 하므로 앱 폴더가 아닌 곳에 둔다.
        private static readonly string SessionPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ExamManager", "exam-session.json");

        public sealed class ExamSession
        {
            public string StudentNumber { get; set; } = string.Empty;   // 이 시험을 치던 학생. 다른 학번으로는 이어받지 못한다
            public string StudentName { get; set; } = string.Empty;
            public string ArchiveName { get; set; } = string.Empty;     // 시험 폴더 이름 (받은 묶음 이름)
            public string Password { get; set; } = string.Empty;        // 답안을 묶을 암호
            public List<string> DeliveredFiles { get; set; } = new();   // 배포로 받은 파일 (재배포 때 지울 대상)
            public List<string> Whitelist { get; set; } = new();        // 감시 목록 — 다시 켜면 교수가 다시 보내지 않는다
            public List<string> Blacklist { get; set; } = new();
            public DateTime StartedAtUtc { get; set; }                  // 남은 시간을 이어 세는 데 쓴다
            public bool ExamEnded { get; set; }                         // 교수가 시험을 끝냈으면 감시는 다시 켜지 않는다
        }

        // 기록이 있고 아직 유효하면 돌려준다. 오래됐거나 깨진 기록은 여기서 지운다.
        public static ExamSession? Load()
        {
            try
            {
                if (!File.Exists(SessionPath)) return null;

                var session = JsonSerializer.Deserialize<ExamSession>(File.ReadAllText(SessionPath));
                if (session != null && session.ArchiveName.Length > 0 &&
                    DateTime.UtcNow - session.StartedAtUtc <= MaxAge)
                    return session;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"이어 받기 기록을 읽지 못했습니다: {ex.Message}");
            }

            Clear();
            return null;
        }

        public static void Save(ExamSession session)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SessionPath)!);
                File.WriteAllText(SessionPath, JsonSerializer.Serialize(session));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"이어 받기 기록을 남기지 못했습니다: {ex.Message}");
            }
        }

        public static void Clear()
        {
            try { File.Delete(SessionPath); } catch { }
        }
    }
}
