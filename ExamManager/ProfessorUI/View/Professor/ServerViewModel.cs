using System.ComponentModel;
using System.Runtime.CompilerServices;
using ProfessorUI.Service;

namespace ProfessorUI.View.Professor
{
    // 상단바에 띄우는 서버 상태.
    //
    // 서버는 앱을 켤 때 자동으로 열리므로 여기에 여닫는 기능은 없다.
    // 상태를 따로 들고 있으면 실제 서버와 화면이 어긋나므로 ServerControl 만 본다.
    public class ServerViewModel : INotifyPropertyChanged
    {
        public ServerViewModel()
        {
            ServerControl.StateChanged += OnServerStateChanged;
        }

        // 학생에게 불러 줄 주소. 여러 번 조회할 필요가 없어 한 번만 구한다.
        public string Address { get; } = ServerControl.Address;

        public bool IsRunning => ServerControl.IsRunning;

        // 주소를 상태에 붙여 둔다. 상단바 한 줄만 보고도 학생에게 불러 줄 수 있어야 한다.
        public string StatusText => IsRunning ? $"열림 · {Address}" : "닫힘";

        private void OnServerStateChanged() => OnPropertyChanged(nameof(StatusText));

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRunning)));
        }
    }
}
