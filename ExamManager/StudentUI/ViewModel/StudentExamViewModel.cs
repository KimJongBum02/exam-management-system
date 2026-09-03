using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
using NetworkLib;
using StudentUI.Model;
using StudentUI.Service;

namespace StudentUI.ViewModel
{
    // 학생 화면 알림창에 표시할 감시 적발 내역 한 건.
    public class CheatWarningItem
    {
        public string Time { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
    }
    public class StudentExamViewModel : INotifyPropertyChanged
    {
        private readonly NavigationStore _navigationStore;

        public Student Student { get; set; } = new Student();

        // 시험 파일 수신·압축 해제 상태 (화면 바인딩용)
        public ExamFileStore ExamFile => ExamFileStore.Instance;

        // 시험 남은 시간. 화면이 바뀌어도 이어지도록 저장소를 그대로 내보낸다.
        public ExamTimeStore ExamTime => ExamTimeStore.Instance;

        // 교수와 주고받는 채팅·알림. 시험 내내 이 화면에 머무르므로 여기에 둔다.
        public SharedChatViewModel ChatVM => SharedChatViewModel.Instance;

        private bool _isNotificationOpen;
        public bool IsNotificationOpen
        {
            get => _isNotificationOpen;
            set { _isNotificationOpen = value; OnPropertyChanged(); }
        }

        private bool _isChatOpen;
        public bool IsChatOpen
        {
            get => _isChatOpen;
            set { _isChatOpen = value; OnPropertyChanged(); }
        }

        public ICommand ToggleNotificationCommand { get; }
        public ICommand ToggleChatCommand { get; }

        // ── 서버 접속 상태 ──
        // 시험 중 교수 PC가 꺼지거나 연결이 끊기면 학생이 바로 알 수 있어야 한다.
        private bool _isConnected;
        public bool IsConnected
        {
            get => _isConnected;
            set { _isConnected = value; OnPropertyChanged(); OnPropertyChanged(nameof(ConnectionStatusText)); }
        }

        public string ConnectionStatusText => IsConnected ? "서버 연결됨" : "서버 미연결";

        public ICommand OpenExtractFolderCommand { get; }

        // 답안을 제출하고 시험을 끝낸다. 시험 중일 때만 누를 수 있다.
        public ICommand SubmitAnswerCommand { get; }

        // 제출 진행 상황. 100MB를 보내는 동안 아무 표시가 없으면 멈춘 줄 알고 다시 누른다.
        private string _submitStatus = string.Empty;
        public string SubmitStatus
        {
            get => _submitStatus;
            private set { _submitStatus = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsSubmitting)); }
        }

        public bool IsSubmitting => AnswerSubmitService.Instance.State is
            AnswerSubmitState.Compressing or AnswerSubmitState.Sending or AnswerSubmitState.WaitingAck;

        // 감시에 걸린 내역. 화면의 알림창에 쌓인다.
        // 최근 것이 위로 오도록 앞에 넣는다.
        public ObservableCollection<CheatWarningItem> CheatWarnings { get; } = new ObservableCollection<CheatWarningItem>();

        public bool HasCheatWarnings => CheatWarnings.Count > 0;

        // 교수 PC 시험 흐름 연결 전까지, 대기/시작 화면을 오가며 테스트하기 위한 임시 전환
        public ICommand GoToWaitingCommand { get; }

        // ── 실시간 시계 타이머 ──
        private readonly System.Windows.Threading.DispatcherTimer _clockTimer;

        private string _currentTime = string.Empty;
        public string CurrentTime
        {
            get => _currentTime;
            set { _currentTime = value; OnPropertyChanged(); }
        }

