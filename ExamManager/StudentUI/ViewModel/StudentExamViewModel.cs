using System;
using System.Collections.Generic;
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

        // 교수 PC 시험 흐름 연결 전까지, 대기/시작 화면을 오가며 테스트하기 위한 임시 전환
        public ICommand GoToWaitingCommand { get; }

        public StudentExamViewModel(NavigationStore navigationStore, Student student)
        {
            _navigationStore = navigationStore;
            Student = student;

            IsConnected = NetworkService.Instance.IsConnected;
            NetworkService.Instance.Disconnected += OnServerDisconnected;



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

        // 화면을 떠날 때 구독을 정리한다.
        private void Unsubscribe()
        {
            NetworkService.Instance.Disconnected -= OnServerDisconnected;
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
