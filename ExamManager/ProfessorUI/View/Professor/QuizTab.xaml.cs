using System.Windows;
using System.Windows.Controls;

namespace ProfessorUI.View.Professor
{
    // 퀴즈 메뉴. 시험 단계와 별개라 메뉴가 항상 열려 있다.
    //
    // 수업 확인 퀴즈(QuizWindow, OX·N지선다를 섞어 여러 문제를 한 번에 내고 채점)와
    // 설문(SurveyWindow, 정답 없이 O·X 만 모음)을 탭으로 나눈다.
    public partial class QuizTab : UserControl
    {
        // 메뉴를 옮겼다 돌아와도 보던 탭을 그대로 연다.
        private static bool _surveySelected;

        public QuizTab()
        {
            InitializeComponent();

            if (_surveySelected) SurveyButton.IsChecked = true;
            else QuizButton.IsChecked = true;
        }

        // 탭을 바꿀 때마다 새로 만든다. 각 탭은 떠날 때(Unloaded) 구독을 풀고 기록을 저장한다.
        private void Tab_Checked(object sender, RoutedEventArgs e)
        {
            _surveySelected = SurveyButton.IsChecked == true;
            TabHost.Content = _surveySelected ? new SurveyWindow() : new QuizWindow();
        }
    }
}
