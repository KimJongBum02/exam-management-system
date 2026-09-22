using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using ProfessorUI.Common;
using ProfessorUI.Service;

namespace ProfessorUI.ViewModel
{
    // 상단바에 띄우는 서버 상태.
    //
    // 서버는 앱을 켤 때 자동으로 열린다. 그 뒤에 네트워크가 끊기거나 접속 받기가 멈출 수 있어
    // 몇 초마다 실제 상태를 다시 본다(ServerService.CheckHealth). 상태를 따로 들고 있지 않는다.
    // 꺼졌으면 [서버 다시 열기]로 같은 포트에 다시 연다.
    public class ServerStatusViewModel : INotifyPropertyChanged
    {
        // 네트워크가 끊기는 것은 몇 초 늦게 알아도 된다. 짧게 잡을 이유가 없다.
        private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(3);

        private ServerService.Health _health = ServerService.Health.Active;

        public ServerStatusViewModel()
        {
            ServerService.StateChanged += Refresh;

            var timer = new DispatcherTimer { Interval = CheckInterval };
            timer.Tick += (_, _) => Refresh();
            timer.Start();

            RestartCommand = new RelayCommand(_ => ExecuteRestart());
            Refresh();
        }

        // 학생에게 불러 줄 주소. 네트워크가 다시 붙으면서 바뀔 수 있어 확인할 때마다 새로 읽는다.
        public string Address { get; private set; } = ServerService.Address;

        public bool IsRunning => ServerService.IsRunning;

        // 학생을 실제로 받을 수 있는지. 상단바의 초록·빨강 불이 이 값을 따른다.
        public bool IsHealthy => _health == ServerService.Health.Active;

        // 꺼졌을 때만 [서버 다시 열기] 버튼을 보인다.
        public bool IsDown => !IsHealthy;

        // 주소와 포트를 상태에 붙여 둔다. 상단바 한 줄만 보고도 학생에게 불러 줄 수 있어야 한다.
        public string StatusText => _health switch
        {
            ServerService.Health.Active     => $"서버 활성화 · {Address}:{ServerService.Port}",
            ServerService.Health.NoNetwork  => "서버 꺼짐 (네트워크 끊김)",
            ServerService.Health.NotStarted => "서버 꺼짐 (서버를 열지 못함)",
            _                               => "서버 꺼짐",
        };

        public ICommand RestartCommand { get; }

        private void Refresh()
        {
            var health = ServerService.CheckHealth();
            string address = ServerService.Address;
            if (health == _health && address == Address) return;

            _health = health;
            Address = address;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        }

        // 네트워크가 끊긴 경우는 다시 열어도 소용이 없다 — 네트워크가 돌아오면 저절로 초록불로 바뀐다.
        private void ExecuteRestart()
        {
            if (_health == ServerService.Health.NoNetwork)
            {
                MessageBox.Show("이 PC가 네트워크에 연결되어 있지 않습니다.\n랜선이나 와이파이를 확인해 주세요. 연결되면 자동으로 다시 활성화됩니다.",
                                "서버 다시 열기", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var confirm = MessageBox.Show("서버를 다시 엽니다.\n접속 중인 학생은 잠시 끊겼다가 자동으로 다시 접속합니다.\n\n계속하시겠습니까?",
                                          "서버 다시 열기", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            string? problem = ServerService.Restart();
            Refresh();
            if (problem != null)
                MessageBox.Show(problem, "서버 다시 열기", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
