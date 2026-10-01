using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using NetworkLib;
using ProfessorUI.Model;

namespace ProfessorUI.Service
{
    // 수업 확인 퀴즈 한 세션(대개 수업 한 번)을 맡는다.
    //
    // 여러 문제를 한 번에 내고, 학생이 [답안 제출]로 보낸 답안을 채점해 기록하고,
    // 퀴즈 한 번마다 엑셀 파일 하나("0928_퀴즈1.xlsx", "0928_퀴즈2.xlsx" …)로 남긴다.
    // 정답 없는 설문은 SurveyService 가 맡고, 파일로 남기지 않는다.
    public class QuizService
    {
        public static QuizService Instance { get; } = new QuizService();

        // 퀴즈 기록을 모아 둘 폴더. 걷은 답안처럼 바탕화면에 둬서 교수가 바로 찾게 한다.
        public static string SessionFolder { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "퀴즈");

        private bool _started;

        // 답안이 들어오면 잠깐 기다렸다가 그 퀴즈 파일만 다시 쓴다(Save 참고).
        private readonly HashSet<QuizRound> _unsavedRounds = new();
        private System.Windows.Threading.DispatcherTimer? _saveTimer;

        private QuizService() { }

        // 최근에 낸 퀴즈가 앞에 온다.
        public ObservableCollection<QuizRound> Rounds { get; } = new();

        public QuizRound? CurrentRound => Rounds.FirstOrDefault();

        // 답안이 하나 들어올 때마다 알린다 (학번). 화면 갱신용.
        public event Action<string>? SubmissionReceived;

        // 앱 시작 시 한 번 호출 — 학생 답안 구독만 해 둔다.
        public void Start()
        {
            if (_started) return;
            _started = true;

            NetworkService.Instance.PacketReceived += OnPacketReceived;

            // 화면 스레드에서 만들어야 저장도 화면 스레드에서 돈다. 답안 기록을 바꾸는 쪽과 같은 스레드라 겹치지 않는다.
            _saveTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _saveTimer.Tick += (_, _) => SaveUnsaved();
        }

        // 퀴즈를 낸다. 출제 시점에 접속해 있는 학생만 대상이 된다.
        // 보낸 학생이 한 명도 없으면 아무 것도 하지 않고 false 를 돌려준다.
        public bool Ask(List<QuizQuestion> questions)
        {
            if (questions.Count == 0) return false;

            var targets = StudentStore.Instance.Students.Where(s => s.IsConnected).ToList();
            if (targets.Count == 0) return false;

            var round = new QuizRound
            {
                QuizId = Guid.NewGuid().ToString(),
                AskedAt = DateTime.Now.ToString("HH:mm:ss"),
                Title = NextTitle(),
                Questions = questions,
            };

            // 학번순으로 채워 둔다. 답안이 들어와도 줄 위치가 바뀌지 않아 눈으로 좇기 쉽다.
            foreach (var student in targets.OrderBy(s => s.StudentId))
            {
                round.Submissions.Add(new QuizSubmission
                {
                    StudentId = student.StudentId,
                    StudentName = student.Name,
                });
            }

            NetworkService.Instance.Broadcast(
                PacketType.QuizQuestion,
                ClassQuizPayload.Encode(round.QuizId,
                    questions.Select(q => (q.Text, (IReadOnlyList<string>)q.Options)).ToList()));

            // 새 퀴즈를 내는 때가 앞 퀴즈의 답안이 다 모인 때다. 앞 퀴즈 파일을 마무리하고 새 파일을 만든다.
            Rounds.Insert(0, round);
            Save();
            return true;
        }

        // 그날 몇 번째 퀴즈인지 정한다. 폴더에 이미 있는 "0928_퀴즈N" 중 가장 큰 N 다음 번호다.
        // 프로그램을 다시 켜도 번호가 이어지고, 앞 파일을 덮어쓰지 않는다.
        private string NextTitle()
        {
            string prefix = $"{DateTime.Now:MMdd}_퀴즈";
            int last = Rounds.Select(r => NumberOf(r.Title)).DefaultIfEmpty(0).Max();

            if (Directory.Exists(SessionFolder))
            {
                foreach (string file in Directory.GetFiles(SessionFolder, prefix + "*.xlsx"))
                    last = Math.Max(last, NumberOf(Path.GetFileNameWithoutExtension(file)));
            }
            return prefix + (last + 1);

            // "0928_퀴즈12" 와 "0928_퀴즈12_사본" 모두 12 로 읽는다. 오늘 것이 아니면 0.
            int NumberOf(string name)
            {
                if (!name.StartsWith(prefix)) return 0;
                string digits = new string(name.Substring(prefix.Length).TakeWhile(char.IsDigit).ToArray());
                return int.TryParse(digits, out int n) ? n : 0;
            }
        }

