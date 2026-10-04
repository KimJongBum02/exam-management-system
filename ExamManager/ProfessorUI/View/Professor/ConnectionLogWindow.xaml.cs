using System.Collections.Specialized;
using System.Windows;
using ProfessorUI.Service;

namespace ProfessorUI.View.Professor
{
    // 학생 접속 기록을 보는 창.
    //
    // 대시보드 옆 버튼으로 연다. 교수가 현황을 보면서 같이 띄워 둘 수 있게
    // 모달이 아니라 따로 뜨는 창으로 둔다.
    public partial class ConnectionLogWindow : Window
    {
        // 버튼을 여러 번 눌러도 창은 하나만 둔다. 현황판 창과 같은 방식이다.
        private static ConnectionLogWindow? _open;

        public static void ShowSingle(Window? owner)
        {
            if (_open != null)
            {
                if (_open.WindowState == WindowState.Minimized) _open.WindowState = WindowState.Normal;
                _open.Activate();
                return;
            }

            _open = new ConnectionLogWindow { Owner = owner };
            _open.Show();
        }

        public ConnectionLogWindow()
        {
            InitializeComponent();

            var log = ConnectionLogStore.Instance;
            LogTable.ItemsSource = log.Entries;

            // 창을 열어 둔 채로 학생이 들어오면 줄이 바로 늘어난다.
            log.Entries.CollectionChanged += OnEntriesChanged;
            Closed += (_, _) =>
            {
                log.Entries.CollectionChanged -= OnEntriesChanged;
                _open = null;
            };

            UpdateSummary();
        }

        private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateSummary();

        private void UpdateSummary()
        {
            int count = ConnectionLogStore.Instance.Entries.Count;
            CountText.Text = $"모두 {count}건";
            EmptyNote.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
