using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using NetworkLib;
using ProfessorUI.Service;
using ProfessorUI.Common;
using ProfessorUI.ViewModel;

namespace ProfessorUI.View.Professor
{
    public partial class ExamEndWindow : UserControl
    {
        private readonly UiContext _ctx = UiContext.Instance;

        // 표에 보일 줄을 거르는 이 화면 전용 뷰.
        // 기본 뷰(CollectionViewSource.GetDefaultView)를 쓰지 않는다 — 그것은 같은 학생 목록을 쓰는
        // 다른 화면(배포 표·대시보드)과 공유되어, 여기서 거르면 거기까지 함께 걸러진다.
        private readonly ListCollectionView _collectView;

        public ExamEndWindow()
        {
            InitializeComponent();

            DataContext = _ctx;

            _collectView = new ListCollectionView(_ctx.Overview.Students);
            CollectTable.ItemsSource = _collectView;

            // 걸러 보는 중에 답안이 들어오면 그 줄은 표에서 빠져야 한다. 줄 하나가 바뀌는 것만으로는
            // 뷰가 다시 거르지 않으므로(ListCollectionView 의 IsLiveFiltering 은 이 목록에서 듣지 않았다)
            // 학생 변화를 이미 모아 듣고 있는 집계 뷰모델에 얹어 그때 다시 거른다.
            // 화면은 메뉴를 옮길 때마다 새로 만들어지므로, 떠날 때 반드시 끊는다.
            Loaded += (_, _) =>
            {
                _ctx.Overview.PropertyChanged -= OnOverviewChanged;
                _ctx.Overview.PropertyChanged += OnOverviewChanged;
            };
            Unloaded += (_, _) => _ctx.Overview.PropertyChanged -= OnOverviewChanged;
        }

        private void OnOverviewChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (_collectView.Filter != null) _collectView.Refresh();
        }

        // [미수집만 보기] — 답안이 아직 없는 학생만 남긴다.
        // 끝까지 걷지 못한 학생은 찾아가 봐야 하므로, 수집이 끝난 학생을 숨겨 남은 사람만 보이게 한다.
        private void OnlyMissing_Changed(object sender, RoutedEventArgs e)
        {
            if (OnlyMissing.IsChecked == true)
                _collectView.Filter = item => item is StudentStatusViewModel student && !student.IsAnswerSubmitted;
            else
                _collectView.Filter = null;
        }

        // 승인할 수 없는 학생(미수집·이미 처리)은 전체 선택에서도 빠진다.
        // 고르는 것은 지금 표에 보이는 줄만이다 — [미수집만 보기] 로 숨긴 학생까지 골라 두면
        // 표에는 체크가 없는데 버튼의 인원 수만 올라가, 무엇이 처리되는지 알 수 없다.
        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (StudentStatusViewModel student in _collectView)
                student.IsSelected = student.CanApprove;
        }

        // 푸는 것은 숨은 줄까지 전부 푼다. 숨은 줄에 체크가 남으면 위와 같은 일이 생긴다.
        private void DeselectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var student in _ctx.Overview.Students)
                student.IsSelected = false;
        }

        // [엑셀로 저장] — 따로 있던 시험 로그 화면에서 이 화면으로 옮겨 온 버튼.
        // 저장까지 끝났으면 기억해 둔다. 두 곳이 그 기록을 본다 —
        // 아래 [처음 화면으로] 잠금(ExamState.IsLogSaved)과 프로그램을 끌 때의 확인(ExamLogViewModel.IsSaved).
        private void SaveExcel_Click(object sender, RoutedEventArgs e)
        {
            if (ExamLogExport.Save(_ctx.ExamLog.Rows)) _ctx.ExamLog.MarkSaved();
        }

        // [처음 화면으로] — 이번 시험 기록을 정리하고 시험 준비 마법사 1단계로 돌아간다.
        // 되돌릴 수 없는 일이라 무엇이 지워지는지 적어 두고 한 번 묻는다.
        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            // 시험 로그를 남기지 않았으면 버튼이 잠겨 있어 여기까지 오지 않는다(ExamSummaryViewModel.CanReset).
            if (!_ctx.Overview.CanReset) return;

            string notice =
                "이번 시험 기록을 초기화 합니다. 초기화 사항은 이렇습니다.\n" +
                " · 대시보드\n" +
                " · 시험 준비\n" +
                " · 시험 관리\n" +
                " · 시험 종료·답안 수집\n" +
                " · 채팅\n" +
                " · 프로그램 관리\n" +
                " · 화면 모니터링\n" +
                " · 퀴즈 · 설문 (설문 응답은 저장되지 않아 다시 볼 수 없습니다)\n" +
                " · 화면 모드를 자유 모드로 변경";

            // 끝내 답안을 걷지 못한 학생은 정리하면 표에서도 지워진다. 마지막으로 한 번 더 보여 준다.
            // 대상은 [미수집 학생 다시 수집] 과 같다 — 이번 시험을 치른 학생 중 답안이 없는 학생이다.
            var missing = _ctx.Overview.Students
                .Where(s => !s.IsAnswerSubmitted && (s.HasEverStarted || s.IsFileReceived))
                .ToList();
            if (missing.Count > 0)
                notice += $"\n답안을 걷지 못한 학생 {missing.Count}명:\n" +
                          string.Join("\n", missing.Select(s => $"  {s.StudentId} {s.Name}")) + "\n";

            if (MessageBox.Show(notice + "\n이 작업은 되돌릴 수 없습니다. 계속하시겠습니까?",
                                "처음 화면으로", MessageBoxButton.YesNo, MessageBoxImage.Warning,
                                MessageBoxResult.No) != MessageBoxResult.Yes)
                return;

            // 낸 퀴즈·설문의 응답 기록도 비운다(작성 칸은 UiContext.ResetForNextExam 이 비운다).
            // 퀴즈는 비우기 전에 엑셀로 한 번 더 저장되고, 저장하지 못하면 비우지 않는다(QuizService.ClearSession).
            // 그때는 알리고 나머지 정리는 그대로 진행한다 — 퀴즈 파일 하나 때문에 다음 시험이 잠기면 안 된다.
            if (!QuizService.Instance.ClearSession())
                MessageBox.Show("퀴즈 기록을 파일로 저장할 수 없어 비우지 못했습니다. " +
                                $"해당 경로에 저장할 수 있는지 확인해 주세요.\n\n{QuizService.SessionFolder}",
                                "퀴즈 기록", MessageBoxButton.OK, MessageBoxImage.Warning);
            SurveyService.Instance.ClearSession();

            _ctx.ResetForNextExam();

            // 시험이 끝났으니 학생 PC 를 잠근 채 두지 않는다. 남겨 두면 다음 학생이 접속하는 순간
            // 그 모드를 그대로 받는다(ClassModeState.SendOnConnect). 상단바 표시까지 함께 맞추려고
            // 교수가 직접 누를 때와 같은 길을 쓴다.
            MainWindow.From(this)?.SetClassMode(ClassMode.Free);

            // 정리하면 이 화면의 메뉴는 다시 잠긴다(MainWindow.ApplyPhaseGates). 머물러 있을 자리가 아니므로
            // 버튼 이름 그대로 시험 준비 마법사 1단계로 보낸다.
            MainWindow.From(this)?.Navigate(new ExamWizard(), 1);
        }
    }
}
