using System.Collections;
using System.ComponentModel;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using ExamManager.Shared;
using ProfessorUI.ViewModel;
using ProfessorUI.Common;

namespace ProfessorUI.View.Professor
{
    public partial class DashboardWindow : UserControl
    {
        private readonly UiContext _ctx = UiContext.Instance;

        public DashboardWindow()
        {
            InitializeComponent();
            DataContext = _ctx;

            // 강의실 PC 는 자리마다 IP 가 1씩 늘어나므로 IP 순으로 두면 칸 배치가 좌석 배치와 같아진다.
            // 같은 학생 목록을 다른 화면도 쓰므로 원본은 그대로 두고 이 화면의 보기만 줄 세운다.
            // 다시 접속해 IP 가 바뀌면 그 자리로 옮겨 간다.
            var byIp = new ListCollectionView(_ctx.Overview.Students) { CustomSort = new IpOrder() };
            byIp.IsLiveSorting = true;
            byIp.LiveSortingProperties.Add(nameof(StudentStatusViewModel.Ip));
            StudentCards.ItemsSource = byIp;

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

        // IP 를 숫자로 비교한다. 문자열로 비교하면 192.168.0.10 이 192.168.0.9 보다 앞에 온다.
        // IP 를 모르는 학생은 뒤로, IP 가 같으면 학번순으로 둔다.
        private sealed class IpOrder : IComparer
        {
            public int Compare(object? x, object? y)
            {
                var a = (StudentStatusViewModel)x!;
                var b = (StudentStatusViewModel)y!;
                int byIp = Key(a.Ip).CompareTo(Key(b.Ip));
                return byIp != 0 ? byIp : string.CompareOrdinal(a.StudentId, b.StudentId);
            }

            private static ulong Key(string ip)
            {
                if (!IPAddress.TryParse(ip, out IPAddress? address) || address.GetAddressBytes() is not { Length: 4 } bytes)
                    return ulong.MaxValue;
                return (ulong)bytes[0] << 24 | (ulong)bytes[1] << 16 | (ulong)bytes[2] << 8 | bytes[3];
            }
        }

        private void OnStudentsChanged(object? sender,
                                       System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
                foreach (StudentStatusViewModel student in e.NewItems) student.PropertyChanged += OnStudentChanged;
            if (e.OldItems != null)
                foreach (StudentStatusViewModel student in e.OldItems) student.PropertyChanged -= OnStudentChanged;

            UpdateEmptyNote();
        }

        // 학생 칸이 바뀌는 순간 잠깐 깜빡인다. 접속은 초록, 연결 끊김은 빨강, 답안 제출은 파랑.
        // 무채색 화면에서는 칸이 흐려지는 것만으로는 누가 방금 바뀌었는지 놓치기 쉽다.
        private void OnStudentChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not StudentStatusViewModel student) return;

            Color? color = e.PropertyName switch
            {
                nameof(StudentStatusViewModel.IsConnected) =>
                    student.IsConnected ? UiSignal.GoodSoftColor : UiSignal.AlertSoftColor,
                nameof(StudentStatusViewModel.IsAnswerSubmitted) when student.IsAnswerSubmitted =>
                    UiSignal.MessageSoftColor,
                _ => null,
            };
            if (color == null) return;

            // 칸이 아직 그려지기 전이면(학생이 막 들어오자마자 상태가 바뀐 경우) 깜빡일 것이 없다.
            // 그려지기 전에 템플릿 안을 찾으면 예외가 나 교수 앱이 통째로 죽는다.
            if (StudentCards.ItemContainerGenerator.ContainerFromItem(student) is ContentPresenter { IsLoaded: true } presenter &&
                presenter.ContentTemplate?.FindName("CardBox", presenter) is Border card)
                UiSignal.Blink(card, Border.BackgroundProperty, color.Value);
        }

        private void UpdateEmptyNote()
            => EmptyNote.Visibility = _ctx.Overview.Students.Count == 0
                                    ? Visibility.Visible : Visibility.Collapsed;

        private void StartWizard_Click(object sender, RoutedEventArgs e)
            => MainWindow.From(this)?.Navigate(new ExamWizard(), 1);

        // 학생 칸의 말풍선. 그 학생과의 1:1 대화를 열어 둔 채 알림·채팅 화면으로 넘어간다.
        private void Chat_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not StudentStatusViewModel student) return;

            var tab = _ctx.Chat.GetOrCreateTab(student.StudentId, student.Name, student.SessionId);
            MainWindow.From(this)?.Navigate(new AlertAndChat(tab), 4);
        }

        // 강의실 큰 모니터에 올려 둘 현황판을 띄운다.
        // 교수 창과 별개로 떠서 다른 모니터로 옮길 수 있다.
        private void OpenMonitorBoard_Click(object sender, RoutedEventArgs e)
            => DisplayBoardWindow.ShowSingle(Window.GetWindow(this));
    }
}
