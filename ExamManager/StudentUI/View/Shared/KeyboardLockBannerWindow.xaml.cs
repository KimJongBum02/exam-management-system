using System.Windows;

namespace StudentUI.View.Shared
{
    public partial class KeyboardLockBannerWindow : Window
    {
        public KeyboardLockBannerWindow()
        {
            InitializeComponent();
            Loaded += (_, _) =>
            {
                Left = (SystemParameters.PrimaryScreenWidth - ActualWidth) / 2;
                Top = 30;
            };
        }
    }
}
