using System.Windows;
using System.Windows.Input;

namespace StudentUI.View.Shared
{
    public partial class ExamSubmitConfirmDialog : Window
    {
        public ExamSubmitConfirmDialog(string examFolderName, string examFolderPath, string studentInfo)
        {
            InitializeComponent();

            ExamFolderNameText.Text = string.IsNullOrWhiteSpace(examFolderName) ? "시험 파일" : examFolderName;
            ExamFolderPathText.Text = string.IsNullOrWhiteSpace(examFolderPath) ? "-" : examFolderPath;
            StudentInfoText.Text = string.IsNullOrWhiteSpace(studentInfo) ? "-" : studentInfo;
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void YesButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void NoButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
