using System.Windows;
using System.Windows.Controls;
using ProfessorUI.ViewModel;

namespace ProfessorUI.View.Professor
{
    public partial class ScreenMonitoring : UserControl
    {
        public ScreenMonitoring()
        {
            InitializeComponent();
            DataContext = ScreenBoardViewModel.Instance;
        }

        // 타일 클릭 → 상세 보기 창 열기. 한 번에 한 창만 띄우고, 다른 학생이면 그 창을 갈아 끼운다.
        private void Tile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is StudentScreenViewModel item)
                DetailMonitoring.ShowSingle(item, Window.GetWindow(this));
        }

        // 전체 키보드 잠금 토글 버튼 클릭
        private void ToggleAllKeyboardLock_Click(object sender, RoutedEventArgs e)
        {
            ScreenBoardViewModel.Instance.ToggleAllKeyboardLock();
        }
    }
}
