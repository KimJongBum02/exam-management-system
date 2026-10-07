using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using NetworkLib;
using ProfessorUI.Service;
using ProfessorUI.ViewModel;

namespace ProfessorUI.View.Professor
{
    public partial class DetailMonitoring : Window
    {
        private readonly StudentScreenViewModel _item;

        public DetailMonitoring(StudentScreenViewModel item)
        {
            InitializeComponent();
            _item = item;

            Title           = $"화면 상세 보기 – {item.DisplayName}";
            NameText.Text   = item.DisplayName;
            UpdatedText.Text = $"마지막 수신: {item.LastUpdated}";
            ScreenImage.Source = item.Screen;
            if (item.Screen != null)
                ResolutionText.Text = $"{item.Screen.PixelWidth} × {item.Screen.PixelHeight} · JPEG";

            if (item.Screen == null)
                WaitingText.Visibility = Visibility.Visible;
            else
                WaitingText.Visibility = Visibility.Collapsed;

            UpdateKeyboardLockButton();

            // 1. 해당 학생에게만 고화질(1920x1080 FHD) 화면 캡처 전송 요청
            NetworkService.Instance.SendToSession(_item.SessionId, PacketType.ScreenQualityMode, new byte[] { 1 });

            // 새 화면 및 상태 변경 자동 갱신
            _item.PropertyChanged += Item_PropertyChanged;
            Closed += (_, _) =>
            {
                _item.PropertyChanged -= Item_PropertyChanged;
                // 2. 창을 닫을 때 기본 썸네일(320x180) 모드로 복귀 요청
                NetworkService.Instance.SendToSession(_item.SessionId, PacketType.ScreenQualityMode, new byte[] { 0 });
            };
        }

        private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(StudentScreenViewModel.Screen))
            {
                Dispatcher.Invoke(() =>
                {
                    ScreenImage.Source      = _item.Screen;
                    UpdatedText.Text        = $"마지막 수신: {_item.LastUpdated}";
                    WaitingText.Visibility  = _item.HasScreen ? Visibility.Collapsed : Visibility.Visible;
                    if (_item.Screen != null)
                        ResolutionText.Text = $"{_item.Screen.PixelWidth} × {_item.Screen.PixelHeight} · JPEG";
                });
            }
            else if (e.PropertyName == nameof(StudentScreenViewModel.IsKeyboardLocked))
            {
                Dispatcher.Invoke(UpdateKeyboardLockButton);
            }
            else if (e.PropertyName == nameof(StudentScreenViewModel.IsConnected))
            {
                // 명단 학생의 칸은 끊겨도 남아 있다가 다시 접속하면 같은 칸에 붙는다(ScreenBoardViewModel).
                // 이 창을 열어 둔 채 다시 들어왔으면 고화질을 다시 요청한다. 새 접속은 기본 화질로 시작한다.
                if (_item.IsConnected)
                    NetworkService.Instance.SendToSession(_item.SessionId, PacketType.ScreenQualityMode, new byte[] { 1 });
                Dispatcher.Invoke(UpdateKeyboardLockButton);
            }
        }

        private void KeyboardLock_Click(object sender, RoutedEventArgs e)
        {
            bool newLockedState = !_item.IsKeyboardLocked;
            _item.IsKeyboardLocked = newLockedState;

            // 학생에게 키보드 잠금/해제 패킷 전송 (1: 잠금, 0: 해제)
            byte payload = (byte)(newLockedState ? 1 : 0);
            NetworkService.Instance.SendToSession(_item.SessionId, PacketType.LockKeyboard, new byte[] { payload });

            UpdateKeyboardLockButton();
            ScreenBoardViewModel.Instance.CheckAllLockState();
        }

        private void UpdateKeyboardLockButton()
        {
            // 접속이 끊긴 학생(명단 학생의 빈 칸)에게는 잠금을 보낼 곳이 없다. 눌러도 표시만 바뀌므로 막아 둔다.
            KeyboardLockButton.IsEnabled = _item.IsConnected;

            if (_item.IsKeyboardLocked)
            {
                KeyboardLockButton.Content = "🔒  키보드 잠김 (해제하려면 클릭)";
                KeyboardLockButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DC2626"));
                KeyboardLockButton.Foreground = Brushes.White;
            }
            else
            {
                KeyboardLockButton.Content = "🔒  키보드 잠금";
                KeyboardLockButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155"));
                KeyboardLockButton.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F1F5F9"));
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
