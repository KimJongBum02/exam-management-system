using System.Windows;
using System.Windows.Controls;
using ProfessorUI.ViewModel;

namespace ProfessorUI.View.Professor
{
    public partial class ScreenMonitoringPage : UserControl
    {
        public ScreenMonitoringPage()
        {
            InitializeComponent();
            DataContext = ScreenMonitoringViewModel.Instance;
        }

        // 타일 클릭 → 상세 보기 창 열기
        private void Tile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is StudentScreenItem item)
            {
                var detail = new ScreenDetailWindow(item)
                {
                    Owner = Window.GetWindow(this)
                };
                detail.Show();
            }
        }
    }
}
