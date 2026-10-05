using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using ExamManager.Shared;
using ProfessorUI.Service;
using ProfessorUI.ViewModel;
using ProfessorUI.Common;

namespace ProfessorUI.View.Professor
{
    public partial class DashboardWindow : UserControl
    {
        private readonly UiContext _ctx = UiContext.Instance;
        private readonly ListCollectionView _cards;

        public DashboardWindow()
        {
            InitializeComponent();
            DataContext = _ctx;

            // 수강생 명단을 불러왔으면 엑셀 순서대로, 아니면 IP 순으로 둔다(CardOrder).
            // 같은 칸 목록을 다른 화면도 쓸 수 있어 원본은 그대로 두고 이 화면의 보기만 줄 세운다.
            // 다시 접속해 IP 가 바뀌면 그 자리로 옮겨 가고, 명단에 없는 학생 칸은 접속이 끊기면 빠진다(IsShown).
            _cards = new ListCollectionView(_ctx.Board.Cards)
            {
                CustomSort = new CardOrder(),
                Filter = item => ((StudentCardViewModel)item).IsShown,
            };
            _cards.IsLiveSorting = true;
            _cards.LiveSortingProperties.Add(nameof(StudentCardViewModel.Ip));
            _cards.IsLiveFiltering = true;
            _cards.LiveFilteringProperties.Add(nameof(StudentCardViewModel.IsShown));
            StudentCards.ItemsSource = _cards;

            // 칸이 바뀔 때 깜빡이고 '아직 없습니다' 안내를 켜고 끈다.
            // 뒤로가기로 이 화면이 다시 올라올 수 있어, 보일 때마다 구독하고 떠날 때 푼다.
            Loaded += (_, _) =>
            {
                _ctx.Board.Cards.CollectionChanged += OnCardsChanged;
                ((INotifyCollectionChanged)_cards).CollectionChanged += OnShownCardsChanged;
                foreach (var card in _ctx.Board.Cards) card.PropertyChanged += OnCardChanged;
                UpdateEmptyNote();
            };
            Unloaded += (_, _) =>
            {
                _ctx.Board.Cards.CollectionChanged -= OnCardsChanged;
                ((INotifyCollectionChanged)_cards).CollectionChanged -= OnShownCardsChanged;
                foreach (var card in _ctx.Board.Cards) card.PropertyChanged -= OnCardChanged;
            };
        }

        // 명단 학생은 엑셀 순서(RosterIndex)대로 앞에, 명단에 없는 학생은 그 뒤에 IP 순으로 둔다.
        // 명단을 불러오지 않았으면 모두 뒤쪽 무리라 예전처럼 IP 순이다.
        // 강의실 PC 는 자리마다 IP 가 1씩 늘어나므로 IP 순으로 두면 칸 배치가 좌석 배치와 같아진다.
        // IP 를 숫자로 비교한다. 문자열로 비교하면 192.168.0.10 이 192.168.0.9 보다 앞에 온다.
        // IP 를 모르는 학생은 뒤로, IP 가 같으면 학번순으로 둔다.
        private sealed class CardOrder : IComparer
        {
            public int Compare(object? x, object? y)
            {
                var a = (StudentCardViewModel)x!;
                var b = (StudentCardViewModel)y!;
                int byRoster = a.RosterIndex.CompareTo(b.RosterIndex);
                if (byRoster != 0) return byRoster;
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

        // 새로 생긴 칸을 구독한다. 명단을 새로 불러오면 칸이 통째로 바뀌는데(비운 뒤 하나씩 더함),
        // 버려진 칸은 학생과 끊겨 더 알릴 것이 없으므로 따로 풀지 않는다.
        private void OnCardsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems == null) return;
            foreach (StudentCardViewModel card in e.NewItems) card.PropertyChanged += OnCardChanged;
        }

        private void OnShownCardsChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateEmptyNote();

        // 학생 칸이 바뀌는 순간 잠깐 깜빡인다. 접속은 초록, 연결 끊김은 빨강, 답안 제출은 파랑.
        // 칸 색만 바뀌어서는 강의실을 훑는 동안 누가 방금 바뀌었는지 놓치기 쉽다.
        private void OnCardChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not StudentCardViewModel card) return;

            Color? color = e.PropertyName switch
            {
                nameof(StudentCardViewModel.IsConnected) =>
                    card.IsConnected ? UiSignal.GoodSoftColor : UiSignal.AlertSoftColor,
                nameof(StudentCardViewModel.IsAnswerSubmitted) when card.IsAnswerSubmitted =>
                    UiSignal.MessageSoftColor,
                _ => null,
            };
            if (color == null) return;

            // 칸이 아직 그려지기 전이면(학생이 막 들어오자마자 상태가 바뀐 경우) 깜빡일 것이 없다.
            // 그려지기 전에 템플릿 안을 찾으면 예외가 나 교수 앱이 통째로 죽는다.
            if (StudentCards.ItemContainerGenerator.ContainerFromItem(card) is ContentPresenter { IsLoaded: true } presenter &&
                presenter.ContentTemplate?.FindName("CardBox", presenter) is Border box)
                UiSignal.Blink(box, Border.BackgroundProperty, color.Value);
        }

        private void UpdateEmptyNote()
            => EmptyNote.Visibility = _cards.IsEmpty ? Visibility.Visible : Visibility.Collapsed;

        private void StartWizard_Click(object sender, RoutedEventArgs e)
            => MainWindow.From(this)?.Navigate(new ExamWizard(), 1);

        // 학생 칸의 말풍선. 그 학생과의 1:1 대화를 열어 둔 채 알림·채팅 화면으로 넘어간다.
        private void Chat_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not StudentCardViewModel { Student: { } student }) return;

            var tab = _ctx.Chat.GetOrCreateTab(student.StudentId, student.Name, student.SessionId);
            MainWindow.From(this)?.Navigate(new AlertAndChat(tab), 4);
        }

        // 학생이 언제 들어오고 다시 들어왔는지 쌓아 둔 기록을 띄운다.
        // 현황 화면을 보면서 같이 띄워 둘 수 있게 따로 뜨는 창이다.
        private void OpenConnectionLog_Click(object sender, RoutedEventArgs e)
            => ConnectionLogWindow.ShowSingle(Window.GetWindow(this));

        // 학교 포털에서 받은 수강생 명단(엑셀)을 불러온다. 학번·이름 열만 읽고 나머지 열은 보지 않는다.
        // 다시 불러오면 새 명단으로 바뀐다. 칸이 바로 바뀌므로 성공은 따로 알리지 않는다.
        private void LoadRoster_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "수강생 명단 엑셀 파일을 고르세요",
                Filter = "엑셀 파일 (*.xlsx;*.xls)|*.xlsx;*.xls|모든 파일 (*.*)|*.*",
            };
            if (dialog.ShowDialog() != true) return;

            string? problem = StudentExcelStore.Load(dialog.FileName);
            if (problem != null)
                MessageBox.Show(problem, "명단 불러오기", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        // 강의실 큰 모니터에 올려 둘 현황판을 띄운다.
        // 교수 창과 별개로 떠서 다른 모니터로 옮길 수 있다.
        private void OpenMonitorBoard_Click(object sender, RoutedEventArgs e)
            => DisplayBoardWindow.ShowSingle(Window.GetWindow(this));
    }
}
