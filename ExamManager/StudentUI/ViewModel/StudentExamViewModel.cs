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

    public class ExamFileStatusItem
    {
        public string Category { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string TimeOrNote { get; set; } = string.Empty;
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
                set
                {
                    _isConnected = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ConnectionStatusText));
                    OnPropertyChanged(nameof(SessionCheckText));
                    OnPropertyChanged(nameof(NetworkCheckText));
                }
            }

            public string ConnectionStatusText => IsConnected ? "실시간 연결 중" : "서버 미연결";

            // 조건 점검 항목
            public string SessionCheckText => IsConnected ? "완료" : "미연결";
            public string SecurityPolicyCheckText => "완료";
            public string NetworkCheckText => IsConnected ? "정상" : "단절";
            public string FileReadyCheckText => ExamFile.IsReceived ? "완료" : "수신 대기";

            // 단계(스텝퍼) 계산 (1: 대기, 2: 준비, 3: 파일 배포, 4: 시험 시작)
            public int CurrentStep
            {
                get
                {
                    if (ExamTime.IsRunning) return 4;
                    if (ExamFile.IsReceived) return 3;
                    if (IsConnected) return 2;
                    return 1;
                }
            }

            public bool IsStep1Active => CurrentStep == 1;
            public bool IsStep2Active => CurrentStep == 2;
            public bool IsStep3Active => CurrentStep == 3;
            public bool IsStep4Active => CurrentStep == 4;

            // 현황 테이블 항목들
            public ObservableCollection<ExamFileStatusItem> StatusItems { get; } = new ObservableCollection<ExamFileStatusItem>();

            private int _selectedMenuIndex = 1;
            public int SelectedMenuIndex
            {
                get => _selectedMenuIndex;
                set { _selectedMenuIndex = value; OnPropertyChanged(); }
            }

            public ICommand OpenExtractFolderCommand { get; }

            // 답안을 제출하고 시험을 끝낸다. 시험 중일 때만 누를 수 있다.
            public ICommand SubmitAnswerCommand { get; }

            // 제출 진행 상황. 100MB를 보내는 동안 아무 표시가 없으면 멈춘 줄 알고 다시 누른다.
            private string _submitStatus = string.Empty;
            public string SubmitStatus
            {
                get => _submitStatus;
                private set
                {
                    _submitStatus = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsSubmitting));
                    RefreshStatusItems();
                }
            }

            public bool IsSubmitting => AnswerSubmitService.Instance.State is
                AnswerSubmitState.Compressing or AnswerSubmitState.Sending or AnswerSubmitState.WaitingAck;

            // 감시에 걸린 내역. 화면의 알림창에 쌓인다.
            // 최근 것이 위로 오도록 앞에 넣는다.
            public ObservableCollection<CheatWarningItem> CheatWarnings { get; } = new ObservableCollection<CheatWarningItem>();

            public bool HasCheatWarnings => CheatWarnings.Count > 0;
            public int CheatWarningCount => CheatWarnings.Count;

            // 교수 PC 시험 흐름 연결 전까지, 대기/시작 화면을 오가며 테스트하기 위한 임시 전환
            public ICommand GoToWaitingCommand { get; }
            public ICommand LogoutCommand { get; }

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
                NetworkService.Instance.PacketReceived += OnPacketReceived;

                // 시험 파일 상태 변경 감지 시 테이블/단계 갱신
                ExamFile.PropertyChanged += (s, e) =>
                {
                    OnPropertyChanged(nameof(FileReadyCheckText));
                    OnPropertyChanged(nameof(CurrentStep));
                    OnPropertyChanged(nameof(IsStep1Active));
                    OnPropertyChanged(nameof(IsStep2Active));
                    OnPropertyChanged(nameof(IsStep3Active));
                    OnPropertyChanged(nameof(IsStep4Active));
                    RefreshStatusItems();
                };

                ExamTime.PropertyChanged += (s, e) =>
                {
                    OnPropertyChanged(nameof(CurrentStep));
                    OnPropertyChanged(nameof(IsStep1Active));
                    OnPropertyChanged(nameof(IsStep2Active));
                    OnPropertyChanged(nameof(IsStep3Active));
                    OnPropertyChanged(nameof(IsStep4Active));
                };

                // 기본은 채팅/알림 오버레이 닫힘 상태
                IsChatOpen = false;
                IsNotificationOpen = false;

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

                LogoutCommand = new RelayCommand(() =>
                {
                    Unsubscribe();
                    NetworkService.Instance.Disconnect();
                    _navigationStore.CurrentViewModel = new LoginViewModel(_navigationStore);
                });

                RefreshStatusItems();
            }

            public void RefreshStatusItems()
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.HasShutdownStarted) return;

                dispatcher.BeginInvoke(() =>
                {
                    StatusItems.Clear();

                    // 1. 문제 압축 파일
                    string fileName = string.IsNullOrEmpty(ExamFile.FileName) ? "배포 대기 중" : ExamFile.FileName;
                    string fileStatus = ExamFile.IsReceived ? "수신 완료" : (string.IsNullOrEmpty(ExamFile.StatusText) ? "대기 중" : ExamFile.StatusText);
                    StatusItems.Add(new ExamFileStatusItem
                    {
                        Category = "문제 압축본",
                        Name = fileName,
                        Status = fileStatus,
                        TimeOrNote = ExamFile.IsReceived ? "암호화 보관" : "-"
                    });

                    // 2. 압축 해제된 실제 시험 문제 및 소스 파일들
                    if (ExamFile.IsExtracted && ExamFile.ExtractedFiles.Count > 0)
                    {
                        foreach (var f in ExamFile.ExtractedFiles)
                        {
                            StatusItems.Add(new ExamFileStatusItem
                            {
                                Category = "문제 파일",
                                Name = System.IO.Path.GetFileName(f),
                                Status = "풀이 준비 완료",
                                TimeOrNote = ExamFile.ExtractFolder
                            });
                        }
                    }
                    else
                    {
                        string folderPath = ExamFile.IsExtracted ? ExamFile.ExtractedRoot : ExamFile.ExtractFolder;
                        string folderStatus = ExamFile.IsExtracted ? "압축 해제 완료" : (ExamFile.IsReceived ? "준비 완료" : "대기 중");
                        StatusItems.Add(new ExamFileStatusItem
                        {
                            Category = "작업 폴더",
                            Name = folderPath,
                            Status = folderStatus,
                            TimeOrNote = ExamFile.IsExtracted ? $"파일 {ExamFile.ExtractedFiles.Count}개" : "시험 시작 시 자동 생성"
                        });
                    }

                    // 3. 답안 제출 파일
                    string submitText = string.IsNullOrEmpty(SubmitStatus) ? "답안 작성 중 (미제출)" : SubmitStatus;
                    StatusItems.Add(new ExamFileStatusItem
                    {
                        Category = "답안 제출",
                        Name = $"{Student.StudentNumber}_답안.zip",
                        Status = submitText,
                        TimeOrNote = "-"
                    });

                    // 4. 보안 감시 정책
                    StatusItems.Add(new ExamFileStatusItem
                    {
                        Category = "보안 정책",
                        Name = "부정행위 감시 및 프로세스 차단",
                        Status = "실시간 감시 가동 중",
                        TimeOrNote = "정상"
                    });
                });
            }

            private void UpdateTime()
            {
                CurrentTime = DateTime.Now.ToString("HH:mm:ss");
            }

            public string CurrentPhaseTitle
            {
                get
                {
                    return CurrentStep switch
                    {
                        1 => "현재 단계: 학생 접속 대기",
                        2 => "현재 단계: 시험 준비 및 환경 점검",
                        3 => "현재 단계: 시험 파일 수신 완료",
                        4 => "현재 단계: 시험 진행 및 문제 풀이",
                        _ => "현재 단계: 시험 대기 중"
                    };
                }
            }

            public string CurrentPhaseDescription
            {
                get
                {
                    return CurrentStep switch
                    {
                        1 => "학생들이 시험 운영 프로그램에 로그인하여 접속 확인이 완료될 때까지 대기합니다.\n모든 학생이 접속 완료되면 다음 단계(준비)로 이동할 수 있습니다.\n지각생이 있을 경우 일부 학생만 접속된 상태로 진행될 수 있습니다.",
                        2 => "시험 세션과 보안 정책이 점검되었으며, 시험 문제 배포를 대기하고 있습니다.\n비인가 프로그램(웹 브라우저, 메신저 등)은 자동으로 차단됩니다.",
                        3 => "시험 문제 파일이 성공적으로 수신되었습니다.\n교수자의 시험 시작 신호가 전달되면 즉시 압축이 해제되고 작업 폴더가 열립니다.",
                        4 => "시험이 시작되었습니다. 지정된 내 작업 폴더에서 문제를 확인하고 풀이를 진행하세요.\n풀이가 끝나면 답안 제출 버튼을 눌러 제출을 완료하세요.",
                        _ => "시험 안내에 따라 대기해 주시기 바랍니다."
                    };
                }
            }

            // 화면을 떠날 때 구독을 정리한다.
            private void Unsubscribe()
            {
                _clockTimer.Stop();
                NetworkService.Instance.Disconnected -= OnServerDisconnected;
                NetworkService.Instance.PacketReceived -= OnPacketReceived;
                AnswerSubmitService.Instance.StateChanged -= OnSubmitStateChanged;
                ExamMonitorService.Instance.CheatWarning -= OnCheatWarning;
            }

            // 교수 PC의 시험 단계 알림 수신
            private void OnPacketReceived(PacketType type, IntPtr payload, uint payloadLen)
            {
                if (type != PacketType.ExamPhaseChange) return;
                if (!ExamPhasePayload.TryDecode(payload, payloadLen, out ExamPhase phase)) return;

                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.HasShutdownStarted) return;

                dispatcher.BeginInvoke(() =>
                {
                    OnPropertyChanged(nameof(CurrentStep));
                    OnPropertyChanged(nameof(IsStep1Active));
                    OnPropertyChanged(nameof(IsStep2Active));
                    OnPropertyChanged(nameof(IsStep3Active));
                    OnPropertyChanged(nameof(IsStep4Active));
                    OnPropertyChanged(nameof(CurrentPhaseTitle));
                    OnPropertyChanged(nameof(CurrentPhaseDescription));
                    RefreshStatusItems();
                });
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
                    OnPropertyChanged(nameof(CheatWarningCount));
                    RefreshStatusItems();

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
