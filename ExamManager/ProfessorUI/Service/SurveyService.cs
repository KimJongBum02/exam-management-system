using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using NetworkLib;
using ProfessorUI.Model;

namespace ProfessorUI.Service
{
    // 설문 한 세션(대개 수업 한 번)을 맡는다. 정답 없이 학생이 O·X 중 무엇을 골랐는지만 모은다.
    // 채점하는 수업 확인 퀴즈는 QuizService 가 맡는다.
    //
    // 문제를 내고, 학생 응답을 받아 화면에 보여 준다.
    // 설문은 기록이 필요 없어 파일로 남기지 않는다 (수업 확인 퀴즈만 엑셀로 남긴다).
    public class SurveyService
    {
        public static SurveyService Instance { get; } = new SurveyService();

        private bool _started;

        private SurveyService() { }

        // 최근에 낸 문제가 앞에 온다.
        public ObservableCollection<SurveyRound> Rounds { get; } = new();

        public SurveyRound? CurrentRound => Rounds.FirstOrDefault();

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
        public bool Ask(string question)
        {
            if (string.IsNullOrWhiteSpace(question)) return false;

            var targets = StudentStore.Instance.Students.Where(s => s.IsConnected).ToList();
            if (targets.Count == 0) return false;

            var round = new SurveyRound
            {
                QuizId = Guid.NewGuid().ToString(),
                Question = question.Trim(),
                AskedAt = DateTime.Now.ToString("HH:mm:ss"),
            };

            // 학번순으로 채워 둔다. 응답이 들어와도 줄 위치가 바뀌지 않아 눈으로 좇기 쉽다.
            foreach (var student in targets.OrderBy(s => s.StudentId))
            {
                round.Responses.Add(new SurveyResponse
                {
                    StudentId = student.StudentId,
                    StudentName = student.Name,
                });
            }

            NetworkService.Instance.Broadcast(
                PacketType.QuizQuestion,
                QuizQuestionPayload.Encode(round.QuizId, round.Question));

            Rounds.Insert(0, round);
            return true;
        }

        // 화면의 기록을 비운다.
        public void ClearSession() => Rounds.Clear();

        // 학생별 누적. 문제를 낸 순서와 상관없이 학번순으로 돌려준다.
        public List<SurveyStudentTally> BuildTally()
        {
            var byStudent = new Dictionary<string, SurveyStudentTally>();

            foreach (var round in Rounds)
            {
                foreach (var response in round.Responses)
                {
                    if (!byStudent.TryGetValue(response.StudentId, out var tally))
                    {
                        tally = new SurveyStudentTally
                        {
                            StudentId = response.StudentId,
                            StudentName = response.StudentName,
                        };
                        byStudent[response.StudentId] = tally;
                    }

                    tally.AskedCount++;
                    if (response.HasAnswered) tally.AnsweredCount++;
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

                ResponseReceived?.Invoke(student);
            }
        }

        private static void PostToUi(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted) return;
            dispatcher.BeginInvoke(action);
        }
    }
}
