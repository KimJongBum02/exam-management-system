using NetworkLib;
using StudentUI.Model;
using StudentUI.Service;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Input;

namespace StudentUI.ViewModel
{
    public class LoginViewModel : INotifyPropertyChanged
    {
        private readonly NavigationStore _navigationStore;
        public Student Student { get; set; } = new Student();
        public ICommand LoginCommand { get; }
        public event Action? ShowIPInput;

        private string _nameError = string.Empty;
        public string NameError
        {
            get => _nameError;
            set { _nameError = value; OnPropertyChanged(); }
        }

        private string _numberError = string.Empty;
        public string NumberError
        {
            get => _numberError;
            set { _numberError = value; OnPropertyChanged(); }
        }

        public LoginViewModel(NavigationStore navigationStore)
        {
            _navigationStore = navigationStore;
            LoginCommand = new RelayCommand(() =>
            {
                // 유효성 검사 후 인라인 에러 표시
                ValidateFields();

                if (!TryLogin()) return;

                string? mismatch = PendingExamMismatch();
                if (mismatch != null)
                {
                    NumberError = "진행 중인 시험의 학번과 다릅니다.";
                    MessageBox.Show(mismatch, "로그인 확인", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                ShowIPInput?.Invoke();
            });
        }

        // 이 PC 에서 진행 중이던 시험이 있으면 그 학번으로만 들어올 수 있다(ExamSessionStore 참고).
        // 다른 학번으로 들어오면 교수 화면에 새 줄이 생기고 답안이 그 학번으로 저장된다.
        // 9자리 학번을 다시 치다 틀리는 경우가 흔하다. 이름은 달라도 막지 않는다 — 교수 쪽은 학번으로 구분한다.
        // 다르면 학생에게 보여 줄 안내를, 같거나 이어받을 시험이 없으면 null 을 돌려준다.
        private string? PendingExamMismatch()
        {
            var session = ExamMonitorService.Instance.PendingResume;
            if (session == null || session.StudentNumber.Length == 0 || session.StudentNumber == Student.StudentNumber)
                return null;

            return $"이 PC에서 진행 중이던 시험은 {session.StudentNumber} {session.StudentName} 학생의 것입니다.\n" +
                   "같은 학번으로 로그인해 주세요.";
        }

        public void CompleteLogin()
        {
            // IP 입력이 끝난 시점에 실제 서버로 연결을 시도한다.
            bool connected = NetworkService.Instance.Connect(Student.IPAddress);
            if (!connected)
            {
                MessageBox.Show($"서버에 연결하지 못했습니다: {Student.IPAddress}\nIP 주소와 서버 실행 여부를 확인해 주세요.",
                    "연결 실패");
                return;
            }

            // 학번(16바이트) + 이름을 담은 로그인 패킷 전송 → 교수 PC 현황판에 표시됨
            byte[] loginPayload = new byte[80];
            Encoding.UTF8.GetBytes(Student.StudentNumber).CopyTo(loginPayload, 0);
            Encoding.UTF8.GetBytes(Student.StudentName).CopyTo(loginPayload, 16);
            NetworkService.Instance.SendPacket(PacketType.StudentLogin, loginPayload);

            // 이 PC 에 설치된 프로그램 목록을 보낸다. 교수의 프로그램 선택창에 강의실 PC 것도 뜨게 한다.
            // 로그인 패킷 뒤에 보내야 한다 — 교수 PC 는 로그인 전에 온 패킷을 버린다.
            Service.InstalledProgramReport.Send();

            // 누가 로그인했는지 알리고, 시험 도중 꺼졌다 다시 켠 경우 진행 중이던 시험의 감시와 차단을 다시 건다.
            // 로그인 뒤에 해야 교수 PC 가 감시 상태 보고를 받는다.
            Service.ExamMonitorService.Instance.OnLoggedIn(Student.StudentNumber, Student.StudentName);

            // 퀴즈 응답에 학번·이름을 실어 보낼 수 있도록 학생 정보를 넘겨 둔다.
            Service.QuizService.Instance.Student = Student;

            // 교수 UI와 동일한 '시험 준비 마법사' 대시보드로 진입 (1단계: 대기부터 시작)
            _navigationStore.CurrentViewModel = new StudentExamViewModel(_navigationStore, Student);
        }

        private void ValidateFields()
        {
            // 이름 검증
            if (string.IsNullOrEmpty(Student.StudentName))
                NameError = "이름을 입력해 주세요.";
            else
                NameError = string.Empty;

            // 학번 검증
            if (string.IsNullOrEmpty(Student.StudentNumber))
                NumberError = "학번을 입력해 주세요.";
            else if (Student.StudentNumber.Length != 9)
                NumberError = "9자리 학번을 입력해 주세요.";
            else
                NumberError = string.Empty;
        }

        public bool TryLogin()
        {
            if (string.IsNullOrEmpty(Student.StudentName))
                return false;

            if (string.IsNullOrEmpty(Student.StudentNumber) || Student.StudentNumber.Length != 9)
                return false;

            return true;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
