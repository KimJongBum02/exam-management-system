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
using ProfessorUI.View.MonitoringView;

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
            // 리스트박스 아이템 클릭(메뉴 이동)이 중복으로 발생하지 않도록 이벤트 먹어치우기
            e.Handled = true;

            // 창이 없거나 닫혔으면 새로 생성
            if (_monitorWindow == null || !_monitorWindow.IsLoaded)
            {
                _monitorWindow = new MonitorWindow()
                {
                    // 듀얼 모니터용 뷰모델 연결
                    DataContext = new ProfessorUI.ViewModel.MonitorViewModel(),
                    // 🔌 [여기가 핵심!] 이 창의 주인은 메인 윈도우다! (메인창 닫히면 같이 닫힘)
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