        public StudentExamViewModel(NavigationStore navigationStore, Student student)
        {
            _navigationStore = navigationStore;
            Student = student;

            SubmitAnswerCommand = new RelayCommand(SubmitAnswer, () => !IsSubmitting);

            AnswerSubmitService.Instance.StateChanged += OnSubmitStateChanged;
            ExamMonitorService.Instance.CheatWarning += OnCheatWarning;


            IsConnected = NetworkService.Instance.IsConnected;
            NetworkService.Instance.Disconnected += OnServerDisconnected;

            // 시험 화면 진입 시 감독관 소통 패널 기본 활성화
            IsChatOpen = true;

            // 실시간 시계 초기화 및 시작
            UpdateTime();
            _clockTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _clockTimer.Tick += (s, e) => UpdateTime();
            _clockTimer.Start();

            // 압축 해제 뒤 탐색기가 자동으로 열리지만, 학생이 창을 닫았거나
            // 자동 열기가 실패한 경우를 위해 언제든 다시 열 수 있게 둔다.
            OpenExtractFolderCommand = new RelayCommand(() => ExamFile.OpenExtractFolder());

            ToggleNotificationCommand = new RelayCommand(() =>
            {
                IsNotificationOpen = !IsNotificationOpen;
                if (IsNotificationOpen) IsChatOpen = false; // 한 번에 하나만
            });

            ToggleChatCommand = new RelayCommand(() =>
            {
                IsChatOpen = !IsChatOpen;
                if (IsChatOpen) IsNotificationOpen = false;
            });

            GoToWaitingCommand = new RelayCommand(() =>
            {
                Unsubscribe();
                _navigationStore.CurrentViewModel = new WaitingViewModel(_navigationStore, Student);
            });

        }

        private void UpdateTime()
        {
            CurrentTime = DateTime.Now.ToString("HH:mm:ss");
        }

        // 화면을 떠날 때 구독을 정리한다.
        private void Unsubscribe()
        {
            _clockTimer.Stop();
            NetworkService.Instance.Disconnected -= OnServerDisconnected;
            AnswerSubmitService.Instance.StateChanged -= OnSubmitStateChanged;
            ExamMonitorService.Instance.CheatWarning -= OnCheatWarning;
        }

        // ── 답안 제출 ──
        // 시험 20분 뒤부터 답안을 다 쓴 학생이 먼저 나갈 수 있다. 그때 누르는 버튼이다.
        private void SubmitAnswer()
        {
            // 되돌릴 수 없는 동작이라 한 번 더 묻는다.
            var answer = MessageBox.Show(
                "답안을 제출하고 시험을 끝냅니다.\n제출 후에는 답안을 수정할 수 없습니다.\n\n계속하시겠습니까?",
                "답안 제출", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (answer != MessageBoxResult.Yes) return;

            // 압축·전송은 시간이 걸리므로 화면을 붙잡지 않는다.
            // 결과는 StateChanged로 올라와 SubmitStatus에 표시된다.
            _ = AnswerSubmitService.Instance.SubmitAsync();
        }

        // 제출 진행 상황 (다른 스레드에서 호출될 수 있음)
        private void OnSubmitStateChanged(AnswerSubmitState state, string message)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted) return;

            dispatcher.BeginInvoke(() =>
            {
                SubmitStatus = message;

                // 제출이 끝나면 결과를 확실히 알려 준다.
                // 실패했어도 답안은 PC에 그대로 남아 있으므로 다시 제출할 수 있다.
                if (state == AnswerSubmitState.Succeeded)
                    MessageBox.Show(message, "제출 완료", MessageBoxButton.OK, MessageBoxImage.Information);
                else if (state == AnswerSubmitState.Failed)
                    MessageBox.Show(message + "\n\n답안은 그대로 남아 있습니다. 다시 시도하거나 교수님께 알려 주세요.",
                                    "제출 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
            });
        }

        // 감시에 걸렸을 때 (네이티브 감시 스레드에서 호출됨)
        private void OnCheatWarning(string description)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted) return;

            dispatcher.BeginInvoke(() =>
            {
                CheatWarnings.Insert(0, new CheatWarningItem
                {
                    Time = DateTime.Now.ToString("HH:mm:ss"),
                    Description = description,
                });
                OnPropertyChanged(nameof(HasCheatWarnings));

                // 알림창이 닫혀 있으면 열어 준다. 왜 프로그램이 꺼졌는지 바로 보이게 한다.
                IsNotificationOpen = true;
                IsChatOpen = false;
            });
        }

        // 서버 연결이 끊겼을 때 UI 상태를 갱신 (네이티브 스레드에서 호출됨)
        private void OnServerDisconnected(DisconnectReason reason)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted) return;
            dispatcher.BeginInvoke(() => IsConnected = false);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
