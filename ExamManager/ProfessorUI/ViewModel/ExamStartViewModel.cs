using NetworkLib;
using ProfessorUI.Service;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

namespace ProfessorUI.ViewModel
{
    public class ExamStartViewModel : INotifyPropertyChanged
    {
        // ⭐ 공용 저장소의 상태에 따라 버튼 활성화 여부 결정
        public bool IsStartButtonEnabled => FileDeployState.IsFileDistributed && !ExamState.IsExamStarted;

        // ⭐ 컨테이너 전체를 흐리게(비활성화) 만들 속성
        public bool IsContainerEnabled => !ExamState.IsExamStarted;

        // ⭐ 배포 안 됐을 때 띄울 주황색 경고창의 표시 여부 결정
        public Visibility WarningVisibility => FileDeployState.IsFileDistributed ? Visibility.Collapsed : Visibility.Visible;

        public ICommand StartExamCommand { get; }

        public ExamStartViewModel()
        {
            FileDeployState.StateChanged += OnStateChanged;
            ExamState.StateChanged += OnStateChanged; // 시험 상태 변화도 구독
            StartExamCommand = new RelayCommand(ExecuteStartExam);
        }

        private void OnStateChanged()
        {
            OnPropertyChanged(nameof(IsStartButtonEnabled));
            OnPropertyChanged(nameof(IsContainerEnabled));
        }

        private void ExecuteStartExam(object obj)
        {
            // 접속 중인 모든 학생 PC에 압축 해제 명령을 보낸다.
            // (학생 쪽에서 해제가 끝나면 해제 폴더가 탐색기로 열린다)
            // 알림창보다 먼저 보내야 교수가 확인을 누를 때까지 학생이 기다리지 않는다.
            NetworkService.Instance.Broadcast(PacketType.ExtractArchive, Array.Empty<byte>());

            MessageBox.Show("시험을 시작합니다!", "알림");

            // 시험 시작! (이 코드가 실행되면 2, 3단계가 깨어납니다)
            ExamState.IsExamStarted = true;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}