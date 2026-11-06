using System.Windows;
using System.Windows.Input;
using ExamManager.Shared;
using StudentUI.Service;

namespace StudentUI.View.Exam
{
    // 시험 화면을 내려놓고 편집기에서 실습을 해도 남은 시간이 보이도록, 항상 위에 뜨는 작은 창.
    // 시험 화면 시간 카드의 작은 창 버튼으로 켜고 끈다.
    public partial class ExamTimerPipWindow : Window
    {
        // submitCommand: 바탕화면에서 바로 답안을 낼 수 있게 메인 시험 창과 같은 제출 명령을 받는다.
        // 없으면(타이머만 쓰는 경우) 제출 버튼을 숨긴다.
        public ExamTimerPipWindow(ICommand? submitCommand = null)
        {
            InitializeComponent();

            // 남은 시간은 화면이 바뀌어도 이어지는 저장소 하나에서 온다.
            DataContext = ExamTimeStore.Instance;

            if (submitCommand != null) SubmitButton.Command = submitCommand;
            else SubmitButton.Visibility = Visibility.Collapsed;

            // eclass 단추는 네트워크 차단이 실제로 걸렸을 때만 보인다. 차단이 없는 PC 는 평소 브라우저로
            // 들어가면 되고, 차단에 실패한 PC 는 열어 둔 주소도 없어 눌러도 들어가지 못한다.
            UpdateEclassButton();
            ExamMonitorService.Instance.NetworkStateChanged += OnNetworkStateChanged;
            Closed += (_, _) => ExamMonitorService.Instance.NetworkStateChanged -= OnNetworkStateChanged;

            // 처음에는 화면 오른쪽 위에 둔다. 코딩 화면과 겹치면 끌어서 옮기면 된다.
            UiWindow.PinTopRight(this);
        }

        // 시험 중 하나만 띄운다. 닫으면 다음에 다시 만든다.
        private EclassWindow? _eclass;

        private void OnOpenEclass(object sender, RoutedEventArgs e)
        {
            if (_eclass == null)
            {
                _eclass = new EclassWindow();
                _eclass.Closed += (_, _) => _eclass = null;
                _eclass.Show();
                return;
            }

            if (_eclass.WindowState == WindowState.Minimized) _eclass.WindowState = WindowState.Normal;
            _eclass.Activate();
        }

        // 차단 상태는 네트워크 수신 스레드에서 바뀐다.
        private void OnNetworkStateChanged() => UiThread.Post(UpdateEclassButton);

        private void UpdateEclassButton()
            => EclassButton.Visibility = ExamMonitorService.Instance.NetworkState == NetworkBlockState.Blocked
                ? Visibility.Visible
                : Visibility.Collapsed;

        // 제목줄이 없는 창이라 몸통을 끌어서 옮긴다 (떼어진 뒤 들어오는 경우는 UiWindow.Drag 가 가린다).
        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            UiWindow.Drag(this);
        }
    }
}
