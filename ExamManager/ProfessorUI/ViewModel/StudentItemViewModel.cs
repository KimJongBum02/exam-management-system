using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;

namespace ProfessorUI.ViewModel
{
    public class StudentItemViewModel : INotifyPropertyChanged
    {
        private string _studentId = string.Empty;
        private string _name = string.Empty;
        private string _status = string.Empty; // 메인 화면용 (대기, 미접속, 시험중 등)
        private string _ip = string.Empty;
        private string _attendance = string.Empty;

        // ⭐ 2, 3단계 lifecycle을 위한 속성 추가
        private bool _isSelected;
        private bool _isFileReceived = false; // 💡 기본값 false -> 무조건 "미수집"으로 시작!
        private bool _isApproved;
        private bool _isAnswerSubmitted;

        public string StudentId { get => _studentId; set { _studentId = value; OnPropertyChanged(); } }
        public string Name { get => _name; set { _name = value; OnPropertyChanged(); } }
        // 상태가 바뀔 때마다 시각을 함께 찍는다. 현황 표의 '마지막 갱신' 열이 이 값을 쓴다.
        public string Status
        {
            get => _status;
            set
            {
                _status = value;
                LastUpdate = DateTime.Now.ToString("HH:mm:ss");
                OnPropertyChanged();
                OnPropertyChanged(nameof(CleanupText));
            }
        }

        private string _lastUpdate = "-";
        public string LastUpdate { get => _lastUpdate; private set { _lastUpdate = value; OnPropertyChanged(); } }
        public string Ip { get => _ip; set { _ip = value; OnPropertyChanged(); } }
        public string Attendance { get => _attendance; set { _attendance = value; OnPropertyChanged(); } }

        // 서버가 부여한 세션 식별자 (접속 종료·개별 전송 매핑용)
        public string SessionId { get; set; } = string.Empty;

        // 지금 실제로 접속돼 있는지.
        // 접속이 끊겨도 SessionId는 남아 있으므로, 전송 가능 여부는 이 값으로 판단한다.
        private bool _isConnected;
        public bool IsConnected
        {
            get => _isConnected;
            set { _isConnected = value; OnPropertyChanged(); OnPropertyChanged(nameof(ConnectionText)); }
        }

        // 표에 그대로 나갈 접속 상태 문구
        public string ConnectionText => _isConnected ? "접속 중" : "미접속";

        // ── 학생 PC 에서 감시가 실제로 켜졌는지 ──
        // 학생이 시험 시작 직후 스스로 보고한 값이다.
        // 보고가 오기 전에는 셋 다 기본값이라 "확인 전"으로 보인다.
        private bool _monitorReported;
        private bool _processMonitorOn;
        private bool _networkMonitorOn;
        private string _monitorDetail = string.Empty;

        public bool NetworkMonitorOn => _networkMonitorOn;
        public string MonitorDetail => _monitorDetail;

        public string MonitorText => !_monitorReported ? "확인 전"
                                   : _processMonitorOn && _networkMonitorOn ? "정상"
                                   : _networkMonitorOn ? "네트워크만"
                                   : _processMonitorOn ? "프로세스만"
                                   : "꺼짐";

        public void SetMonitorStatus(bool processOn, bool networkOn, string detail)
        {
            _monitorReported = true;
            _processMonitorOn = processOn;
            _networkMonitorOn = networkOn;
            _monitorDetail = detail;

            OnPropertyChanged(nameof(NetworkMonitorOn));
            OnPropertyChanged(nameof(MonitorDetail));
            OnPropertyChanged(nameof(MonitorText));
        }

        // 학생 PC 에 시험 파일이 남았다는 보고를 받았는지.
        //
        // 예전에는 Status 문자열이 "정리실패" 인지로 판단했는데, 그 뒤에 부정행위 알림이
        // 오면 Status 가 "부정행위 감지" 로 덮여 실패 표시가 사라졌다.
        // 지워졌는지 여부는 다른 사건에 묻히면 안 되므로 따로 들고 있는다.
        private bool _isCleanupFailed;
        public bool IsCleanupFailed
        {
            get => _isCleanupFailed;
            set { _isCleanupFailed = value; OnPropertyChanged(); OnPropertyChanged(nameof(CleanupText)); }
        }

        // 학생이 "지웠다"고 알려 왔는지.
        // 이 보고가 있어야 완료로 본다. 답안을 걷은 것만으로 지워졌다고 단정하면,
        // 보고 전에 학생 PC 가 꺼진 경우를 지워진 것으로 착각한다.
        private bool _isCleanupDone;
        public bool IsCleanupDone
        {
            get => _isCleanupDone;
            set { _isCleanupDone = value; OnPropertyChanged(); OnPropertyChanged(nameof(CleanupText)); }
        }

        // ── 종료 및 정산 화면에 그대로 나갈 문구 ──
        // 흔적 삭제는 학생이 답안 회신을 받은 뒤 스스로 하므로, 답안을 걷었는지로 판단한다.
        // 다만 학생이 "못 지웠다"고 알려 오면 그쪽이 우선이다.
        public string CollectText => IsAnswerSubmitted ? "완료" : "미수집";
        public string CleanupText => IsCleanupFailed ? "오류"
                                   : IsCleanupDone ? "완료"
                                   : IsAnswerSubmitted ? "확인 필요" : "미실행";
        public string ShutdownText => IsApproved ? "완료" : "미실행";

        public bool IsSelected { get => _isSelected; set { _isSelected = value; OnPropertyChanged(); } }
        public bool IsFileReceived { get => _isFileReceived; set { _isFileReceived = value; OnPropertyChanged(); } }
        public bool IsApproved
        {
            get => _isApproved;
            set { _isApproved = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShutdownText)); }
        }

        // 이 학생의 답안을 받아 저장까지 끝냈는지.
        // IsFileReceived와 헷갈리기 쉬운데 방향이 반대다 —
        // 그쪽은 교수가 보낸 시험 파일을 학생이 받은 것이고, 이쪽은 학생 답안을 교수가 받은 것이다.
        // 학생이 접속을 끊어도 이 기록은 남으므로, 나갔는지 못 냈는지 구분할 수 있다.
        public bool IsAnswerSubmitted
        {
            get => _isAnswerSubmitted;
            set
            {
                _isAnswerSubmitted = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CollectText));
                OnPropertyChanged(nameof(CleanupText));
            }
        }

        // 개별 승인 명령 바인딩용
        public System.Windows.Input.ICommand? ApproveSingleCommand { get; set; }

        // 🎯 1:1 채팅 열기 커맨드 및 요청 콜백
        public System.Action<string, string>? RequestOpenChat { get; set; }
        public System.Windows.Input.ICommand? OpenChatCommand { get; set; }

        public StudentItemViewModel()
        {
            // 초기화 시점에 커맨드 등록 (Service/RelayCommand 사용을 가정)
            OpenChatCommand = new RelayCommand(o =>
            {
                RequestOpenChat?.Invoke(SessionId, Name);
            });
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
