using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ExamManager.Shared;
using ProfessorUI.ViewModel;

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
            foreach (var student in _ctx.Overview.Students) student.PropertyChanged += OnStudentChanged;
            Unloaded += (_, _) =>
            {
                _ctx.Overview.Students.CollectionChanged -= OnStudentsChanged;
                foreach (var student in _ctx.Overview.Students) student.PropertyChanged -= OnStudentChanged;
            };

            UpdateEmptyNote();
        }

        private void OnStudentsChanged(object? sender,
                                       System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
                foreach (StudentItemViewModel student in e.NewItems) student.PropertyChanged += OnStudentChanged;
            if (e.OldItems != null)
                foreach (StudentItemViewModel student in e.OldItems) student.PropertyChanged -= OnStudentChanged;

            UpdateEmptyNote();
        }

        // 학생 칸이 바뀌는 순간 잠깐 깜빡인다. 접속은 초록, 연결 끊김은 빨강, 답안 제출은 파랑.
        // 무채색 화면에서는 칸이 흐려지는 것만으로는 누가 방금 바뀌었는지 놓치기 쉽다.
        private void OnStudentChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not StudentItemViewModel student) return;

            Color? color = e.PropertyName switch
            {
                nameof(StudentItemViewModel.IsConnected) =>
                    student.IsConnected ? UiSignal.GoodSoftColor : UiSignal.AlertSoftColor,
                nameof(StudentItemViewModel.IsAnswerSubmitted) when student.IsAnswerSubmitted =>
                    UiSignal.MessageSoftColor,
                _ => null,
            };
            if (color == null) return;

            if (StudentCards.ItemContainerGenerator.ContainerFromItem(student) is ContentPresenter presenter &&
                presenter.ContentTemplate?.FindName("CardBox", presenter) is Border card)
                UiSignal.Blink(card, Border.BackgroundProperty, color.Value);
        }

        private void UpdateEmptyNote()
            => EmptyNote.Visibility = _ctx.Overview.Students.Count == 0
                                    ? Visibility.Visible : Visibility.Collapsed;

        private void StartWizard_Click(object sender, RoutedEventArgs e)
            => ShellWindow.From(this)?.Navigate(new ExamPrepPage(), 1);

        // 학생 칸의 말풍선. 그 학생과의 1:1 대화를 열어 둔 채 알림·채팅 화면으로 넘어간다.
        private void Chat_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not StudentItemViewModel student) return;

            var tab = _ctx.Chat.GetOrCreateTab(student.StudentId, student.Name, student.SessionId);
            ShellWindow.From(this)?.Navigate(new ChatPage(tab), 4);
        }

        // 강의실 큰 모니터에 올려 둘 현황판을 띄운다.
        // 교수 창과 별개로 떠서 다른 모니터로 옮길 수 있다.
        private void OpenMonitorBoard_Click(object sender, RoutedEventArgs e)
            => MonitorBoardWindow.ShowSingle(Window.GetWindow(this));
    }
}
