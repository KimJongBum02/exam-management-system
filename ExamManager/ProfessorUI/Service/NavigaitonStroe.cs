using System;
using System.Collections.Generic;
using System.Text;

namespace ProfessorUI.Service
{
    using System;

    // 현재 화면(ViewModel)이 무엇인지 저장하고 변경을 알리는 클래스
    public class NavigationStore
    {
        public event Action? CurrentViewModelChanged;

        private object? _currentViewModel;
        public object? CurrentViewModel {
            get => _currentViewModel;
            set
            {
                _currentViewModel = value;
                CurrentViewModelChanged?.Invoke(); // 화면이 바뀌었음을 알림
            }
        }
    }
}
