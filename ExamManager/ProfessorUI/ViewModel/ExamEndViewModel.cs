using NetworkLib; // ExamPhase 사용을 위해 추가
using ProfessorUI.Service;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

namespace ProfessorUI.ViewModel
{
    public class ExamEndViewModel : INotifyPropertyChanged
    {
        public bool IsContainerEnabled => ExamState.IsExamStarted;
        public ObservableCollection<StudentItemViewModel> Students => StudentStore.Instance.Students;

        public ICommand ApproveSelectedCommand { get; }


        public ExamEndViewModel()
        {
            ApproveSelectedCommand = new RelayCommand(ExecuteApproveSelected);

            ExamState.StateChanged += () => OnPropertyChanged(nameof(IsContainerEnabled));
        }

        // 학생 PC에 종료 명령을 보낸다. 접속이 끊겨 보내지 못했으면 false.
        // 시험 흔적 삭제는 답안 회신을 받은 학생 쪽에서 이미 진행되므로 여기서는 종료만 지시한다.
        private static bool ShutdownStudentPc(StudentItemViewModel student)
        {
            if (!student.IsConnected || string.IsNullOrEmpty(student.SessionId)) return false;

            NetworkService.Instance.SendToSession(
                student.SessionId, PacketType.ShutdownPC, System.Array.Empty<byte>());
            return true;
        }

        // 선택 항목 일괄 승인 처리
        private void ExecuteApproveSelected(object obj)
        {
            var targets = Students.Where(s => s.IsSelected && s.IsAnswerSubmitted && !s.IsApproved).ToList();

            if (!targets.Any())
            {
                MessageBox.Show("승인할 대상이 선택되지 않았거나, 수집 완료된 항목이 없습니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var offline = new List<string>();
            foreach (var student in targets)
            {
                if (!ShutdownStudentPc(student))
                    offline.Add($"{student.StudentId} {student.Name}");
                student.IsApproved = true;
                student.IsSelected = false; // 승인한 학생은 더 고를 수 없으므로 체크도 푼다
                student.Status = "종료";
            }

            // 표의 비고 칸만 바뀌어서는 승인이 됐는지 알아채기 어렵다. 결과를 창으로 알린다.
            // 접속이 끊긴 학생에게는 명령이 가지 않으므로 누구인지 따로 적는다.
            string message = $"{targets.Count}명을 승인했습니다.\n" +
                             $"{targets.Count - offline.Count}명의 PC에 종료 명령을 보냈습니다.";
            if (offline.Count > 0)
                message += $"\n\n접속이 끊겨 종료 명령을 보내지 못한 학생 {offline.Count}명:\n" +
                           string.Join("\n", offline) +
                           "\n\n이 학생들의 PC는 자리에서 직접 확인해 주세요.";

            MessageBox.Show(message, "승인 완료", MessageBoxButton.OK,
                offline.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
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