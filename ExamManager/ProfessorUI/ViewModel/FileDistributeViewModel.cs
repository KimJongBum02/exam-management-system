using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using ProfessorUI.Service;

namespace ProfessorUI.ViewModel
{
    // ⭐ 개별 학생 데이터를 관리하는 클래스 (기존 유지)
    public class StudentItem : INotifyPropertyChanged
    {
        private bool _isSelected = true; // 기본적으로 모두 선택
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        // 현황판 학생과 매핑하기 위한 학번
        public string StudentId { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        private int _progressValue = 0;
        public int ProgressValue
        {
            get => _progressValue;
            set { _progressValue = value; OnPropertyChanged(); }
        }

        private string _statusText = "대기 중";
        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        // 전송을 시작한 세션 ID (수신 완료 응답이 오면 비운다).
        // 같은 세션에 중복 전송하는 것을 막는 용도 — 학생이 재접속하면 세션이 바뀌어 자연히 풀린다.
        public string? SendingSessionId { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public class FileDistributeViewModel : INotifyPropertyChanged
    {
        private bool _isDeploying = false; // 중복 실행 방지용

        // ⭐ 핵심 1: XAML이 바라보는 활성화 여부 프로퍼티 (FileDeployState 연동)
        public bool IsContainerEnabled => FileDeployState.IsFilePrepared;

        // ⭐ 학생 목록을 담을 컬렉션
        public ObservableCollection<StudentItem> Students { get; } = new ObservableCollection<StudentItem>();

        private string _validationMessage = string.Empty;
        public string ValidationMessage
        {
            get => _validationMessage;
            set { _validationMessage = value; OnPropertyChanged(); }
        }

        private string _deployStatusMessage = "배포 대기 중";
        public string DeployStatusMessage
        {
            get => _deployStatusMessage;
            set { _deployStatusMessage = value; OnPropertyChanged(); }
        }

        // 명령어(Command)들
        public ICommand StartDeployCommand { get; }
        public ICommand SelectAllCommand { get; }
        public ICommand DeselectAllCommand { get; }

        public FileDistributeViewModel()
        {
            // ⭐ 핵심 2: Command CanExecute 조건에 IsContainerEnabled 연결
            StartDeployCommand = new RelayCommand(
                ExecuteStartDeploy,
                canExecute: o => IsContainerEnabled && !_isDeploying
            );
            SelectAllCommand = new RelayCommand(
                o => SetAllSelection(true),
                canExecute: o => IsContainerEnabled
            );
            DeselectAllCommand = new RelayCommand(
                o => SetAllSelection(false),
                canExecute: o => IsContainerEnabled
            );

            // ⭐ 핵심 3: static 클래스의 상태가 바뀌면 UI 전체 갱신 호출
            FileDeployState.StateChanged += OnFileDeployStateChanged;

            // 실제 접속한 학생(현황판)과 동기화
            foreach (var connected in StudentStore.Instance.Students)
                Students.Add(CreateRow(connected));

            StudentStore.Instance.Students.CollectionChanged += OnStoreStudentsChanged;
            StudentStore.Instance.FileReceivedConfirmed += OnFileReceivedConfirmed;

            // 전송 진행률·실패를 학생 행에 반영
            NetworkService.Instance.SendProgress += OnSendProgress;
            NetworkService.Instance.SendError += OnSendError;
        }

        // 전송 진행률 (네이티브 스레드에서 올라온다)
        private void OnSendProgress(string sessionId, string studentId, string transferId, string fileName, int percent) => PostToUi(() =>
        {
            var row = Students.FirstOrDefault(s => s.StudentId == studentId);
            if (row == null || row.SendingSessionId != sessionId) return;

            row.ProgressValue = percent;
            row.StatusText = percent >= 100 ? "전송 완료" : $"전송 중 {percent}%";
        });

        // 전송 실패 (네이티브 스레드에서 올라온다)
        private void OnSendError(string sessionId, string studentId, string transferId, string message) => PostToUi(() =>
        {
            var row = Students.FirstOrDefault(s => s.StudentId == studentId);
            if (row == null || row.SendingSessionId != sessionId) return;

            row.SendingSessionId = null; // 실패했으므로 재전송을 다시 허용한다
            row.ProgressValue = 0;
            row.StatusText = "전송 실패";
        });

        // 콜백은 네이티브 스레드에서 올라오므로 UI 스레드로 넘겨 처리한다.
        private static void PostToUi(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted) return;
            dispatcher.BeginInvoke(action);
        }

        // static 상태 변경 시 호출되는 이벤트 핸들러
        private void OnFileDeployStateChanged()
        {
            // XAML 바인딩 속성 갱신 알림
            // 1. IsContainerEnabled 속성 변경 알림 (XAML 바인딩 갱신)
            OnPropertyChanged(nameof(IsContainerEnabled));

            // 2. Command들의 CanExecute 상태를 다시 평가하도록 WPF UI 프레임워크에 알림
            CommandManager.InvalidateRequerySuggested();
        }

        private static StudentItem CreateRow(StudentItemViewModel student) => new StudentItem
        {
            StudentId = student.StudentId,
            Name = student.Name,
            IsSelected = true,
            ProgressValue = 0,
            StatusText = "대기 중"
        };

        // 현황판에 학생이 추가/삭제될 때 배포 목록도 함께 갱신
        private void OnStoreStudentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (StudentItemViewModel added in e.NewItems)
                    if (!Students.Any(s => s.StudentId == added.StudentId))
                        Students.Add(CreateRow(added));
            }

            if (e.OldItems != null)
            {
                foreach (StudentItemViewModel removed in e.OldItems)
                {
                    var row = Students.FirstOrDefault(s => s.StudentId == removed.StudentId);
                    if (row != null) Students.Remove(row);
                }
            }
        }

