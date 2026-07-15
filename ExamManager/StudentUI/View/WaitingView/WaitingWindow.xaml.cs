using System;
using System.Windows;
using System.Windows.Input;

namespace StudentUI.View.WaitingView
{
    public partial class WaitingWindow : Window
    {
        public WaitingWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Position the window at the top right of the primary screen
            var desktopWorkingArea = SystemParameters.WorkArea;
            this.Left = desktopWorkingArea.Right - this.Width - 10;
            this.Top = desktopWorkingArea.Top + 10;
        }

        private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Border 배경 영역에서만 드래그 - 버튼 클릭 시에는 발동하지 않음
            if (e.OriginalSource is System.Windows.Controls.Border ||
                e.OriginalSource is System.Windows.Controls.TextBlock ||
                e.OriginalSource is System.Windows.Controls.Grid)
            {
                this.DragMove();
            }
        }
    }
}
