// 수업 확인 퀴즈 데이터: 낸 퀴즈 한 번(QuizRound) · 그 안의 문제(QuizQuestion)
// · 학생 한 명의 답안(QuizSubmission) · 문제별 집계(QuizQuestionStat) · 학생별 누적(QuizStudentTally)
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace ProfessorUI.Model
{
    // 낸 문제 하나. 출제한 순간의 내용을 그대로 붙잡아 둔다 — 작성 칸을 고쳐도 기록은 바뀌지 않는다.
    public class QuizQuestion
    {
        public int Number { get; init; }
        public string Text { get; init; } = string.Empty;

        // 비어 있으면 OX 문제다.
        public string[] Options { get; init; } = Array.Empty<string>();

        // "O"·"X" 또는 보기 번호("1"~). 학생이 보내는 답과 같은 모양이라 그대로 비교한다.
        public string CorrectAnswer { get; init; } = string.Empty;

        public bool IsOX => Options.Length == 0;
        public string CorrectText => IsOX ? CorrectAnswer : $"{CorrectAnswer}번";
    }

    // 한 학생의 답안 한 벌.
    //
    // 출제 시점에 접속해 있던 학생 전원을 미리 만들어 두고 비워 둔다.
    // 그래야 "안 낸 사람"과 "그때 없던 사람"이 섞이지 않는다.
    public class QuizSubmission
    {
        public string StudentId { get; init; } = string.Empty;
        public string StudentName { get; init; } = string.Empty;

        // 문제 순서대로의 답. 빈 칸은 풀지 않은 문제다. 제출 전에는 null 이다.
        public string[]? Answers { get; private set; }
        public string SubmittedAt { get; private set; } = "-";
        public int Score { get; private set; }
        public int Total { get; private set; }

        public bool HasSubmitted => Answers != null;
        public string ScoreText => HasSubmitted ? $"{Score} / {Total}" : "미제출";

        // 미제출 → 점수 낮은 순. 교수가 봐야 할 학생이 위로 오게 하기 위한 값이다.
        public int SortRank => HasSubmitted ? Score : -1;

        public void Submit(string[] answers, IReadOnlyList<QuizQuestion> questions)
        {
            // 문제 수보다 적게 오면 뒤를 빈 칸으로 채운다. 많이 오면 넘치는 것은 버린다.
            Answers = questions.Select((_, i) => i < answers.Length ? answers[i].Trim() : string.Empty).ToArray();
            SubmittedAt = DateTime.Now.ToString("HH:mm:ss");
            Total = questions.Count;
            Score = questions.Count(q => ResultOf(q) == Result.Correct);
        }

        public enum Result { NotSubmitted, Correct, Wrong, Blank }

        public Result ResultOf(QuizQuestion question)
        {
            if (Answers == null) return Result.NotSubmitted;
            string answer = Answers[question.Number - 1];
            return answer.Length == 0 ? Result.Blank
                 : answer == question.CorrectAnswer ? Result.Correct
                 : Result.Wrong;
        }

        // 엑셀에 적는 문구.
        public string AnswerTextOf(QuizQuestion question)
        {
            if (Answers == null) return "-";
            string answer = Answers[question.Number - 1];
            return answer.Length == 0 ? "-" : question.IsOX ? answer : $"{answer}번";
        }
    }

    // 문제 하나에 대한 집계. 제출한 학생만 센다 (미제출은 퀴즈 단위로 따로 센다).
    public class QuizQuestionStat
    {
        public QuizQuestionStat(QuizQuestion question, IEnumerable<QuizSubmission> submissions)
        {
            Question = question;
            foreach (var submission in submissions)
            {
                switch (submission.ResultOf(question))
                {
                    case QuizSubmission.Result.Correct: CorrectCount++; break;
                    case QuizSubmission.Result.Wrong:   WrongCount++;   break;
                    case QuizSubmission.Result.Blank:   BlankCount++;   break;
                }
            }
        }

        public QuizQuestion Question { get; }
        public string CorrectText => Question.CorrectText;
        public int CorrectCount { get; }
        public int WrongCount { get; }
        public int BlankCount { get; }

        public string RateText
        {
            get
            {
                int answered = CorrectCount + WrongCount + BlankCount;
                return answered == 0 ? "-" : $"{CorrectCount * 100 / answered}%";
            }
        }
    }

    // 퀴즈를 한 번 낸 기록.
    public class QuizRound
    {
        public string QuizId { get; init; } = string.Empty;
        public string AskedAt { get; init; } = string.Empty;

        // "0928_퀴즈3" 처럼 그날 몇 번째 퀴즈인지. 엑셀 파일 이름이자 화면 제목이다.
        public string Title { get; init; } = string.Empty;

        // 실제로 저장한 파일 경로. 파일이 엑셀로 열려 있어 다른 이름으로 저장했으면 그 이름이 남는다.
        public string FilePath { get; set; } = string.Empty;

        public List<QuizQuestion> Questions { get; init; } = new();
        public ObservableCollection<QuizSubmission> Submissions { get; init; } = new();

        public int TargetCount => Submissions.Count;
        public int SubmittedCount => Submissions.Count(s => s.HasSubmitted);
        public int MissedCount => Submissions.Count(s => !s.HasSubmitted);

        public string AverageText
        {
            get
            {
                var submitted = Submissions.Where(s => s.HasSubmitted).ToList();
                return submitted.Count == 0 ? "-" : $"{submitted.Average(s => s.Score):0.#} / {Questions.Count}";
            }
        }

        public List<QuizQuestionStat> BuildQuestionStats()
            => Questions.Select(q => new QuizQuestionStat(q, Submissions)).ToList();
    }

    // 학생 한 명의 누적. 수업을 따라오고 있는지 볼 때 실제로 쓰이는 표다.
    public class QuizStudentTally
    {
        public string StudentId { get; init; } = string.Empty;
        public string StudentName { get; init; } = string.Empty;
        public int RoundCount { get; set; }       // 그 학생이 대상이었던 퀴즈 수
        public int SubmittedCount { get; set; }
        public int QuestionCount { get; set; }    // 대상이었던 퀴즈의 문제를 모두 합친 수 (미제출 포함)
        public int CorrectCount { get; set; }
        public int MissedCount => RoundCount - SubmittedCount;
        public string ScoreText => $"{CorrectCount} / {QuestionCount}";

        public static List<QuizStudentTally> Build(IEnumerable<QuizRound> rounds)
        {
            var byStudent = new Dictionary<string, QuizStudentTally>();

            foreach (var round in rounds)
            {
                foreach (var submission in round.Submissions)
                {
                    if (!byStudent.TryGetValue(submission.StudentId, out var tally))
                    {
                        tally = new QuizStudentTally
                        {
                            StudentId = submission.StudentId,
                            StudentName = submission.StudentName,
                        };
                        byStudent[submission.StudentId] = tally;
                    }

                    tally.RoundCount++;
                    tally.QuestionCount += round.Questions.Count;
                    if (!submission.HasSubmitted) continue;
                    tally.SubmittedCount++;
                    tally.CorrectCount += submission.Score;
                }
            }

            return byStudent.Values.OrderBy(t => t.StudentId).ToList();
        }
    }
}
