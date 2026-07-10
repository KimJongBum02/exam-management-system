using ProfessorUI.View.MonitoringView;
using ProfessorUI.ViewModel;
using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace ProfessorUI.View.SidebarView
{
    /// <summary>
    /// SidebarView.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class SidebarView : UserControl
    {
        // 듀얼 모니터 창이 중복해서 열리지 않도록 기억해두는 변수
        private MonitorWindow? _monitorWindow;

        public SidebarView()
        {
            InitializeComponent();
        }

        // 🖥️ 꼬마 버튼 클릭 시 실행되는 이벤트
        private void OpenMonitorWindow_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;

            if (_monitorWindow == null || !_monitorWindow.IsLoaded)
            {
                var mainVM = Application.Current.MainWindow.DataContext as MainViewModel;

                _monitorWindow = new MonitorWindow()
                {
                    // mainVM이 null이면 기본 생성자로 생성, 아니면 기존 인스턴스 사용
                    DataContext = mainVM?.MonitorVM ?? new MonitorViewModel(),
                    Owner = Application.Current.MainWindow
                };
                _monitorWindow.Show();
            }
            else
            {
                // 이미 창이 열려있으면 최소화되어 있는지 확인 후 화면 맨 앞으로 끌고 옴
                if (_monitorWindow.WindowState == WindowState.Minimized)
                {
                    _monitorWindow.WindowState = WindowState.Normal;
                }
                _monitorWindow.Activate();
            }
        }
    }
}
