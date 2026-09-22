using NetworkLib;
using ProfessorUI.Service;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using ProfessorUI.Common;

namespace ProfessorUI.ViewModel
{
    // 시험을 끝내는 흐름 하나를 맡는다: 시험 종료 + 답안 수집 → 학생 승인.
    // 두 가지가 같은 화면(ExamEndWindow)에서 이어지므로 한 뷰모델에 둔다.
    // 실제 전송·승인 처리는 ExamFlowService 가 하고, 여기서는 버튼 상태와 확인·결과 창만 다룬다.
    public class ExamEndViewModel : INotifyPropertyChanged
    {
        public bool IsContainerEnabled => ExamState.IsExamStarted;

        // 시험이 종료(SubmitRequested 이상)되면 true → 시험 종료 버튼을 비활성화한다.
        // 상태 초기화(Waiting)가 되면 다시 false로 돌아온다.
        public bool IsExamEnded => ExamState.CurrentPhase >= ExamPhase.SubmitRequested;

        // 승인 대상을 고르는 표가 쓰는 학생 목록
        public ObservableCollection<StudentStatusViewModel> Students => StudentStore.Instance.Students;

        public ICommand EndExamCommand { get; }
        public ICommand ApproveSelectedCommand { get; }
        public ICommand RecollectCommand { get; }

        public ExamEndViewModel()
        {
            EndExamCommand = new RelayCommand(ExecuteEndExam, canExecute: o => IsContainerEnabled && !IsExamEnded);
            ApproveSelectedCommand = new RelayCommand(ExecuteApproveSelected);
            RecollectCommand = new RelayCommand(ExecuteRecollect, canExecute: o => IsExamEnded);

            ExamState.StateChanged += () =>
            {
                OnPropertyChanged(nameof(IsContainerEnabled));
                OnPropertyChanged(nameof(IsExamEnded));
                CommandManager.InvalidateRequerySuggested();
            };
        }

        // [시험 종료 및 답안 수집] 버튼 — 시험을 끝내고 곧바로 전원의 답안을 걷는다.
        private void ExecuteEndExam(object obj)
        {
            var result = MessageBox.Show("시험을 종료하고 모든 학생의 답안을 수집하시겠습니까?", "시험 종료 확인",
                                         MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            // 종료 신호·단계 올리기·답안 요청은 ExamFlowService 가 맡는다.
            // 알림창보다 먼저 보내야 교수가 확인을 누를 때까지 학생이 기다리지 않는다.
            ExamFlowService.Instance.EndExam();
            ExamFlowService.Instance.RequestAnswers();

            MessageBox.Show("시험을 종료하고 답안 수집을 요청했습니다.\n학생이 답안을 보내면 자동으로 저장됩니다.",
                            "안내", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // [미수집 학생 다시 수집] 버튼 — 시험 종료 뒤 답안이 오지 않은 학생에게만 다시 요청한다.
        private void ExecuteRecollect(object? obj)
        {
            var result = ExamFlowService.Instance.RequestMissingAnswers();

            if (result.Requested == 0 && result.Offline.Count == 0)
            {
                MessageBox.Show("모든 학생의 답안이 수집되었습니다.", "다시 수집", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string message = $"{result.Requested}명에게 답안을 다시 요청했습니다.\n학생이 답안을 보내면 자동으로 저장됩니다.";
            if (result.Offline.Count > 0)
                message += $"\n\n접속이 끊겨 요청하지 못한 학생 {result.Offline.Count}명:\n" +
                           string.Join("\n", result.Offline) +
                           "\n\n이 학생들은 다시 접속한 뒤 한 번 더 눌러 주세요.";

            MessageBox.Show(message, "다시 수집", MessageBoxButton.OK,
                result.Offline.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }

        // [선택 승인] 버튼.
        // 승인과 학생 PC 종료 명령은 ExamFlowService 가 맡고, 여기서는 대상 고르기와 결과 안내만 한다.
        private void ExecuteApproveSelected(object obj)
        {
            var targets = Students.Where(s => s.IsSelected && s.IsAnswerSubmitted && !s.IsApproved).ToList();

            if (!targets.Any())
            {
                MessageBox.Show("승인할 대상이 선택되지 않았거나, 수집 완료된 항목이 없습니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = ExamFlowService.Instance.ApproveStudents(targets);

            // 표의 비고 칸만 바뀌어서는 승인이 됐는지 알아채기 어렵다. 결과를 창으로 알린다.
            // 접속이 끊긴 학생에게는 명령이 가지 않으므로 누구인지 따로 적는다.
            string message = $"{result.Approved}명을 승인했습니다.\n" +
                             $"{result.Approved - result.Offline.Count}명의 PC에 종료 명령을 보냈습니다.";
            if (result.Offline.Count > 0)
                message += $"\n\n접속이 끊겨 종료 명령을 보내지 못한 학생 {result.Offline.Count}명:\n" +
                           string.Join("\n", result.Offline) +
                           "\n\n이 학생들의 PC는 자리에서 직접 확인해 주세요.";

            MessageBox.Show(message, "승인 완료", MessageBoxButton.OK,
                result.Offline.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }

        // 승인이 끝났다고 해서 상태를 초기화하지 않는다.
        //
        // 예전에는 모든 학생이 승인되면 곧바로 학생별 기록과 시험 단계를 되돌렸다.
        // 그러면 교수가 결과를 보려고 종료 완료 현황으로 넘어간 순간 방금 걷은 답안이
        // '미수집', 승인한 학생이 '대기'로 바뀌어 있게 된다.
        // (학생이 한 명이면 승인하자마자 그렇게 된다)
        //
        // 초기화는 종료 완료 현황 화면의 [처음 화면으로] 가 확인을 받고 처리한다.

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}