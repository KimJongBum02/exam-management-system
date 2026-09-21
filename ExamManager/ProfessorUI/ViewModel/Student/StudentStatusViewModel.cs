using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;

namespace ProfessorUI.ViewModel
{
    public class StudentStatusViewModel : INotifyPropertyChanged
    {
        private string _studentId = string.Empty;
        private string _name = string.Empty;
        private string _status = string.Empty; // 메인 화면용 (대기, 미접속, 시험중 등)
        private string _ip = string.Empty;

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

        // 서버가 부여한 세션 식별자 (접속 종료·개별 전송 매핑용)
        public string SessionId { get; set; } = string.Empty;

        // ── 시험 파일 배포 상태 (시험 준비 2단계 표) ──

        // 배포 표의 체크. 정산 화면의 승인 체크(IsSelected)와 따로 둔다 —
        // 하나를 같이 쓰면 배포 때 전체 선택한 것이 정산 화면의 승인 대상까지 바꿔 놓는다.
        private bool _isDeployTarget = true;   // 접속한 학생은 기본적으로 모두 보낸다
        public bool IsDeployTarget
        {
            get => _isDeployTarget;
            set { _isDeployTarget = value; OnPropertyChanged(); }
        }

        private int _deployProgress;
        public int DeployProgress
        {
            get => _deployProgress;
            set { _deployProgress = value; OnPropertyChanged(); OnPropertyChanged(nameof(DeployProgressText)); }
        }

        private string _deployStatus = "대기 중";
        public string DeployStatus
        {
            get => _deployStatus;
            set { _deployStatus = value; OnPropertyChanged(); OnPropertyChanged(nameof(DeployProgressText)); }
        }

        // 표에 그대로 나갈 진행률.
        // 압축 단계와 같은 0~100% 표기로 맞추고, 실패했을 때만 숫자 대신 실패로 적는다.
        // 실패하면 DeployProgress 가 0으로 되돌아가는데, 그 0%를 그냥 보여 주면
        // 아직 시작 안 한 학생과 구분되지 않는다.
        public string DeployProgressText => _deployStatus switch
        {
            "전송 실패" => "실패",
            "미접속"   => "-",
            "대기 중"  => "-",
            _          => $"{_deployProgress}%"
        };

        // 전송을 시작한 세션 ID (수신 완료 응답이 오면 비운다).
        // 같은 세션에 중복 전송하는 것을 막는 용도 — 학생이 재접속하면 세션이 바뀌어 자연히 풀린다.
        public string? SendingSessionId { get; set; }

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
                                   : _processMonitorOn ? "프로그램만"
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
            OnPropertyChanged(nameof(HasExamStarted));
        }

        // 이 접속에서 학생 PC 가 시험을 시작했는지. 시험 관리 화면의 재배포 대상이 이 값으로 갈린다.
        // 학생은 시험을 시작하면(이어 받기 포함) 감시 상태를 보고하므로, 보고가 왔으면 시작한 것이다.
        // 끊기면 모르는 상태로 돌린다 — 다시 켠 학생이 이어 받았는지는 새 보고로 안다.
        public bool HasExamStarted => _monitorReported;

        public void ClearMonitorStatus()
        {
            _monitorReported = false;
            OnPropertyChanged(nameof(MonitorText));
            OnPropertyChanged(nameof(HasExamStarted));
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
            set { _isApproved = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShutdownText)); OnPropertyChanged(nameof(CanApprove)); }
        }

        // 종료 화면에서 고를 수 있는 학생인지. 답안을 걷었고 아직 승인하지 않은 학생만 승인 대상이다.
        public bool CanApprove => IsAnswerSubmitted && !IsApproved;

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
                OnPropertyChanged(nameof(CanApprove));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
