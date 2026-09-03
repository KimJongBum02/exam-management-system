using ProfessorUI.Service;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using ProfessorUI.ViewModel;

namespace ProfessorUI.ViewModel
{
    public class SidebarViewModel : INotifyPropertyChanged
    {
        private readonly NavigationStore _navigationStore;

        private readonly StudentBoardViewModel _studentBoardViewModel;
        private readonly AttendanceMainViewModel _attendanceMainViewModel;
        private readonly ExamFlowViewModel _examFlowViewModel;
        private readonly OXQuizViewModel _quizViewModel;

        private int _selectedIndex;
        public int SelectedIndex
        {
            get => _selectedIndex;
            set
            {
                if (_selectedIndex != value)
                {
                    _selectedIndex = value;
                    OnPropertyChanged();
                    NavigateToSelectedMenu(); // 선택된 번호가 바뀔 때마다 네비게이션 실행
                }
            }
        }

        public SidebarViewModel(NavigationStore navigationStore)
        {
            _navigationStore = navigationStore;

            _studentBoardViewModel = new StudentBoardViewModel();
            _attendanceMainViewModel = new AttendanceMainViewModel();
            _examFlowViewModel = new ExamFlowViewModel();
            _quizViewModel = new OXQuizViewModel();

            // ⭐ 앱이 처음 켜질 때 현재 인덱스(0번 = 현황판) 화면을 즉시 띄우도록 호출!
            NavigateToSelectedMenu();
        }

        private void NavigateToSelectedMenu()
        {
            // 번호에 따라 뷰를 직접 생성하는 대신, 'ViewModel'을 생성하여 Store에 넘김.
            // 뷰모델은 생성자에서 한 번만 만들어 두고 돌려쓴다 —
            // 메뉴를 옮겼다 돌아왔을 때 입력해 둔 내용이 사라지지 않도록.
            switch (SelectedIndex)
            {
                case 0: // 현황판
                    _navigationStore.CurrentViewModel = _studentBoardViewModel;
                    break;
                case 1: // 출결 관리
                    _navigationStore.CurrentViewModel = _attendanceMainViewModel;
                    break;
                case 2: // 시험 — 프로세스 제어부터 승인 종료까지 단계별로 진행
                    _navigationStore.CurrentViewModel = _examFlowViewModel;
                    break;
                case 3: // OX 퀴즈
                    _navigationStore.CurrentViewModel = _quizViewModel;
                    break;
            }
        }
        public event PropertyChangedEventHandler? PropertyChanged;

        // 2. 값이 바뀔 때마다 화면에 방송을 때려주는 메서드
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
        // INotifyPropertyChanged 구현 생략 (위와 동일)
    }
}