        // 화면의 기록을 비운다. 파일은 퀴즈마다 이미 따로 저장돼 있으므로, 비우기 전에 한 번 더 저장만 한다.
        // 저장하지 못했으면 비우지 않고 false — 비우면 마지막 답안이 어디에도 남지 않는다.
        public bool ClearSession()
        {
            if (!Save()) return false;
            Rounds.Clear();
            return true;
        }

        // 학생 답안 수신. 네이티브 스레드에서 올라오므로 화면은 건드리지 않고 UI 스레드로 넘긴다.
        private void OnPacketReceived(string sessionId, string studentId, string studentName,
                                      PacketType type, IntPtr payload, uint payloadLen)
        {
            if (type != PacketType.QuizAnswer) return;

            if (!QuizAnswerPayload.TryDecode(payload, payloadLen,
                                             out string quizId, out string payloadStudentId,
                                             out _, out string[] answers))
                return;

            // 학번은 서버가 로그인 때 등록한 값을 먼저 믿는다.
            // 학생이 보낸 값은 비어 있을 수 있어 보조로만 쓴다.
            string who = !string.IsNullOrEmpty(studentId) ? studentId : payloadStudentId;
            if (string.IsNullOrEmpty(who)) return;

            PostToUi(() => Record(quizId, who));
            return;

            void Record(string id, string student)
            {
                var round = Rounds.FirstOrDefault(r => r.QuizId == id);
                if (round == null) return;   // 설문의 응답이거나 이미 지운 세션의 답안이면 버린다

                var submission = round.Submissions.FirstOrDefault(s => s.StudentId == student);
                if (submission == null) return; // 출제 시점에 없던 학생이면 세지 않는다

                // 학생 화면은 제출 뒤 답을 바꿀 수 없다. 혹시 다시 와도 처음 것만 인정한다.
                if (submission.HasSubmitted) return;

                submission.Submit(answers, round.Questions);

                SubmissionReceived?.Invoke(student);

                // 마지막 답안이 들어오고 2초가 지나면 쓴다. 다음 답안이 오면 다시 2초를 기다린다.
                _unsavedRounds.Add(round);
                _saveTimer?.Stop();
                _saveTimer?.Start();
            }
        }

        private void SaveUnsaved()
        {
            _saveTimer?.Stop();
            foreach (var round in _unsavedRounds) SaveRound(round);
            _unsavedRounds.Clear();
        }

        private static void PostToUi(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted) return;
            dispatcher.BeginInvoke(action);
        }

        // 퀴즈마다 자기 파일에 다시 쓴다 — 퀴즈를 낼 때, 기록을 비울 때, 퀴즈 화면·프로그램을 닫을 때는 모든 퀴즈를,
        // 답안이 들어왔을 때는 그 퀴즈만(마지막 답안 뒤 2초).
        // xlsx 는 한 줄만 덧붙일 수 없는 형식이라 파일 전체를 다시 쓴다. 답안마다 바로 쓰면 학생 수십 명이
        // 한꺼번에 낼 때 화면이 끊기므로, 몰려 들어오는 동안은 기다렸다가 한 번에 쓴다.
        //
        // 교수가 파일을 엑셀로 열어 두면 그 파일에는 쓸 수 없다. 그때는 "0928_퀴즈3_사본" 으로 저장하고
        // 그 퀴즈는 이후로도 사본에 쓴다. 모두 저장했으면 true.
        public bool Save()
        {
            _saveTimer?.Stop();
            _unsavedRounds.Clear();

            bool saved = true;
            foreach (var round in Rounds)
                if (!SaveRound(round)) saved = false;
            return saved;
        }

        private bool SaveRound(QuizRound round)
        {
            if (round.FilePath.Length == 0)
                round.FilePath = Path.Combine(SessionFolder, round.Title + ".xlsx");

            if (TrySave(round, round.FilePath)) return true;

            string copy = Path.Combine(SessionFolder, round.Title + "_사본.xlsx");
            if (copy != round.FilePath && TrySave(round, copy))
            {
                round.FilePath = copy;
                return true;
            }
            return false;
        }

        // 기록 저장에 실패해도 수업은 계속돼야 하므로 예외를 밖으로 내지 않는다.
        private static bool TrySave(QuizRound round, string path)
        {
            try
            {
                ExcelReport.SaveQuizRound(round, path);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
