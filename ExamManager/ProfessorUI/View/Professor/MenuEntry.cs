using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ProfessorUI.View.Professor
{
    // 좌측 운영 메뉴 한 줄. 시험 단계에 따라 잠기고, 잠긴 이유를 툴팁으로 보여준다.
    public class MenuEntry : INotifyPropertyChanged
    {
        public MenuEntry(string name) => Name = name;

        public string Name { get; }

        private bool _enabled = true;
        public bool Enabled
        {
            get => _enabled;
            set { if (_enabled != value) { _enabled = value; OnPropertyChanged(); } }
        }

        // 잠겨 있을 때만 값이 있다. 열려 있으면 null이라 툴팁이 뜨지 않는다.
        private string? _lockReason;
        public string? LockReason
        {
            get => _lockReason;
            set { if (_lockReason != value) { _lockReason = value; OnPropertyChanged(); } }
        }

        public void SetGate(bool open, string reason)
        {
            Enabled = open;
            LockReason = open ? null : reason;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
