using NetworkLib;
using ProfessorUI.Service;
using System.Windows;
using System.Windows.Input;

namespace ProfessorUI.ViewModel
{
    // 시험 흐름의 첫 단계 — 학생 PC를 대기 화면에서 준비 화면으로 넘긴다.
    //
    // 파일 배포보다 먼저 눌러야 한다. 준비 화면에 파일 수신 진행률이 있어서,
    // 배포를 먼저 하면 학생이 파일이 도착하는 것을 보지 못한다.
    public class ExamReadyStateViewModel
    {
        public ICommand ForceReadyStateCommand { get; }

        public ExamReadyStateViewModel()
        {
            ForceReadyStateCommand = new RelayCommand(ExecuteForceReadyState);
        }

        private void ExecuteForceReadyState(object? obj)
        {
            var answer = MessageBox.Show(
                "학생 PC를 시험 준비 화면으로 전환합니다.\n계속하시겠습니까?",
                "시험 준비 상태로 전환", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (answer != MessageBoxResult.Yes) return;

            // 접속 중인 모든 학생에게 알린다. 학생 쪽 대기 화면이 이 신호를 받고 넘어간다.
            NetworkService.Instance.Broadcast(
                PacketType.ExamPhaseChange,
                ExamPhasePayload.Encode(ExamPhase.Ready, "시험 준비를 시작합니다."));

            ExamState.CurrentPhase = ExamPhase.Ready;

            MessageBox.Show("학생 PC를 시험 준비 화면으로 전환했습니다.", "안내",
                            MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
