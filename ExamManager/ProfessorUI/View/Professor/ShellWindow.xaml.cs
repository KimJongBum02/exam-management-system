using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Windows;
using ExamManager.Shared;
using System.Windows.Controls;
using System.Windows.Threading;
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
        // 수업 중에도 채팅·공지를 쓰므로 잠그지 않는다
        private readonly MenuEntry _chat = new("알림·채팅");
        private readonly MenuEntry _policy = new("프로그램 관리");
        private readonly MenuEntry _settle = new("종료 및 정산");
        // 시험 단계와 무관한 기능이라 잠그지 않는다
        private readonly MenuEntry _quiz       = new("OX 퀴즈");
        private readonly MenuEntry _monitoring = new("화면 모니터링");

        private readonly UiContext _ctx = UiContext.Instance;

        public ShellWindow()
        {
            InitializeComponent();

            MenuList.ItemsSource = new List<MenuEntry>
            {
                _dashboard, _prep, _manage, _chat, _policy, _settle, _quiz, _monitoring
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
        }

        // 시험 단계에 맞지 않는 메뉴를 잠그고, 잠긴 이유를 툴팁으로 남긴다.
        private void ApplyPhaseGates()
        {
            bool started = ExamState.IsExamStarted;                                  // 시험 시작 이후

            _prep.SetGate(!started, "시험이 시작되어 준비 단계는 끝났습니다. 지각생 파일 전송은 시험 관리 창의 파일 재배포를 쓰십시오.");
            _manage.SetGate(started, "시험을 시작하면 열립니다. 시험 준비 마법사를 4단계까지 진행하십시오.");
            // 먼저 답안을 낸 학생을 시험 중에 승인·종료해야 하므로 시험 종료를 기다리지 않고 연다.
            // 시험 중 [답안 일괄 수집]은 AnswerCollectViewModel 이 따로 막는다.
            _settle.SetGate(started, "시험을 시작하면 열립니다. 먼저 답안을 낸 학생은 시험 중에도 여기서 승인합니다.");
        }

        private void MenuList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PageHost == null) return;

            PageHost.Content = MenuList.SelectedIndex switch
            {
                0 => new DashboardPage(),
                1 => new ExamPrepPage(),
                2 => new ExamManagePage(),
                3 => new ChatPage(),
                4 => new SecurityPolicyPage(),
                5 => new ExamEndPage(),
                6 => new OXQuizPage(),
                7 => new ScreenMonitoringPage(),
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

        // 현재 화면이 속한 셸을 찾는다. 화면 쪽 코드비하인드에서 사용한다.
        public static ShellWindow? From(DependencyObject child)
            => Window.GetWindow(child) as ShellWindow;
    }
}
