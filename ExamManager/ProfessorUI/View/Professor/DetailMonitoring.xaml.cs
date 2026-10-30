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
        // 상세 보기는 한 번에 한 창만 띄운다. 학생마다 창이 쌓이면 어느 창이 지금 보고 있는
        // 학생인지 알 수 없고, 창마다 고화질 화면을 요청해 그만큼 네트워크를 더 쓴다.
        private static DetailMonitoring? _open;

        private readonly StudentScreenViewModel _item;

        /// <summary>그 학생의 상세 보기를 띄운다. 이미 다른 학생의 창이 떠 있으면 그 창을 갈아 끼운다.</summary>
        public static void ShowSingle(StudentScreenViewModel item, Window? owner)
        {
            var previous = _open;

            // 같은 학생이면 떠 있는 창을 그대로 앞으로 가져온다.
            // 다시 만들면 받아 둔 화면이 사라져 '대기 중…'부터 다시 시작한다.
            if (previous != null && previous._item == item)
            {
                if (previous.WindowState == WindowState.Minimized) previous.WindowState = WindowState.Normal;
                previous.Activate();
                return;
            }

            var window = new DetailMonitoring(item) { Owner = owner };
            _open = window;
            window.Show();

            // 옛 창은 새 창을 띄운 뒤에 닫는다. 먼저 닫으면 그 창이 교수 주 창을 앞으로 올려
            // (아래 포커스 되돌리기) 새 창이 그 뒤에 깔린다.
            previous?.Close();
        }

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

            // 이 창을 닫을 때 교수 주 창을 앞으로 올린다.
            //
            // 윈도우는 소유 창(Owner)을 자동으로 올려 주지 않는다 — 소유 창이 지정돼 있어도,
            // Z 순서상 다른 프로그램의 창이 교수 주 창보다 위에 있으면 그 창이 앞으로 온다.
            // 그러면 교수는 학생 화면을 한 번 볼 때마다 교수 앱을 다시 앞으로 끌어와야 한다.
            //
            // 올리는 시점이 중요하다. 포커스를 다른 프로그램에 한 번 넘기고 나면 윈도우가
            // Activate(SetForegroundWindow)를 막아 버려(포커스 잠금) 교수 앱은 스스로 앞으로 올 수 없다.
            // 그래서 아직 우리 앱이 포커스를 쥐고 있는 '닫는 순간'에 올린다.
            //
            // IsActive 로 거르는 이유: 사용자가 일부러 다른 프로그램으로 간 뒤에 이 창이 닫히는 경우
            // (학생이 끊겨 자동으로 닫히는 경우도 있다) 남의 화면 위로 교수 앱을 밀어 올리면 안 된다.
            bool wasActive = false;
            Closing += (_, _) => wasActive = IsActive;

            // 새 화면 및 상태 변경 자동 갱신
            _item.PropertyChanged += Item_PropertyChanged;
            Closed += (_, _) =>
            {
                if (_open == this) _open = null;

                _item.PropertyChanged -= Item_PropertyChanged;
                // 2. 창을 닫을 때 기본 썸네일(320x180) 모드로 복귀 요청
                NetworkService.Instance.SendToSession(_item.SessionId, PacketType.ScreenQualityMode, new byte[] { 0 });

                if (wasActive && Owner is { IsLoaded: true } owner) owner.Activate();
            };
        }

        // 아래 Dispatcher.Invoke 셋은 일부러 UiThread.Post 로 바꾸지 않았다.
        // 이 알림은 이미 화면 스레드에서 올라오므로(화면 목록이 올려 주는 PropertyChanged) Invoke 가 그 자리에서 바로 실행된다.
        // 넘기는 방식으로 바꾸면 '바로 끝났다'를 전제로 이어지는 뒷줄과 순서가 어긋난다.
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
                // 학생이 PC 를 끄거나 앱을 닫으면 이 창에는 멈춘 그림만 남는다. 더 보여 줄 것이 없으니 닫는다.
                // 따로 알리지 않는다 — 뒤에 있는 화면 모니터링 타일이 바로 '미접속' 검은 칸이 되고,
                // 원격 제어 중이었다면 그 창이 같은 알림을 받아 이유까지 띄운다.
                if (!_item.IsConnected)
                {
                    Dispatcher.Invoke(Close);
                    return;
                }

                // 명단 학생의 칸은 끊겨도 남아 있다가 다시 접속하면 같은 칸에 붙는다(ScreenBoardViewModel).
                // 이 창을 열어 둔 채 다시 들어왔으면 고화질을 다시 요청한다. 새 접속은 기본 화질로 시작한다.
                NetworkService.Instance.SendToSession(_item.SessionId, PacketType.ScreenQualityMode, new byte[] { 1 });
                Dispatcher.Invoke(UpdateKeyboardLockButton);
            }
        }

        // 이 학생 한 명에게만 상태 전환을 보낸다(전체 일괄은 교수 상단바에서).
        private void ClassModeFree_Click(object sender, RoutedEventArgs e) => SendClassMode(ClassMode.Free);
        private void ClassModeLocked_Click(object sender, RoutedEventArgs e) => SendClassMode(ClassMode.Locked);
        private void ClassModeExam_Click(object sender, RoutedEventArgs e) => SendClassMode(ClassMode.Exam);

        // 이 학생 PC 를 1:1 로 조작한다. 학생 쪽은 요청을 받으면 입력 잠금을 풀고 화면을 공유한다.
        private void RemoteControl_Click(object sender, RoutedEventArgs e) => RemoteControlWindow.Start(_item);

        private void SendClassMode(ClassMode mode)
        {
            // 통제·시험은 학생 입력을 잠그고 덮개를 띄운다 — 그러면 교수가 보낸 원격 입력까지 함께 막히고
            // 화면은 덮개만 보여, 원격 창은 못 쓰는데 떠 있는 상태가 된다. 먼저 끊고 알린다.
            if (mode != ClassMode.Free)
                RemoteControlWindow.CloseIfOpen("상태를 바꾸려면 원격 제어를 먼저 끊어야 합니다.");

            if (!_item.IsConnected) return;
            NetworkService.Instance.SendToSession(_item.SessionId, PacketType.ClassModeChange, new byte[] { (byte)mode });
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
