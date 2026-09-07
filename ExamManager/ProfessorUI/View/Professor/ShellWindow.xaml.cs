using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NetworkLib;
using ProfessorUI.Service;

namespace ProfessorUI.View.Professor
{
    // 새 교수 UI의 셸. 좌측 운영 메뉴와 화면 사이 이동만 담당한다.
    // 화면 안에서 다른 화면으로 넘어가는 경우(경고 상세, 종료 완료 현황 등)는 Navigate로 처리한다.
    //
    // 메뉴 잠금은 기존 ExamState.CurrentPhase 하나만 보고 결정한다.
    // 화면 안쪽 버튼 잠금(ExamStartViewModel 등)은 그대로 두고, 여기서 한 겹 더 앞을 막는 것이다.
    public partial class ShellWindow : Window
    {
        private readonly MenuEntry _dashboard = new("대시보드");
        private readonly MenuEntry _prep = new("시험 준비");
        private readonly MenuEntry _manage = new("시험 관리");
        private readonly MenuEntry _policy = new("보안 정책");
        private readonly MenuEntry _settle = new("종료 및 정산");
        // 시험 단계와 무관한 기능이라 잠그지 않는다
        private readonly MenuEntry _quiz = new("OX 퀴즈");

        private readonly UiContext _ctx = UiContext.Instance;

        public ShellWindow()
        {
            InitializeComponent();

            MenuList.ItemsSource = new List<MenuEntry> { _dashboard, _prep, _manage, _policy, _settle, _quiz };
            ApplyPhaseGates();
            MenuList.SelectedIndex = 0;

            DataContext = _ctx;
            PaneAlertList.ItemsSource = _ctx.Overview.Alerts;
            PaneChatList.ItemsSource = _ctx.Chat.RecentMessages;

            // 경고와 채팅은 서로 다른 곳에서 올라오므로 배지는 둘을 합쳐 센다.
            _ctx.Overview.Alerts.CollectionChanged += (_, _) => UpdateNotifications();
            _ctx.Chat.RecentMessages.CollectionChanged += (_, _) => UpdateNotifications();
            UpdateNotifications();

            ExamState.StateChanged += ApplyPhaseGates;
            Closed += (_, _) => ExamState.StateChanged -= ApplyPhaseGates;
        }

        // 시험 단계에 맞지 않는 메뉴를 잠그고, 잠긴 이유를 툴팁으로 남긴다.
        private void ApplyPhaseGates()
        {
            bool started = ExamState.IsExamStarted;                                  // 시험 시작 이후
            bool ended = ExamState.CurrentPhase >= ExamPhase.SubmitRequested;        // 시험 종료 이후

            _prep.SetGate(!started, "시험이 시작되어 준비 단계는 끝났습니다. 지각생 파일 전송은 시험 관리 창의 파일 재배포를 쓰십시오.");
            _manage.SetGate(started, "시험을 시작하면 열립니다. 시험 준비 마법사를 4단계까지 진행하십시오.");
            _settle.SetGate(ended, "시험을 종료하면 열립니다. 시험 관리 창에서 시험 종료를 실행하십시오.");
        }

        private void MenuList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PageHost == null) return;

            PageHost.Content = MenuList.SelectedIndex switch
            {
                0 => new DashboardPage(),
                1 => new ExamPrepPage(),
                2 => new ExamManagePage(),
                3 => new SecurityPolicyPage(),
                4 => new ExamEndPage(),
                5 => new OXQuizPage(),
                _ => PageHost.Content
            };

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
            // SelectionChanged가 기본 화면을 올린 뒤 원하는 화면으로 덮어쓴다.
            MenuList.SelectedIndex = menuIndex;
            PageHost.Content = page;
            ScrollPageToTop();
        }

        // 배지에는 미확인 경고와 안 읽은 채팅을 합쳐 적는다. 0이면 배지가 숨는다.
        private void UpdateNotifications()
        {
            int count = _ctx.Overview.UnreadAlertCount + _ctx.Chat.UnreadCount;
            NotifyButton.Tag = count.ToString();
            PaneAlertTitle.Text = $"부정행위 경고 (미확인 {_ctx.Overview.UnreadAlertCount})";
            PaneChatTitle.Text = $"학생 채팅 ({_ctx.Chat.UnreadCount})";
        }

        private void ToggleNotifyPane_Click(object sender, RoutedEventArgs e)
        {
            bool opening = NotifyPane.Visibility != Visibility.Visible;
            NotifyPane.Visibility = opening ? Visibility.Visible : Visibility.Collapsed;

            // 열어서 읽었으므로 안 읽음 표시를 지운다.
            if (opening)
            {
                _ctx.Chat.MarkAllRead();
                UpdateNotifications();
            }
        }

        // 알림 패널에서 바로 경고 상세로 넘어간다. 패널은 닫아 화면을 가리지 않게 한다.
        // 경고는 시험 중에만 올라오므로, 시험 관리가 잠긴 동안에는 이동하지 않는다.
        private void PaneAlertDetail_Click(object sender, RoutedEventArgs e)
        {
            if (!_manage.Enabled) return;

            var alert = (sender as FrameworkElement)?.DataContext as Service.AlertItem;
            NotifyPane.Visibility = Visibility.Collapsed;
            Navigate(new CheatAlertDetailPage(alert), 2);
        }

        // 현재 화면이 속한 셸을 찾는다. 화면 쪽 코드비하인드에서 사용한다.
        public static ShellWindow? From(DependencyObject child)
            => Window.GetWindow(child) as ShellWindow;
    }
}
