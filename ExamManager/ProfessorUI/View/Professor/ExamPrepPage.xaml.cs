using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ProfessorUI.Service;

namespace ProfessorUI.View.Professor
{
    // 시험 준비 → 파일 배포 → 시험 시작 3단계 마법사.
    // 단계별 화면은 같은 XAML 안에 있고 보이기/숨기기로만 전환한다.
    //
    // 단계 이동에는 조건을 걸지 않는다. 스텝퍼를 눌러 어느 단계든 미리 볼 수 있고,
    // 실제로 실행할 수 있는지는 각 단계 안의 버튼과 자물쇠 안내가 알려 준다.
    public partial class ExamPrepPage : UserControl
    {
        private static readonly SolidColorBrush ActiveBorder = new(Color.FromRgb(0x2A, 0x2D, 0x31));
        private static readonly SolidColorBrush IdleBorder = new(Color.FromRgb(0xE5, 0xE7, 0xEB));

        private const int LastStep = 3;

        private readonly UiContext _ctx = UiContext.Instance;
        private int _step;

        public ExamPrepPage()
        {
            InitializeComponent();

            DataContext = _ctx;
            DownloadTable.ItemsSource = _ctx.Distribute.Students; // 배포 전용 행(진행률·선택 포함)

            // 앞 단계를 끝내야 열리는 단계에 자물쇠를 붙였다 뗀다.
            FileDeployState.StateChanged += UpdateStepLocks;
            Unloaded += (_, _) => FileDeployState.StateChanged -= UpdateStepLocks;

            ShowStep(1);
        }

        // ── 단계 이동 ─────────────────────────────────────────

        private void GoStep1_Click(object sender, RoutedEventArgs e) => ShowStep(1);
        private void GoStep2_Click(object sender, RoutedEventArgs e) => ShowStep(2);
        private void GoStep3_Click(object sender, RoutedEventArgs e) => ShowStep(3);

        private void GoPrev_Click(object sender, RoutedEventArgs e) => ShowStep(_step - 1);
        private void GoNext_Click(object sender, RoutedEventArgs e) => ShowStep(_step + 1);

        private void GoDashboard_Click(object sender, RoutedEventArgs e)
            => ShellWindow.From(this)?.Navigate(new DashboardPage(), 0);

        private void ShowStep(int step)
        {
            if (step < 1 || step > LastStep) return;
            _step = step;

            Step1Panel.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
            Step2Panel.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
            Step3Panel.Visibility = step == 3 ? Visibility.Visible : Visibility.Collapsed;

            MarkStep(Step1Box, Step1Name, step == 1);
            MarkStep(Step2Box, Step2Name, step == 2);
            MarkStep(Step3Box, Step3Name, step == 3);

            UpdateFooter();
            UpdateStepLocks();
        }

        // 현재 단계만 테두리와 글자를 진하게 해서 스텝퍼에서 구분되게 한다.
        private static void MarkStep(Button box, TextBlock name, bool active)
        {
            box.BorderBrush = active ? ActiveBorder : IdleBorder;
            box.BorderThickness = new Thickness(active ? 1.6 : 1);
            name.FontWeight = active ? FontWeights.Bold : FontWeights.Normal;
        }

        // 첫 단계에서는 이전이, 마지막 단계에서는 다음이 없다.
        // 마지막 단계의 오른쪽 버튼은 '다음' 대신 '시험 시작 실행'이 된다.
        private void UpdateFooter()
        {
            PrevButton.IsEnabled = _step > 1;
            PrevButton.Content = _step switch
            {
                2 => "이전: 시험 준비 단계",
                3 => "이전: 파일 배포 단계",
                _ => "이전"
            };

            bool last = _step == LastStep;
            NextButton.Visibility = last ? Visibility.Collapsed : Visibility.Visible;
            StartExamButton.Visibility = last ? Visibility.Visible : Visibility.Collapsed;

            NextButton.Content = _step == 1 ? "다음: 파일 배포 단계로 이동"
                                            : "다음: 시험 시작 단계로 이동";
        }

        // 아직 실행할 수 없는 단계에는 스텝퍼에 자물쇠를 띄운다.
        // 이동 자체는 막지 않는다 — 무엇이 남았는지 보러 갈 수 있어야 한다.
        private void UpdateStepLocks()
        {
            Step2Lock.Visibility = FileDeployState.IsFilePrepared
                                 ? Visibility.Collapsed : Visibility.Visible;
            Step3Lock.Visibility = FileDeployState.IsFileDistributed
                                 ? Visibility.Collapsed : Visibility.Visible;
        }

        // ── 시험 시작 ─────────────────────────────────────────

        // 목록 전달 · 시작 신호 · 압축 해제 요청은 ExamStartViewModel 이 순서대로 보낸다.
        // 그 안에서 단계가 InProgress 로 올라가야 좌측 메뉴의 시험 관리가 열리므로,
        // 명령을 먼저 실행하고 실제로 시작됐을 때만 화면을 옮긴다.
        private void StartExam_Click(object sender, RoutedEventArgs e)
        {
            var command = _ctx.ExamStart.StartExamCommand;
            if (!command.CanExecute(null)) return;

            command.Execute(null);

            if (ExamState.IsExamStarted)
                ShellWindow.From(this)?.Navigate(new ExamManagePage(), 2);
        }

        // 실행 파일 이름을 외우지 않아도 되도록 프로그램 목록에서 고르게 한다.
        private void PickWhite_Click(object sender, RoutedEventArgs e)
            => ProgramPickerWindow.PickInto(this, toWhiteList: true);

        private void PickBlack_Click(object sender, RoutedEventArgs e)
            => ProgramPickerWindow.PickInto(this, toWhiteList: false);
    }
}
