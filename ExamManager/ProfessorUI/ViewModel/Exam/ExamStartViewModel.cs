using ProfessorUI.Service;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using ProfessorUI.Common;

namespace ProfessorUI.ViewModel
{
    public class ExamStartViewModel : INotifyPropertyChanged
    {
        // ⭐ 공용 저장소의 상태에 따라 버튼 활성화 여부 결정
        public bool IsStartButtonEnabled => SendFileState.IsFileDistributed && !ExamState.IsExamStarted;

        // ⭐ 컨테이너 전체를 흐리게(비활성화) 만들 속성
        public bool IsContainerEnabled => !ExamState.IsExamStarted;

        public ICommand StartExamCommand { get; }

        public ExamStartViewModel()
        {
            SendFileState.StateChanged += OnStateChanged;
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
            // 보내는 순서와 단계 올리기는 ExamFlowService 가 맡는다.
            // 알림창보다 먼저 보내야 교수가 확인을 누를 때까지 학생이 기다리지 않는다.
            ExamFlowService.Instance.StartExam();

            MessageBox.Show("시험을 시작합니다!", "알림");
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}