using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using ExamManager.Shared;

namespace ProfessorUI.View.Professor
{
    // 부정행위 경고 목록 한 벌. 시험 관리 화면과 알림·채팅 화면에 같이 들어간다.
    public partial class AlertListPanel : UserControl
    {
        public AlertListPanel()
        {
            InitializeComponent();

            // 새 경고가 오면 목록을 잠깐 붉게 깜빡인다. 맨 위 줄이 방금 온 경고다.
            // 화면을 옮길 때마다 새로 만들어지므로, 떠날 때 구독을 푼다.
            var alerts = UiContext.Instance.Overview.Alerts;
            Loaded += (_, _) =>
            {
                alerts.CollectionChanged -= OnAlertsChanged;
                alerts.CollectionChanged += OnAlertsChanged;
            };
            Unloaded += (_, _) => alerts.CollectionChanged -= OnAlertsChanged;
        }

        private void OnAlertsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Add)
                UiSignal.Blink(Frame, Border.BackgroundProperty, UiSignal.AlertSoftColor);
        }

        // 누른 줄의 경고를 상세 화면으로 넘긴다. 상세 화면은 시험 관리 메뉴 아래에 있다.
        private void AlertDetail_Click(object sender, RoutedEventArgs e)
        {
            var alert = (sender as FrameworkElement)?.DataContext as Service.AlertItem;
            ShellWindow.From(this)?.Navigate(new CheatAlertDetailPage(alert), 2);
        }
    }
}
