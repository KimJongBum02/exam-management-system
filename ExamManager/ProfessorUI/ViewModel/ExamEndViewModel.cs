using NetworkLib; // ExamPhase 사용을 위해 추가
using ProfessorUI.Service;
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

        public ICommand ApproveSingleCommand { get; }
        public ICommand ApproveSelectedCommand { get; }

        private bool _isAllSelected;
        public bool IsAllSelected
        {
            get => _isAllSelected;
            set
            {
                _isAllSelected = value;
                OnPropertyChanged();

                foreach (var student in Students)
                {
                    student.IsSelected = value;
                }
            }
        }

        public ExamEndViewModel()
        {
            ApproveSingleCommand = new RelayCommand(ExecuteApproveSingle);
            ApproveSelectedCommand = new RelayCommand(ExecuteApproveSelected);

            ExamState.StateChanged += () => OnPropertyChanged(nameof(IsContainerEnabled));
        }

        // 개별 승인 처리
        private void ExecuteApproveSingle(object obj)
        {
            if (obj is StudentItemViewModel student && student.IsAnswerSubmitted)
            {
                student.IsApproved = true;
                student.Status = "종료";
            }

            // 단일 승인 후 모든 학생이 승인되었는지 확인
            CheckAndResetIfAllApproved();
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

            foreach (var student in targets)
            {
                student.IsApproved = true;
                student.Status = "종료";
            }

            // 일괄 승인 후 체크박스 해제
            _isAllSelected = false;
            OnPropertyChanged(nameof(IsAllSelected));

            // 일괄 승인 후에도 '모든 학생이 승인되었는지' 검사 후 리셋
            CheckAndResetIfAllApproved();
        }

        // 모든 학생의 승인이 끝났는지 확인하는 메서드
        private void CheckAndResetIfAllApproved()
        {
            // 학생 목록이 존재하고, 목록 내 모든 학생(Students)의 IsApproved가 true일 때만 초기화
            if (Students.Count > 0 && Students.All(s => s.IsApproved))
            {
                MessageBox.Show("모든 학생의 승인이 완료되어 시험 상태를 초기화합니다.", "안내", MessageBoxButton.OK, MessageBoxImage.Information);
                ResetAllState();
            }
        }

        // 모든 상태 초기화 메서드
        private void ResetAllState()
        {
            // 1. 전체 선택 체크박스 해제
            _isAllSelected = false;
            OnPropertyChanged(nameof(IsAllSelected));

            // 2. 모든 학생 개별 상태 초기화
            foreach (var student in Students)
            {
                student.IsSelected = false;
                student.IsFileReceived = false;
                student.IsAnswerSubmitted = false;
                student.IsApproved = false;
                student.Status = "대기";
            }

            // 3. 준비해 둔 시험 파일과 배포 기록도 함께 비웁니다.
            //    이걸 남겨 두면 다음 시험이 '이미 배포됨' 상태에서 시작됩니다.
            FileDeployState.Clear();

            // 4. 전역 시험 상태 초기화.
            //    ExamPhase.Waiting(0)으로 바꾸면 IsExamStarted(>= InProgress)가 false가 되고,
            //    단계 화면은 이 신호를 받아 첫 단계로 돌아갑니다.
            ExamState.CompleteAndReset();
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}