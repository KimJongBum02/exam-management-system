using NetworkLib;
using ProfessorUI.Service;
using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Input;

namespace ProfessorUI.ViewModel
{
    public class FileDeployMainViewModel
    {
        // 세 개의 자식 뷰모델을 쥐고 있습니다.
        // ExamReadyStateView는 자기 뷰모델 없이 이 쟁반의 DataContext를 그대로 물려받으므로,
        // 준비 상태 전환 커맨드는 여기에 둡니다.
        public FileReadyViewModel ReadyVM { get; }
        public FileDistributeViewModel DistributeVM { get; }

        public ICommand ForceReadyStateCommand { get; }

        public FileDeployMainViewModel()
        {
            // 쟁반이 생성될 때, 자식들도 같이 생성해줍니다.
            ReadyVM = new FileReadyViewModel();
            DistributeVM = new FileDistributeViewModel();

            ForceReadyStateCommand = new RelayCommand(ExecuteForceReadyState);
        }

        // [시험 준비 상태로 전환] 버튼 — 시험 흐름의 첫 단계입니다.
        //
        // 학생 PC를 대기 화면에서 준비 화면으로 넘깁니다.
        // 파일 배포보다 먼저 눌러야 합니다. 준비 화면에 파일 수신 진행률이 있어서,
        // 배포를 먼저 하면 학생이 파일이 도착하는 것을 보지 못합니다.
        private void ExecuteForceReadyState(object? obj)
        {
            var answer = MessageBox.Show(
                "학생 PC를 시험 준비 화면으로 전환합니다.\n계속하시겠습니까?",
                "시험 준비 상태로 전환", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (answer != MessageBoxResult.Yes) return;

            // 접속 중인 모든 학생에게 알립니다. 학생 쪽 대기 화면이 이 신호를 받고 넘어갑니다.
            NetworkService.Instance.Broadcast(
                PacketType.ExamPhaseChange,
                ExamPhasePayload.Encode(ExamPhase.Ready, "시험 준비를 시작합니다."));

            ExamState.CurrentPhase = ExamPhase.Ready;

            MessageBox.Show("학생 PC를 시험 준비 화면으로 전환했습니다.", "안내",
                            MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
