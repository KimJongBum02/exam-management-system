using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using NetworkLib;

namespace ProfessorUI.Service
{
    // 한 학생의 한 문제에 대한 응답.
    //
    // 출제 시점에 접속해 있던 학생 전원을 미리 만들어 두고 Answer 를 비워 둔다.
    // 그래야 "안 낸 사람"과 "그때 없던 사람"이 섞이지 않는다 —
    // 수업 태도를 보는 것이 목적이라 이 둘을 구분하지 못하면 쓸모가 없다.
    public class QuizResponse : INotifyPropertyChanged
    {
        public string StudentId { get; init; } = string.Empty;
        public string StudentName { get; init; } = string.Empty;

        private bool? _answer;
        public bool? Answer
        {
            get => _answer;
            set { _answer = value; NotifyAll(); }
        }

        private string _respondedAt = "-";
        public string RespondedAt
        {
            get => _respondedAt;
            set { _respondedAt = value; NotifyAll(); }
        }

        // 이 응답이 붙어 있는 문제의 정답. 정오 판정에 쓴다.
        [JsonIgnore]
        public bool CorrectAnswer { get; set; }

        [JsonIgnore] public bool HasAnswered => _answer.HasValue;
        [JsonIgnore] public bool IsCorrect => _answer.HasValue && _answer.Value == CorrectAnswer;

        // 화면에 그대로 나갈 문구. 색을 쓰지 않는 화면이라 기호로 구분한다.
        [JsonIgnore] public string AnswerText => _answer == true ? "O" : _answer == false ? "X" : "−";
        [JsonIgnore] public string ResultText => !_answer.HasValue ? "− 미응답" : IsCorrect ? "✓ 정답" : "✕ 오답";

        // 오답 → 미응답 → 정답 순. 교수가 봐야 할 학생이 위로 오게 하기 위한 값이다.
        //
        // 목록을 이 값으로 늘 정렬하지는 않는다. 응답이 하나씩 들어오는 동안 줄이 계속
        // 튀면 오히려 읽기 어렵기 때문이다. 화면에서 정렬 여부를 고르게 두고, 이 값만 제공한다.
        [JsonIgnore] public int SortRank => !_answer.HasValue ? 1 : IsCorrect ? 2 : 0;

        private void NotifyAll() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    // 문제 하나를 낸 기록.
    public class QuizRound : INotifyPropertyChanged
    {
        public string QuizId { get; init; } = string.Empty;
        public string Question { get; init; } = string.Empty;
        public bool CorrectAnswer { get; init; }
        public string AskedAt { get; init; } = string.Empty;

        public ObservableCollection<QuizResponse> Responses { get; init; } = new();

        [JsonIgnore] public int TargetCount => Responses.Count;
        [JsonIgnore] public int AnsweredCount => Responses.Count(r => r.HasAnswered);
        [JsonIgnore] public int CorrectCount => Responses.Count(r => r.IsCorrect);
        [JsonIgnore] public int WrongCount => Responses.Count(r => r.HasAnswered && !r.IsCorrect);
        [JsonIgnore] public int MissedCount => Responses.Count(r => !r.HasAnswered);

        [JsonIgnore] public string CorrectAnswerText => CorrectAnswer ? "O" : "X";
        [JsonIgnore] public string SummaryText => $"정답 {CorrectCount} · 오답 {WrongCount} · 미응답 {MissedCount}";

        // 응답이 하나 들어올 때마다 위 집계를 다시 읽게 한다.
        public void NotifyCounts() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    // 학생 한 명의 누적. 수업 태도 평가에 실제로 쓰이는 것은 이 표다.
    public class QuizStudentTally
    {
        public string StudentId { get; init; } = string.Empty;
        public string StudentName { get; init; } = string.Empty;
        public int AskedCount { get; set; }      // 그 학생이 대상이었던 문제 수
        public int AnsweredCount { get; set; }
        public int CorrectCount { get; set; }
        public int MissedCount => AskedCount - AnsweredCount;
    }

    // OX 퀴즈 한 세션(대개 수업 한 번)을 맡는다.
    //
    // 문제를 내고, 학생 응답을 받아 기록하고, 세션 기록을 파일로 남긴다.
    // 화면은 붙이지 않는다 — 교수 UI 가 정해진 뒤에 연결한다.
    public class QuizSessionService
    {
        public static QuizSessionService Instance { get; } = new QuizSessionService();

        // 세션 기록을 모아 둘 폴더. 걷은 답안과 같은 자리에 둬서 교수가 한곳만 보면 되게 한다.
        public static string SessionFolder { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "ExamManager", "QuizSessions");

        private readonly string _sessionFileName = $"quiz_{DateTime.Now:yyyyMMdd_HHmmss}.json";
        private bool _started;

        private QuizSessionService() { }

