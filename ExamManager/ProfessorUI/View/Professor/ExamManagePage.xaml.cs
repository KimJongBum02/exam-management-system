using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using ProfessorUI.ViewModel;

namespace ProfessorUI.View.Professor
{
    // 시험 진행 중 허브 화면. 경고 상세 / 프로그램 관리 / 종료 화면으로 갈라진다.
    public partial class ExamManagePage : UserControl
    {
        private readonly UiContext _ctx = UiContext.Instance;
        private readonly ListCollectionView _studentView;

        public ExamManagePage()
        {
            InitializeComponent();

            DataContext = _ctx;

            // 재배포가 필요한 학생만 거른다.
            // 종료·정산 화면도 같은 학생 목록을 쓰므로 기본 보기(GetDefaultView)에 필터를 걸면
            // 그 화면 표까지 걸러진다. 이 화면만의 보기를 따로 만든다.
            _studentView = new ListCollectionView(_ctx.Overview.Students) { Filter = MatchesFilter };

            // 학생이 시험을 시작하거나 끊기면 목록에서 바로 빠지고 들어온다.
            _studentView.IsLiveFiltering = true;
            _studentView.LiveFilteringProperties.Add(nameof(StudentItemViewModel.HasExamStarted));
            _studentView.LiveFilteringProperties.Add(nameof(StudentItemViewModel.IsAnswerSubmitted));
            _studentView.LiveFilteringProperties.Add(nameof(StudentItemViewModel.IsConnected));

            StudentTable.ItemsSource = _studentView;
            ((INotifyCollectionChanged)_studentView).CollectionChanged += (_, _) => UpdateEmptyNote();
            UpdateEmptyNote();
        }

        private bool MatchesFilter(object item)
        {
            if (item is not StudentItemViewModel student) return false;

            // 재배포 대상: 답안을 내지 않았고, 이 접속에서 시험을 시작하지 못한 학생.
            // (늦게 들어왔거나, 파일을 받기 전에 끊겼거나, 다시 켰는데 이어 받을 기록이 없는 학생)
            if (student.IsAnswerSubmitted || student.HasExamStarted) return false;

            string keyword = SearchBox?.Text?.Trim() ?? string.Empty;
            if (keyword.Length > 0 &&
                !student.StudentId.Contains(keyword) &&
                !student.Name.Contains(keyword))
                return false;

            string conn = SelectedText(ConnFilter);
            if (conn == "접속 중" && !student.IsConnected) return false;
            if (conn == "미접속" && student.IsConnected) return false;

            return true;
        }

        private static string SelectedText(ComboBox? box)
            => (box?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? string.Empty;

        private void Filter_Changed(object sender, RoutedEventArgs e) => _studentView?.Refresh();

        private void UpdateEmptyNote()
            => EmptyNote.Visibility = _studentView.IsEmpty ? Visibility.Visible : Visibility.Collapsed;

        // 목록에 보이는 학생 중 접속 중인 학생에게 모두 다시 보낸다.
        // 끊긴 학생은 보낼 수 없으므로 뺀다 — 다시 들어오면 그때 보낸다.
        private void RedeployAll_Click(object sender, RoutedEventArgs e)
        {
            var targets = _studentView.Cast<StudentItemViewModel>().Where(s => s.IsConnected).ToList();
            if (targets.Count == 0)
            {
                MessageBox.Show("다시 보낼 수 있는 학생이 없습니다.\n접속이 끊긴 학생은 다시 접속한 뒤에 보낼 수 있습니다.",
                                "재배포", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _ctx.Distribute.RedeployMany(targets);
        }

        private void SecurityPolicy_Click(object sender, RoutedEventArgs e)
            => ShellWindow.From(this)?.Navigate(new SecurityPolicyPage(), 5);

        // 시험 종료 실행. 학생 PC의 감시를 멈추는 신호까지 AnswerCollectViewModel 이 보낸다.
        // 교수가 확인 창에서 취소하면 단계가 그대로이므로 화면도 옮기지 않는다.
        // 끝난 뒤에는 다음 시험을 준비할 수 있도록 시험 준비 화면으로 돌아간다.
        // 답안 수집과 승인은 좌측 [종료 및 정산] 메뉴에서 이어서 한다.
        private void EndExam_Click(object sender, RoutedEventArgs e)
        {
            var command = _ctx.AnswerCollect.EndExamCommand;
            if (!command.CanExecute(null)) return;

            command.Execute(null);

            if (Service.ExamState.CurrentPhase >= NetworkLib.ExamPhase.SubmitRequested)
                ShellWindow.From(this)?.Navigate(new ExamPrepPage(), 1);
        }
    }
}
