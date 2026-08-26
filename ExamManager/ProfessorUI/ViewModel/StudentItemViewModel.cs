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

        public string StudentId { get => _studentId; set { _studentId = value; OnPropertyChanged(); } }
        public string Name { get => _name; set { _name = value; OnPropertyChanged(); } }
        public string Status { get => _status; set { _status = value; OnPropertyChanged(); } }
        public string Ip { get => _ip; set { _ip = value; OnPropertyChanged(); } }
        public string Attendance { get => _attendance; set { _attendance = value; OnPropertyChanged(); } }

        // 서버가 부여한 세션 식별자 (접속 종료·개별 전송 매핑용)
        public string SessionId { get; set; } = string.Empty;

        // 지금 실제로 접속돼 있는지.
        // 접속이 끊겨도 SessionId는 남아 있으므로, 전송 가능 여부는 이 값으로 판단한다.
        public bool IsConnected { get; set; }

        public bool IsSelected { get => _isSelected; set { _isSelected = value; OnPropertyChanged(); } }
        public bool IsFileReceived { get => _isFileReceived; set { _isFileReceived = value; OnPropertyChanged(); } }
        public bool IsApproved { get => _isApproved; set { _isApproved = value; OnPropertyChanged(); } }

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
