using NetworkLib;
using StudentUI.Model;
using StudentUI.Service;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;

namespace StudentUI.ViewModel
{
    public class LoginViewModel : INotifyPropertyChanged
    {
        private readonly NavigationStore _navigationStore;
        public Student Student { get; set; } = new Student();
        public ICommand LoginCommand { get; }
        // 교수 PC 를 자동으로 찾지 못했을 때만 IP 를 직접 받는다.
        // 화면이 돌려준 주소·포트로 잇고, 학생이 창을 닫으면 null 이다.
        public event Func<(string Ip, int Port)?>? RequestManualAddress;

        // 찾는 동안 화면에 보여 줄 한 줄. 엔터를 누르고 몇 초 조용하면 멈춘 줄 안다.
        private string _connectStatus = string.Empty;
        public string ConnectStatus
        {
            get => _connectStatus;
            private set { _connectStatus = value; OnPropertyChanged(); }
        }

        // 찾는 중에 엔터를 또 눌러 두 번 붙는 일이 없게 한다.
        private bool _connecting;

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
            LoginCommand = new RelayCommand(() => _ = LoginAsync());
        }

        private async Task LoginAsync()
        {
            if (_connecting) return;

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

            _connecting = true;
            try
            {
                // 학번·이름만 치면 들어갈 수 있도록 교수 PC 를 스스로 찾는다.
                ConnectStatus = "교수 PC를 찾는 중…";
                (string Ip, int Port)? professor = await ProfessorDiscovery.FindAsync();
                ConnectStatus = string.Empty;

                // 못 찾는 경우(브로드캐스트를 막는 공유기, 교수 PC 가 다른 대역)에는 예전처럼 IP 를 직접 받는다.
                professor ??= RequestManualAddress?.Invoke();
                if (professor == null) return;

                await CompleteLogin(professor.Value.Ip, professor.Value.Port);
            }
            finally
            {
                _connecting = false;
                ConnectStatus = string.Empty;
            }
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

        // 이름은 완성된 한글만 받는다. 영문·숫자·공백·자모 낱자(ㄱ, ㅏ)는 받지 않는다.
        private static bool IsKoreanName(string name) => Regex.IsMatch(name, "^[가-힣]+$");

        private async Task CompleteLogin(string ip, int port)
        {
            // 교수 PC 를 찾았거나 직접 입력받은 시점에 실제 서버로 연결을 시도한다.
            Student.IPAddress = ip;
            bool connected = NetworkService.Instance.Connect(ip, port);
            if (!connected)
            {
                MessageBox.Show($"서버에 연결하지 못했습니다: {ip}:{port}\n주소와 교수 프로그램 실행 여부를 확인해 주세요.",
                    "연결 실패");
                return;
            }

            // 교수 PC 의 승인·거절을 기다린다. 회신이 빨리 오므로 로그인 패킷보다 먼저 구독해 둔다.
            var response = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnPacket(PacketType type, IntPtr payload, uint payloadLen)
            {
                if (type == PacketType.LoginResponse &&
                    LoginResponsePayload.TryDecode(payload, payloadLen, out bool approved, out string reason))
                    response.TrySetResult(approved ? null : reason);
            }
            NetworkService.Instance.PacketReceived += OnPacket;

            // 학번(16바이트) + 이름을 담은 로그인 패킷 전송 → 교수 PC 현황판에 표시됨
            byte[] loginPayload = new byte[80];
            Encoding.UTF8.GetBytes(Student.StudentNumber).CopyTo(loginPayload, 0);
            Encoding.UTF8.GetBytes(Student.StudentName).CopyTo(loginPayload, 16);
            NetworkService.Instance.SendPacket(PacketType.StudentLogin, loginPayload);

            // 회신이 오지 않으면 승인된 것으로 보고 넘어간다. 회신 하나 때문에 로그인이 막히지 않게 한다.
            Task finished = await Task.WhenAny(response.Task, Task.Delay(TimeSpan.FromSeconds(3)));
            NetworkService.Instance.PacketReceived -= OnPacket;
            string? rejection = finished == response.Task ? response.Task.Result : null;

            // 같은 학번이 이미 접속해 있으면 교수 PC 가 받지 않는다(ProfessorServer::HandleLogin).
            // 연결을 끊고 로그인 화면에 머문다.
            if (rejection != null)
            {
                NetworkService.Instance.Disconnect();

                if (rejection == LoginResponsePayload.DuplicateStudentId)
                {
                    NumberError = "이미 접속 중인 학번입니다.";
                    MessageBox.Show(
                        $"학번 {Student.StudentNumber}(으)로 이미 접속한 학생이 있어 들어갈 수 없습니다.\n" +
                        "학번을 다시 확인해 주세요.\n\n" +
                        "방금 연결이 끊겨 다시 들어오는 중이라면 20초쯤 뒤에 다시 시도해 주세요.",
                        "로그인 거절", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else
                {
                    MessageBox.Show($"교수님 PC가 접속을 받지 않았습니다. ({rejection})",
                                    "로그인 거절", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                return;
            }

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
            else if (!IsKoreanName(Student.StudentName))
                NameError = "이름은 한글로만 입력해 주세요.";
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
            if (string.IsNullOrEmpty(Student.StudentName) || !IsKoreanName(Student.StudentName))
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