        // 최근에 낸 문제가 앞에 온다.
        public ObservableCollection<QuizRound> Rounds { get; } = new();

        public QuizRound? CurrentRound => Rounds.FirstOrDefault();

        // 응답이 하나 들어올 때마다 알린다 (학번). 화면 갱신용.
        public event Action<string>? ResponseReceived;

        // 앱 시작 시 한 번 호출 — 학생 응답 구독만 해 둔다.
        public void Start()
        {
            if (_started) return;
            _started = true;

            NetworkService.Instance.PacketReceived += OnPacketReceived;
        }

        // 문제를 낸다. 출제 시점에 접속해 있는 학생만 대상이 된다.
        // 보낸 학생이 한 명도 없으면 아무 것도 하지 않고 false 를 돌려준다.
        public bool Ask(string question, bool correctAnswer)
        {
            if (string.IsNullOrWhiteSpace(question)) return false;

            var targets = StudentStore.Instance.Students.Where(s => s.IsConnected).ToList();
            if (targets.Count == 0) return false;

            var round = new QuizRound
            {
                QuizId = Guid.NewGuid().ToString(),
                Question = question.Trim(),
                CorrectAnswer = correctAnswer,
                AskedAt = DateTime.Now.ToString("HH:mm:ss"),
            };

            // 학번순으로 채워 둔다. 응답이 들어와도 줄 위치가 바뀌지 않아 눈으로 좇기 쉽다.
            foreach (var student in targets.OrderBy(s => s.StudentId))
            {
                round.Responses.Add(new QuizResponse
                {
                    StudentId = student.StudentId,
                    StudentName = student.Name,
                    CorrectAnswer = correctAnswer,
                });
            }

            NetworkService.Instance.Broadcast(
                PacketType.QuizQuestion,
                QuizQuestionPayload.Encode(round.QuizId, round.Question));

            Rounds.Insert(0, round);
            Save();
            return true;
        }

        // 세션을 비운다. 파일은 그대로 두므로 기록이 사라지지는 않는다.
        public void ClearSession()
        {
            Rounds.Clear();
            Save();
        }

        // 학생별 누적. 문제를 낸 순서와 상관없이 학번순으로 돌려준다.
        public List<QuizStudentTally> BuildTally()
        {
            var byStudent = new Dictionary<string, QuizStudentTally>();

            foreach (var round in Rounds)
            {
                foreach (var response in round.Responses)
                {
                    if (!byStudent.TryGetValue(response.StudentId, out var tally))
                    {
                        tally = new QuizStudentTally
                        {
                            StudentId = response.StudentId,
                            StudentName = response.StudentName,
                        };
                        byStudent[response.StudentId] = tally;
                    }

                    tally.AskedCount++;
                    if (response.HasAnswered) tally.AnsweredCount++;
                    if (response.IsCorrect) tally.CorrectCount++;
                }
            }

            return byStudent.Values.OrderBy(t => t.StudentId).ToList();
        }

        // 학생 응답 수신. 네이티브 스레드에서 올라오므로 화면은 건드리지 않고 UI 스레드로 넘긴다.
        private void OnPacketReceived(string sessionId, string studentId, string studentName,
                                      PacketType type, IntPtr payload, uint payloadLen)
        {
            if (type != PacketType.QuizAnswer) return;

            if (!QuizAnswerPayload.TryDecode(payload, payloadLen,
                                             out string quizId, out string payloadStudentId,
                                             out _, out bool answer))
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
                if (round == null) return;   // 이미 지운 세션의 응답이면 버린다

                var response = round.Responses.FirstOrDefault(r => r.StudentId == student);
                if (response == null) return; // 출제 시점에 없던 학생이면 세지 않는다

                // 먼저 낸 응답만 인정한다. 다시 보내도 바뀌지 않는다.
                if (response.HasAnswered) return;

                response.Answer = answer;
                response.RespondedAt = DateTime.Now.ToString("HH:mm:ss");
                round.NotifyCounts();

                Save();
                ResponseReceived?.Invoke(student);
            }
        }

        private static void PostToUi(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted) return;
            dispatcher.BeginInvoke(action);
        }

        // 응답 하나가 들어올 때마다 통째로 다시 쓴다.
        // 한 수업에 문제 몇 개, 학생 수십 명 규모라 이 정도로 충분하고,
        // 중간에 프로그램이 꺼져도 그때까지의 기록이 남는다.
        private void Save()
        {
            try
            {
                Directory.CreateDirectory(SessionFolder);
                string json = JsonSerializer.Serialize(Rounds,
                    new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(Path.Combine(SessionFolder, _sessionFileName), json);
            }
            catch
            {
                // 기록 저장에 실패해도 수업은 계속돼야 하므로 여기서 막지 않는다.
            }
        }
    }
}
