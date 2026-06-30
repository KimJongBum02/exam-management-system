using ProfessorUI.Service;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace ProfessorUI.ViewModel
{
    public class ExamEndViewModel : INotifyPropertyChanged
    {
        public bool IsContainerEnabled => ExamState.IsExamStarted;
        public ObservableCollection<StudentItemViewModel> Students => StudentStore.Instance.Students;

        public ICommand ApproveSingleCommand { get; }
        public ICommand ApproveSelectedCommand { get; }

        // ⭐ 일괄 선택 / 해제를 위한 프로퍼티
        private bool _isAllSelected;
        public bool IsAllSelected
        {
            get => _isAllSelected;
            set
            {
                _isAllSelected = value;
                OnPropertyChanged();

                // 리스트에 있는 모든 학생의 체크 상태를 한 번에 변경
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

        private void ExecuteApproveSingle(object obj)
        {
            if (obj is StudentItemViewModel student && student.IsFileReceived)
            {
                student.IsApproved = true;
                student.Status = "종료";
            }
        }

        private void ExecuteApproveSelected(object obj)
        {
            // .ToList()로 명확히 뽑아서 바인딩 충돌 방지
            var targets = Students.Where(s => s.IsSelected && s.IsFileReceived && !s.IsApproved).ToList();

            foreach (var student in targets)
            {
                student.IsApproved = true;
                student.Status = "종료";
            }

            // 일괄 승인 후 전체선택 체크박스 해제
            IsAllSelected = false;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}