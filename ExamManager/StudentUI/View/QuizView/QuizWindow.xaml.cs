using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using StudentUI.Service;

namespace StudentUI.View.QuizView
{
    // 교수가 낸 OX 문제를 띄우고 답을 받는 작은 창.
    //
    // 시험 화면·대기 화면 어디에 있든 떠야 하므로 별도 창으로 둔다.
    //
    // 닫기 버튼을 일부러 두지 않았다. 답을 반드시 받아야 하는 기능이라
    // 학생이 그냥 치우고 넘어갈 수 있으면 안 된다.
    // 답을 내면 2초 뒤 스스로 닫힌다 — 접수됐다는 문구를 읽을 시간만 주고 비켜 준다.
    public partial class QuizWindow : Window
    {
        private readonly DispatcherTimer _closeTimer = new() { Interval = TimeSpan.FromSeconds(2) };

        public QuizWindow(string question)
        {
            InitializeComponent();

            QuestionText.Text = question;
            StatusText.Text = "O 또는 X 를 고르십시오.";

            _closeTimer.Tick += (_, _) => { _closeTimer.Stop(); Close(); };
        }

        private void AnswerO_Click(object sender, RoutedEventArgs e) => Submit(true);
        private void AnswerX_Click(object sender, RoutedEventArgs e) => Submit(false);

        private void Submit(bool answer)
        {
            QuizService.Instance.Answer(answer);

            // 한 번만 낼 수 있다. 교수 쪽도 먼저 온 응답만 인정한다.
            AnswerButtons.IsEnabled = false;
            StatusText.Text = $"{(answer ? "O" : "X")} 로 제출했습니다.";

            _closeTimer.Start();
        }

        // 테두리를 잡고 창을 옮길 수 있게 한다. 문제가 화면을 가릴 때를 위한 것이다.
        private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }
    }
}
