using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using ProfessorUI.Service;
using ProfessorUI.ViewModel;

namespace ProfessorUI.View.Professor
{
    // 마법사 2단계의 서버 열기/닫기 화면.
    //
    // 서버를 실제로 여닫는 일은 ServerControl 이 맡고, 여기서는 그 상태를 보여 주기만 한다.
    // 앱을 켠 직후에는 서버가 닫혀 있고, 교수가 [서버 열기] 를 눌러야 열린다.
    // 상태를 여기서 따로 들고 있으면 실제 서버와 화면이 어긋나므로 ServerControl 만 본다.
    public class ServerViewModel : INotifyPropertyChanged
    {
        public ServerViewModel()
        {
            StartCommand = new RelayCommand(_ => Start(), _ => !IsRunning);
            StopCommand = new RelayCommand(_ => Stop(), _ => IsRunning);

            // 다른 곳에서 서버를 여닫아도 이 화면이 따라가게 한다.
            ServerControl.StateChanged += OnServerStateChanged;
        }

        // 학생에게 불러 줄 주소. 여러 번 조회할 필요가 없어 한 번만 구한다.
        public string Address { get; } = ServerControl.Address;

        public string Port => ServerControl.Port.ToString();

        public bool IsRunning => ServerControl.IsRunning;

        public string StatusText => IsRunning ? "열림 · 학생 접속 대기 중" : "닫힘";

        public ICommand StartCommand { get; }
        public ICommand StopCommand { get; }

        private void Start()
        {
            if (!ServerControl.Start())
            {
                MessageBox.Show("서버를 열지 못했습니다. 포트 9000을 다른 프로그램이 쓰고 있는지 확인해 주세요.",
                                "서버 열기 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void Stop() => ServerControl.Stop();

        private void OnServerStateChanged()
        {
            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(StatusText));
            CommandManager.InvalidateRequerySuggested();
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
