using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ProfessorUI.View.Professor
{
    // 대기 → 준비 → 파일 배포 → 시험 시작 4단계 마법사.
    // 단계별 화면은 같은 XAML 안에 있고 보이기/숨기기로만 전환한다.
    public partial class ExamPrepPage : UserControl
    {
        public ExamPrepPage()
        {
            InitializeComponent();

            var ctx = UiContext.Instance;
            DataContext = ctx;

            ConnectTable.ItemsSource = ctx.Overview.Students;   // 접속한 학생 원본
            DownloadTable.ItemsSource = ctx.Distribute.Students; // 배포 전용 행(진행률·선택 포함)

            ShowStep(1);
        }

        private void GoStep1_Click(object sender, RoutedEventArgs e) => ShowStep(1);
        private void GoStep2_Click(object sender, RoutedEventArgs e) => ShowStep(2);
        private void GoStep3_Click(object sender, RoutedEventArgs e) => ShowStep(3);
        private void GoStep4_Click(object sender, RoutedEventArgs e) => ShowStep(4);

        // 시험 시작 실행.
        // 목록 전달 · 시작 신호 · 압축 해제 요청은 ExamStartViewModel 이 순서대로 보낸다.
        // 그 안에서 단계가 InProgress 로 올라가야 좌측 메뉴의 시험 관리가 열리므로,
        // 명령을 먼저 실행하고 실제로 시작됐을 때만 화면을 옮긴다.
        private void StartExam_Click(object sender, RoutedEventArgs e)
        {
            var command = UiContext.Instance.ExamStart.StartExamCommand;
            if (!command.CanExecute(null)) return;

            command.Execute(null);

            if (Service.ExamState.IsExamStarted)
                ShellWindow.From(this)?.Navigate(new ExamManagePage(), 2);
        }

        private void ShowStep(int step)
        {
            Step1Panel.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
            Step2Panel.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
            Step3Panel.Visibility = step == 3 ? Visibility.Visible : Visibility.Collapsed;
            Step4Panel.Visibility = step == 4 ? Visibility.Visible : Visibility.Collapsed;

            MarkStep(Step1Box, Step1Name, step == 1);
            MarkStep(Step2Box, Step2Name, step == 2);
            MarkStep(Step3Box, Step3Name, step == 3);
            MarkStep(Step4Box, Step4Name, step == 4);
        }

        // 현재 단계만 테두리와 글자를 진하게 해서 스텝퍼에서 구분되게 한다.
        private static void MarkStep(Border box, TextBlock name, bool active)
        {
            box.BorderBrush = active ? new SolidColorBrush(Color.FromRgb(0x2A, 0x2D, 0x31))
                                     : new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB));
            box.BorderThickness = new Thickness(active ? 1.6 : 1);
            name.FontWeight = active ? FontWeights.Bold : FontWeights.Normal;
        }

        // 실행 파일 이름을 외우지 않아도 되도록 프로그램 목록에서 고르게 한다.
        private void PickWhite_Click(object sender, RoutedEventArgs e)
            => ProgramPickerWindow.PickInto(this, toWhiteList: true);

        private void PickBlack_Click(object sender, RoutedEventArgs e)
            => ProgramPickerWindow.PickInto(this, toWhiteList: false);

    }
}
