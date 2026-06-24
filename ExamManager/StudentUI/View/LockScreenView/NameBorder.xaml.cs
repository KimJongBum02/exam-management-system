using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace StudentUI.View.LockScreenView
{
    public partial class NameBorder : UserControl
    {
        public static readonly DependencyProperty LogoutCommandProperty =
            DependencyProperty.Register("LogoutCommand", typeof(ICommand), typeof(NameBorder));

        public ICommand LogoutCommand
        {
            get => (ICommand)GetValue(LogoutCommandProperty);
            set => SetValue(LogoutCommandProperty, value);
        }

        public NameBorder()
        {
            InitializeComponent();
        }

        private void LogoutButton_Click(object sender, RoutedEventArgs e)
        {
            LogoutCommand?.Execute(null);
        }
    }
}