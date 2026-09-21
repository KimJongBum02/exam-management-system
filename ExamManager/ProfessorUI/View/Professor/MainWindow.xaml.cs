using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Windows;
using ExamManager.Shared;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ProfessorUI.Service;
using ProfessorUI.Common;
using ProfessorUI.ViewModel;

namespace ProfessorUI.View.Professor
{
    // 새 교수 UI의 셸. 좌측 운영 메뉴와 화면 사이 이동만 담당한다.
    // 화면 안에서 다른 화면으로 넘어가는 경우(경고 상세, 종료 완료 현황 등)는 Navigate로 처리한다.
    //
    // 메뉴 잠금은 기존 ExamState.CurrentPhase 하나만 보고 결정한다.
    // 화면 안쪽 버튼 잠금(ExamStartViewModel 등)은 그대로 두고, 여기서 한 겹 더 앞을 막는 것이다.
    public partial class MainWindow : Window
    {
        private readonly MenuEntryViewModel _dashboard = new("대시보드");
        private readonly MenuEntryViewModel _prep = new("시험 준비");
        private readonly MenuEntryViewModel _manage = new("시험 관리");
        // 수업 중에도 채팅·공지를 쓰므로 잠그지 않는다
        private readonly MenuEntryViewModel _chat = new("알림·채팅");
        private readonly MenuEntryViewModel _policy = new("프로그램 관리");
        private readonly MenuEntryViewModel _settle = new("종료 및 정산");
        // 시험 단계와 무관한 기능이라 잠그지 않는다
        private readonly MenuEntryViewModel _quiz       = new("OX 퀴즈");
        private readonly MenuEntryViewModel _monitoring = new("화면 모니터링");
        // 지난 기록을 보는 화면이라 시험 전에도 열어 둔다 (표가 비어 있을 뿐이다)
        private readonly MenuEntryViewModel _examLog    = new("시험 로그");

        private readonly UiContext _ctx = UiContext.Instance;

        // 거쳐 온 화면. 뒤로가기가 뒤에서부터 하나씩 꺼내 되돌린다.
        // 화면을 통째로 들고 있으므로 돌아갔을 때 검색어·스크롤 같은 것이 그대로 남는다.
        //
        // 오래된 것부터 버리기 때문에 Stack 이 아니라 List 로 둔다.
        // 화면은 학생 목록을 구독하고 있어, 한 시험 내내 쌓아 두면 버려진 화면들이 계속 따라 움직인다.
        private readonly List<(UserControl Page, int MenuIndex)> _history = new();

        // 되돌아갈 수 있는 화면 수. 이만큼이면 한 번에 파고든 깊이를 모두 되짚을 수 있다.
        private const int MaxHistory = 20;

        // 지금 화면이 어느 메뉴의 것인지. 하위 화면(경고 상세 등)은 부모 메뉴 번호를 쓴다.
        private int _currentMenuIndex;

        // 뒤로 가는 중이거나 Navigate 가 곧 원하는 화면을 올릴 때는
        // 메뉴 선택이 기본 화면을 띄우지 않도록 잠시 막는다.
        private bool _suppressMenuPage;

        public MainWindow()
        {
            InitializeComponent();

            MenuList.ItemsSource = new List<MenuEntryViewModel>
            {
                _dashboard, _prep, _manage, _settle, _chat, _policy, _quiz, _monitoring, _examLog
            };
            ApplyPhaseGates();
            MenuList.SelectedIndex = 0;

            DataContext = _ctx;

            // 경고·채팅이 오면 창이 뒤에 있거나 최소화돼 있어도 알 수 있게 작업표시줄 아이콘을 깜빡인다.
            _ctx.Overview.Alerts.CollectionChanged += (_, e) =>
            {
                if (e.Action == NotifyCollectionChangedAction.Add) UiSignal.FlashTaskbar();
            };
            _ctx.Chat.RecentMessages.CollectionChanged += (_, e) =>
            {
                if (e.Action == NotifyCollectionChangedAction.Add) UiSignal.FlashTaskbar();
            };

            ExamState.StateChanged += ApplyPhaseGates;
            Closed += (_, _) => ExamState.StateChanged -= ApplyPhaseGates;

            // 화면 안의 목록·대화창이 휠을 삼켜도 전체 화면이 내려가게 한다.
            // 이미 처리된 휠까지 받아야 하므로 handledEventsToo 로 단다.
            PageScroll.AddHandler(MouseWheelEvent, new MouseWheelEventHandler(OnPageWheel), true);
        }

        // 휠 한 칸에 움직일 거리. WPF 기본값(세 줄)과 비슷하게 맞춘다.
        private const double WheelStep = 48;

        // 안쪽 스크롤은 끝에 닿아도 휠을 자기가 처리한 것으로 표시해 버린다.
        // 그래서 바깥 화면이 더 내려갈 곳이 있어도 멈춰 버린다 — 그 몫을 여기서 대신 움직인다.
        private void OnPageWheel(object sender, MouseWheelEventArgs e)
        {
            // 휠이 놓인 자리에서 바깥으로 훑는다.
            bool swallowedInside = false;
            for (DependencyObject? node = e.OriginalSource as DependencyObject;
                 node != null && node != PageScroll;
                 node = ParentOf(node))
            {
                if (node is not ScrollViewer inner) continue;

                // 안쪽이 아직 더 움직일 수 있으면 그쪽 몫이다
                if (CanScrollFurther(inner, e.Delta)) return;
                swallowedInside = true;
            }

            // 안쪽에 스크롤이 없었다면 PageScroll 이 이미 스스로 움직였다
            if (!swallowedInside || !CanScrollFurther(PageScroll, e.Delta)) return;

            PageScroll.ScrollToVerticalOffset(PageScroll.VerticalOffset - Math.Sign(e.Delta) * WheelStep);
        }

