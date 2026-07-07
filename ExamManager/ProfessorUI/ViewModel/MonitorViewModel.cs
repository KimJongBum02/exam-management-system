using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ProfessorUI.ViewModel
{
    public class MonitorViewModel : INotifyPropertyChanged
    {
        // 1. 우리가 아까 만든 탑 뷰모델 (시계)
        public TopViewModel TopVM { get; }


        public MonitorViewModel()
        {
            TopVM = new TopViewModel();
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}