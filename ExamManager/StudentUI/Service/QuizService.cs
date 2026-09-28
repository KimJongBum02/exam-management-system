using System;
using System.Collections.Generic;
using System.Windows;
using NetworkLib;

namespace StudentUI.Service
{
    // 교수가 낸 문제를 받아 알리고, 학생이 고른 답을 되돌려 보낸다.
    //   설문(OX 한 문제)          — 고르는 즉시 보낸다 (QuizWindow)
    //   수업 확인 퀴즈(여러 문제) — 학생이 [답안 제출]을 눌러야 보낸다 (ClassQuizWindow)
    //
    // 이 기능은 시험 중뿐 아니라 수업 중 이해도 확인에도 쓴다.
    // 그래서 시험 화면에 묶지 않고, 어느 화면에 있든 문제가 오면 뜨도록 앱에서 구독한다.
    public class QuizService
    {
        public static QuizService Instance { get; } = new QuizService();

        private bool _started;
        private string _currentQuizId = string.Empty;

        private QuizService() { }

        // 로그인할 때 넘겨받는다. 응답에 학번·이름을 실어 보내기 위한 것이다.
        //
        // 이 서비스는 앱이 뜰 때 만들어져 로그인보다 먼저 존재하므로, 로그인 화면이
        // 여기로 건네주는 수밖에 없다. 값을 베껴 두면 나중에 학생 정보가 바뀌었을 때
        // 어긋나므로 객체를 그대로 참조한다.
        //
        // 교수 쪽은 접속 세션으로도 누구인지 알 수 있어, 이 값이 비어 있어도
        // 응답이 버려지지는 않는다.
        public Model.Student? Student { get; set; }

        // 새 문제가 도착했음을 알린다 (문제 본문).
        public event Action<string>? QuestionReceived;

        // 수업 확인 퀴즈가 도착했음을 알린다 (퀴즈 번호, 문제와 보기 — 보기가 없으면 OX).
        public event Action<string, List<(string Question, string[] Options)>>? ClassQuizReceived;

        // 앱 시작 시 한 번 호출 — 구독만 해 둔다.
        public void Start()
        {
            if (_started) return;
            _started = true;

            NetworkService.Instance.PacketReceived += OnPacketReceived;
        }

        // 지금 화면에 떠 있는 문제에 답한다. 한 문제에 한 번만 보낸다.
        public void Answer(bool answer)
        {
            if (string.IsNullOrEmpty(_currentQuizId)) return;

            NetworkService.Instance.SendPacket(
                PacketType.QuizAnswer,
                QuizAnswerPayload.Encode(_currentQuizId,
                                         Student?.StudentNumber ?? string.Empty,
                                         Student?.StudentName ?? string.Empty,
                                         answer));

            // 같은 문제에 두 번 답하지 못하게 비운다.
            // 교수 쪽도 먼저 온 응답만 인정하므로 양쪽 판단이 같다.
            _currentQuizId = string.Empty;
        }

        // 수업 확인 퀴즈 답안을 보낸다. 문제 순서대로의 답이고, 풀지 않은 문제는 빈 칸이다.
        // 한 번만 보낸다 — 창이 제출 뒤 답을 잠그고, 교수 쪽도 처음 온 답안만 인정한다.
        // 교수 PC 와 끊겨 있으면 보내지 않고 false — 창이 답을 잠그지 않아 다시 누를 수 있다.
        public bool SubmitClassQuiz(string quizId, IEnumerable<string> answers)
        {
            if (!NetworkService.Instance.IsConnected) return false;

            NetworkService.Instance.SendPacket(
                PacketType.QuizAnswer,
                QuizAnswerPayload.Encode(quizId,
                                         Student?.StudentNumber ?? string.Empty,
                                         Student?.StudentName ?? string.Empty,
                                         answers));
            return true;
        }

        private void OnPacketReceived(PacketType type, IntPtr payload, uint payloadLen)
        {
            if (type != PacketType.QuizQuestion) return;

            // 네이티브 수신 스레드에서 올라오므로 화면 작업은 UI 스레드로 넘긴다.
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted) return;

            if (QuizQuestionPayload.TryDecode(payload, payloadLen,
                                              out string quizId, out string question, out _))
            {
                _currentQuizId = quizId;
                dispatcher.BeginInvoke(() => QuestionReceived?.Invoke(question));
                return;
            }

            if (ClassQuizPayload.TryDecode(payload, payloadLen, out string classQuizId, out var questions))
                dispatcher.BeginInvoke(() => ClassQuizReceived?.Invoke(classQuizId, questions));
        }
    }
}