        // 글자 조각(Run·Hyperlink)처럼 그리기 나무에 없는 것이 올라올 수 있어 둘 다 살핀다.
        private static DependencyObject? ParentOf(DependencyObject node)
            => node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);

        private static bool CanScrollFurther(ScrollViewer scroll, int delta)
            => delta < 0 ? scroll.VerticalOffset < scroll.ScrollableHeight
                         : scroll.VerticalOffset > 0;

        // 시험 단계에 맞지 않는 메뉴를 잠그고, 잠긴 이유를 툴팁으로 남긴다.
        private void ApplyPhaseGates()
        {
            bool started = ExamState.IsExamStarted;                                  // 시험 시작 이후

            // 시험을 끝내면 다음 시험을 준비할 수 있도록 준비 화면이 다시 열린다.
            _prep.SetGate(!ExamState.IsExamRunning, "시험이 진행 중입니다. 지각생 파일 전송은 시험 관리 창의 파일 재배포를 쓰십시오.");
            _manage.SetGate(started, "시험을 시작하면 열립니다. 시험 준비 마법사를 4단계까지 진행하십시오.");
            // 먼저 답안을 낸 학생을 시험 중에 승인·종료해야 하므로 시험 종료를 기다리지 않고 연다.
            // 시험 종료·답안 수집 버튼은 ExamEndViewModel 이 시험 단계에 따라 따로 막는다.
            _settle.SetGate(started, "시험을 시작하면 열립니다. 먼저 답안을 낸 학생은 시험 중에도 여기서 승인합니다.");
        }

        // 포트는 9000 을 그대로 쓰는 것이 기본이다. 그 자리를 다른 프로그램이 쓰고 있을 때만 여기서 바꾼다.
        private void PortButton_Click(object sender, RoutedEventArgs e)
        {
            new PortWindow { Owner = this }.ShowDialog();
        }

        private void MenuList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PageHost == null || _suppressMenuPage) return;

            var page = MenuPageFor(MenuList.SelectedIndex);
            if (page != null) ShowPage(page, MenuList.SelectedIndex);
        }

        // 메뉴 한 줄이 여는 기본 화면.
        private static UserControl? MenuPageFor(int menuIndex) => menuIndex switch
        {
            0 => new DashboardWindow(),
            1 => new ExamWizard(),
            2 => new ExamManagePage(),
            3 => new ExamEndWindow(),
            4 => new AlertAndChat(),
            5 => new ProgramManageWindow(),
            6 => new QuizWindow(),
            7 => new ScreenMonitoring(),
            8 => new ExamLogWindow(),
            _ => null
        };

        // 화면을 갈아 끼우면서 지금 보던 화면을 뒤로가기 기록에 남긴다.
        private void ShowPage(UserControl page, int menuIndex)
        {
            if (PageHost.Content is UserControl current)
            {
                _history.Add((current, _currentMenuIndex));
                if (_history.Count > MaxHistory) _history.RemoveAt(0);
            }

            PageHost.Content = page;
            _currentMenuIndex = menuIndex;
            NavHistory.CanGoBack = _history.Count > 0;

            ScrollPageToTop();
        }

        // 직전 화면으로 되돌린다. 기록에서 꺼내는 것이라 여기서는 새로 쌓지 않는다.
        // 좌측 메뉴 표시도 그때의 메뉴로 함께 되돌린다.
        public void GoBack()
        {
            if (_history.Count == 0) return;

            var (page, menuIndex) = _history[^1];
            _history.RemoveAt(_history.Count - 1);

            _suppressMenuPage = true;
            MenuList.SelectedIndex = menuIndex;
            _suppressMenuPage = false;

            PageHost.Content = page;
            _currentMenuIndex = menuIndex;
            NavHistory.CanGoBack = _history.Count > 0;

            ScrollPageToTop();
        }

        // 화면을 바꾸면 항상 맨 위부터 보여 준다.
        // 새 화면 안의 컨트롤이 포커스를 받으면서 스크롤이 내려가는 경우가 있어,
        // 배치가 끝난 뒤에 되돌린다.
        private void ScrollPageToTop()
            => Dispatcher.BeginInvoke(new Action(() => PageScroll.ScrollToTop()),
                                      DispatcherPriority.Loaded);

        // 화면 안의 버튼에서 다른 화면으로 이동할 때 사용한다.
        // 좌측 메뉴 표시도 함께 맞춰 준다(하위 화면이면 부모 메뉴를 선택 상태로 둔다).
        public void Navigate(UserControl page, int menuIndex)
        {
            // 메뉴 선택이 기본 화면을 띄우지 않게 막아 둔다.
            // 그대로 두면 기본 화면이 한 번 올라갔다가 덮이면서 뒤로가기 기록에도 끼어든다.
            _suppressMenuPage = true;
            MenuList.SelectedIndex = menuIndex;
            _suppressMenuPage = false;

            ShowPage(page, menuIndex);
        }

        // 현재 화면이 속한 셸을 찾는다. 화면 쪽 코드비하인드에서 사용한다.
        public static MainWindow? From(DependencyObject child)
            => Window.GetWindow(child) as MainWindow;
    }
}
