using ProfessorUI.Stores;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using ProfessorUI.ViewModels;

namespace ProfessorUI.ViewModels
{
    public class SidebarViewModel : INotifyPropertyChanged
    {
        private readonly NavigationStore _navigationStore;

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

            // ⭐ 앱이 처음 켜질 때 현재 인덱스(0번 = 현황판) 화면을 즉시 띄우도록 호출!
            NavigateToSelectedMenu();
        }

        private void NavigateToSelectedMenu()
        {
            // 번호에 따라 뷰를 직접 생성하는 대신, 'ViewModel'을 생성하여 Store에 넘김
            switch (SelectedIndex)
            {
                case 0: // 현황판
                    _navigationStore.CurrentViewModel = new ViewModels.StudentBoardViewModel();
                    break;
                case 1: // 파일 배포 (주석 해제 후 쟁반 뷰모델 연결!)
                    _navigationStore.CurrentViewModel = new FileDeployMainViewModel();
                    break;
                case 2: // 시험 시작/종료 (⭐ 이 부분 주석을 풀고 새 뷰모델을 연결합니다!)
                    _navigationStore.CurrentViewModel = new ExaminationMainViewModel();
                    break;
                case 3:
                    _navigationStore.CurrentViewModel = new ProgramControlMainViewModel();
                    break;
                    case 4:
                        _navigationStore.CurrentViewModel = new AttendanceMainViewModel();
                        break;
                    //case 5:
                    //    _navigationStore.CurrentViewModel = new QuizViewModel();
                    //    break;
            }
        }
        public event PropertyChangedEventHandler PropertyChanged;

        // 2. 값이 바뀔 때마다 화면에 방송을 때려주는 메서드
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
        // INotifyPropertyChanged 구현 생략 (위와 동일)
    }
}
