using ProfessorUI.Service;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ProfessorUI.ViewModel
{
    public class MainViewModel : INotifyPropertyChanged
    {
        public TopViewModel TopVM { get; } = new TopViewModel();

        private readonly NavigationStore _navigationStore;
        public SidebarViewModel SidebarViewModel { get; }

        // XAML에서 ContentControl과 바인딩할 프로퍼티
        public object? CurrentViewModel => _navigationStore.CurrentViewModel;

        public MainViewModel(NavigationStore navigationStore)
        {
            _navigationStore = navigationStore;
            _navigationStore.CurrentViewModelChanged += OnCurrentViewModelChanged;
            SidebarViewModel = new SidebarViewModel(navigationStore);

        }

        private void OnCurrentViewModelChanged()
        {
            OnPropertyChanged(nameof(CurrentViewModel));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
