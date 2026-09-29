using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ProfessorUI.ViewModel
{
    // 좌측 운영 메뉴 한 줄. 시험 단계에 따라 잠기고, 잠긴 이유를 툴팁으로 보여준다.
    public class MenuEntryViewModel : INotifyPropertyChanged
    {
        public MenuEntryViewModel(string name) => Name = name;

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

        // 메뉴 이름 옆 빨간 동그라미에 보일 개수(처리할 것이 쌓인 수). 0 이면 숨긴다.
        private int _badgeCount;
        public int BadgeCount
        {
            get => _badgeCount;
            set { if (_badgeCount != value) { _badgeCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasBadge)); } }
        }
        public bool HasBadge => _badgeCount > 0;

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