        // 학생이 '수신 완료' 응답을 보내오면 해당 행을 완료 표시
        private void OnFileReceivedConfirmed(string studentId)
        {
            var row = Students.FirstOrDefault(s => s.StudentId == studentId);
            if (row != null)
            {
                row.SendingSessionId = null; // 전송이 끝났으므로 재배포를 다시 허용한다
                row.ProgressValue = 100;
                row.StatusText = "수신완료";
            }
        }

        // 전체 선택/해제 로직
        private void SetAllSelection(bool isSelected)
        {
            if (_isDeploying || !IsContainerEnabled) return; // 전송 중이거나 비활성화 시 선택 변경 불가
            foreach (var student in Students)
            {
                student.IsSelected = isSelected;
            }
        }

        private void ExecuteStartDeploy(object? obj)
        {
            // 1. 공용 저장소의 상태 확인 (압축/암호화 완료 여부)
            if (!FileDeployState.IsFilePrepared || string.IsNullOrEmpty(FileDeployState.PackagePath))
            {
                ValidationMessage = "⚠️ 1단계: 파일 준비 및 암호화를 먼저 완료해 주세요.";
                return;
            }

            // 2. 선택된 학생이 있는지 체크
            var selectedStudents = Students.Where(s => s.IsSelected).ToList();
            if (selectedStudents.Count == 0)
            {
                ValidationMessage = "⚠️ 배포할 학생을 최소 한 명 이상 선택해 주세요.";
                return;
            }

            if (_isDeploying) return;
            _isDeploying = true;
            ValidationMessage = "";

            // 3. 선택된 학생별로 세션을 찾아 실제 파일 전송
            int sentCount = 0;
            foreach (var student in selectedStudents)
            {
                // 접속이 끊겨도 SessionId는 남으므로 SessionId 유무로 판단하면 안 된다
                var connected = StudentStore.Instance.Students
                    .FirstOrDefault(s => s.StudentId == student.StudentId && s.IsConnected);

                if (connected == null)
                {
                    student.StatusText = "미접속";
                    continue;
                }

                // 버튼을 두 번 눌러도 같은 세션에 같은 파일을 겹쳐 보내지 않는다
                if (student.SendingSessionId == connected.SessionId)
                    continue;

                if (!NetworkService.Instance.SendFileToSession(
                        connected.SessionId, FileDeployState.PackagePath!, FileDeployState.Password ?? ""))
                {
                    student.StatusText = "전송 실패";
                    continue;
                }

                student.SendingSessionId = connected.SessionId;
                student.ProgressValue = 0;
                student.StatusText = "전송 중";
                sentCount++;
            }

            DeployStatusMessage = $"{sentCount}명에게 파일을 전송했습니다. 학생 수신 응답 대기 중...";
            _isDeploying = false;

            // 전송을 시작했음을 공용 저장소에 기록 (시험 시작 단계 활성화)
            if (sentCount > 0)
                FileDeployState.IsFileDistributed = true;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}