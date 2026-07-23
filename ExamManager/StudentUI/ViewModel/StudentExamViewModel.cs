using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
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

        public Student Student { get; set; } = new Student();

        // 시험 파일 수신·압축 해제 상태 (화면 바인딩용)
        public ExamFileStore ExamFile => ExamFileStore.Instance;

        // ── 서버 접속 상태 ──
        // 시험 중 교수 PC가 꺼지거나 연결이 끊기면 학생이 바로 알 수 있어야 한다.
        private bool _isConnected;
        public bool IsConnected
        {
            get => _isConnected;
            set { _isConnected = value; OnPropertyChanged(); OnPropertyChanged(nameof(ConnectionStatusText)); }
        }

        public string ConnectionStatusText => IsConnected ? "서버 연결됨" : "서버 미연결";

        public ICommand ExtractCommand { get; }
        public ICommand OpenExtractFolderCommand { get; }

        public StudentExamViewModel(Student student)
        {
            Student = student;

            IsConnected = NetworkService.Instance.IsConnected;
            NetworkService.Instance.Disconnected += OnServerDisconnected;

            ExtractCommand = new RelayCommand(
                async () => await ExamFile.ExtractAsync(),
                () => ExamFile.CanExtract);

            OpenExtractFolderCommand = new RelayCommand(
                () => Process.Start("explorer.exe", ExamFile.ExtractFolder),
                () => ExamFile.IsExtracted);
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
