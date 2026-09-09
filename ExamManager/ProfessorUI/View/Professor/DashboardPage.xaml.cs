using System.Windows;
using System.Windows.Controls;

namespace ProfessorUI.View.Professor
{
    public partial class DashboardPage : UserControl
    {
        private readonly UiContext _ctx = UiContext.Instance;

        public DashboardPage()
        {
            InitializeComponent();
            DataContext = _ctx;
            StudentCards.ItemsSource = _ctx.Overview.Students;

            // 학생이 붙고 빠질 때마다 '아직 없습니다' 안내를 켜고 끈다.
            // 화면은 메뉴를 옮길 때마다 새로 만들어지므로 떠날 때 구독을 푼다.
            _ctx.Overview.Students.CollectionChanged += OnStudentsChanged;
            Unloaded += (_, _) => _ctx.Overview.Students.CollectionChanged -= OnStudentsChanged;

            UpdateEmptyNote();
        }

        private void OnStudentsChanged(object? sender,
                                       System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
            => UpdateEmptyNote();

        private void UpdateEmptyNote()
            => EmptyNote.Visibility = _ctx.Overview.Students.Count == 0
                                    ? Visibility.Visible : Visibility.Collapsed;

        private void StartWizard_Click(object sender, RoutedEventArgs e)
            => ShellWindow.From(this)?.Navigate(new ExamPrepPage(), 1);

        // 강의실 큰 모니터에 올려 둘 현황판을 띄운다.
        // 교수 창과 별개로 떠서 다른 모니터로 옮길 수 있다.
        private void OpenMonitorBoard_Click(object sender, RoutedEventArgs e)
            => MonitorBoardWindow.ShowSingle(Window.GetWindow(this));
    }
}
