using System;
using System.Windows;
using System.Windows.Threading;

namespace ProfessorUI.View.Professor
{
    // 강의실 큰 모니터에 띄워 두는 현황판.
    //
    // 교수 화면과 같은 저장소를 보므로 학생 수는 저절로 따라 바뀐다.
    // 조작할 것은 두지 않는다 — 학생들이 보는 화면이라 누를 것이 있으면 안 된다.
    public partial class MonitorBoardWindow : Window
    {
        // 창을 여러 개 띄우면 어느 것이 최신인지 알 수 없어 하나만 둔다.
        private static MonitorBoardWindow? _open;

        private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };

        public static void ShowSingle(Window? owner)
        {
            if (_open != null)
            {
                // 이미 떠 있으면 새로 만들지 않고 앞으로 가져온다.
                // 다른 모니터로 옮겨 둔 창을 놓쳤을 때 다시 찾는 용도이기도 하다.
                if (_open.WindowState == WindowState.Minimized) _open.WindowState = WindowState.Normal;
                _open.Activate();
                return;
            }

            // 소유 창을 지정하지 않는다. 지정하면 교수 창 위에 붙어 다녀
            // 다른 모니터로 옮겨 두기 어렵다.
            _open = new MonitorBoardWindow();
            _open.Show();
        }

        public MonitorBoardWindow()
        {
            InitializeComponent();
            DataContext = UiContext.Instance;

            UpdateClock();
            _clock.Tick += (_, _) => UpdateClock();
            _clock.Start();

            Closed += (_, _) =>
            {
                // 창이 닫힌 뒤에도 초마다 깨어나지 않도록 반드시 멈춘다.
                _clock.Stop();
                _open = null;
            };
        }

        private void UpdateClock()
        {
            DateText.Text = DateTime.Now.ToString("yyyy. MM. dd (ddd)");
            TimeText.Text = DateTime.Now.ToString("HH:mm:ss");
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
