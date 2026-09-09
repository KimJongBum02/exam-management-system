using System.ComponentModel;
using System.Windows;
using ProfessorUI.ViewModel;

namespace ProfessorUI.View.Professor
{
    public partial class ScreenDetailWindow : Window
    {
        private readonly StudentScreenItem _item;

        public ScreenDetailWindow(StudentScreenItem item)
        {
            InitializeComponent();
            _item = item;

            Title           = $"화면 상세 보기 – {item.DisplayName}";
            NameText.Text   = item.DisplayName;
            UpdatedText.Text = $"마지막 수신: {item.LastUpdated}";
            ScreenImage.Source = item.Screen;

            if (item.Screen == null)
                WaitingText.Visibility = Visibility.Visible;
            else
                WaitingText.Visibility = Visibility.Collapsed;

            // 새 화면이 들어올 때마다 자동 갱신
            _item.PropertyChanged += Item_PropertyChanged;
            Closed += (_, _) => _item.PropertyChanged -= Item_PropertyChanged;
        }

        private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(StudentScreenItem.Screen)) return;
            Dispatcher.Invoke(() =>
            {
                ScreenImage.Source      = _item.Screen;
                UpdatedText.Text        = $"마지막 수신: {_item.LastUpdated}";
                WaitingText.Visibility  = _item.HasScreen ? Visibility.Collapsed : Visibility.Visible;
            });
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
