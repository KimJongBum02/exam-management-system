using ProfessorUI.Service;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace ProfessorUI.ViewModel
{
    // '시험' 메뉴 전체를 담당하는 뷰모델.
    //
    // 프로세스 제어 · 파일 배포 · 시험 시작/종료로 나뉘어 있던 세 화면을
    // 순서가 있는 한 줄기 단계로 합친 것이다. 한 번에 한 단계만 보여주고,
    // 이전/다음 버튼이나 상단 동그라미로 단계를 옮긴다.
    //
    // 단계 이동 자체에는 아무 조건을 걸지 않는다. 어느 단계로든 건너뛸 수 있고,
    // 실제로 무엇을 할 수 있는지는 각 화면이 원래 갖고 있던 활성화 조건이 정한다.
    // (예: 파일 배포 버튼은 압축이 끝나야 눌린다)
    public class ExamFlowViewModel : INotifyPropertyChanged
    {
        private readonly List<ExamStepViewModel> _steps;
        private int _currentIndex;

        public ExamFlowViewModel()
        {
            // 목록형 화면(프로세스 제어·배포 목록·승인 목록)은 넓게, 카드형은 좁게 둔다.
            const double Card = 620;
            const double List = 940;
            const double Full = double.PositiveInfinity;

            _steps = new List<ExamStepViewModel>
            {
                new ExamStepViewModel(1, "시험 준비 상태로 전환", new ExamReadyStateViewModel(), Card),
                new ExamStepViewModel(2, "프로세스 제어",        new ProgramControlMainViewModel(), Full),
                new ExamStepViewModel(3, "파일 압축",            new FileReadyViewModel(), Card),
                new ExamStepViewModel(4, "파일 배포",            new FileDistributeViewModel(), List),
                new ExamStepViewModel(5, "시험 시작",            new ExamStartViewModel(), Card),
                new ExamStepViewModel(6, "시험 중 / 종료 / 답안 수집", new AnswerCollectViewModel(), Card),
                new ExamStepViewModel(7, "승인 후 종료",         new ExamEndViewModel(), List),
            };

            PreviousCommand = new RelayCommand(_ => GoToIndex(_currentIndex - 1), _ => CanGoPrevious);
            NextCommand = new RelayCommand(_ => GoToIndex(_currentIndex + 1), _ => CanGoNext);
            GoToStepCommand = new RelayCommand(step => GoToIndex(_steps.IndexOf((ExamStepViewModel)step!)));

            _steps[0].IsCurrent = true;

            // 모든 학생의 승인이 끝나면 처음 단계로 돌아간다.
            ExamState.ExamCompleted += () => GoToIndex(0);
        }

        public IReadOnlyList<ExamStepViewModel> Steps => _steps;

        public ExamStepViewModel CurrentStep => _steps[_currentIndex];

        public bool CanGoPrevious => _currentIndex > 0;
        public bool CanGoNext => _currentIndex < _steps.Count - 1;

        public ICommand PreviousCommand { get; }
        public ICommand NextCommand { get; }
        public ICommand GoToStepCommand { get; }

        private void GoToIndex(int index)
        {
            if (index < 0 || index >= _steps.Count || index == _currentIndex) return;

            _steps[_currentIndex].IsCurrent = false;
            _currentIndex = index;
            _steps[_currentIndex].IsCurrent = true;

            OnPropertyChanged(nameof(CurrentStep));
            OnPropertyChanged(nameof(CanGoPrevious));
            OnPropertyChanged(nameof(CanGoNext));

            // 이전/다음 버튼의 활성화 여부를 다시 판단하게 한다.
            // RelayCommand는 CommandManager에 기대고 있어, 이 호출이 없으면
            // 마지막 단계에서도 '다음' 버튼이 눌리는 상태로 남는다.
            CommandManager.InvalidateRequerySuggested();
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
