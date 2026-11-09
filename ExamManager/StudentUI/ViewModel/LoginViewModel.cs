using NetworkLib;
using ExamManager.Shared;
using StudentUI.Model;
using StudentUI.Service;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;

namespace StudentUI.ViewModel
{
    public class LoginViewModel : UiBinding
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

        // 명단에 없다고 거절당한 학번. 같은 학번으로 또 거절당하면 오타가 아니라는 뜻이다 —
        // 강의실끼리 망이 분리돼 있지 않아 옆 강의실 교수 PC 에 붙은 것이다.
        // 시험 화면까지 들어갔다 되돌려 보내진 경우에는 그 학번을 넘겨받는다(ExamViewModel.Logout).
        private string _rejectedStudentId;

        // 이번 시도에서는 교수 PC 를 찾지 말고 주소를 직접 받는다. 위의 '같은 학번 두 번째 거절'일 때만 선다 —
        // 다시 찾으면 같은(엉뚱한) 교수 PC 가 또 먼저 답해 끝없이 거절당한다. 한 번 쓰고 내린다.
        private bool _askProfessorAddress;

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

        public LoginViewModel(NavigationStore navigationStore, string rejectedStudentId = "")
        {
            _navigationStore = navigationStore;
            _rejectedStudentId = rejectedStudentId;
            LoginCommand = new UiCommand(_ => { _ = LoginAsync(); });
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
                // 보통 한 번만 돈다. 같은 학번으로 '명단에 없다'를 두 번 받았을 때만 한 번 더 돌아
                // 교수 PC 주소를 직접 받는다(_askProfessorAddress). 다시 '찾게' 하면 안 된다 —
                // 강의실끼리 망이 분리돼 있지 않아 옆 강의실 교수 PC 가 또 먼저 답하고,
                // 바르게 친 학번이 끝없이 거절당해 제 강의실에 영원히 못 들어간다(2026-11-05 확인).
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    (string Ip, int Port)? professor;
                    // 학번을 고쳐 쳤다면 다시 찾아 봐야 한다 — 이 깃발은 거절당한 그 학번에만 해당한다.
                    if (_askProfessorAddress && _rejectedStudentId == Student.StudentNumber)
                    {
                        _askProfessorAddress = false;
                        professor = RequestManualAddress?.Invoke();
                    }
                    else
                    {
                        // 학번·이름만 치면 들어갈 수 있도록 교수 PC 를 스스로 찾는다.
                        ConnectStatus = "교수 PC를 찾는 중…";
                        professor = await AutoConnectService.FindAsync();
                        ConnectStatus = string.Empty;

                        // 못 찾는 경우(브로드캐스트를 막는 공유기, 교수 PC 가 다른 대역)에는 예전처럼 IP 를 직접 받는다.
                        professor ??= RequestManualAddress?.Invoke();
                    }
                    if (professor == null) return;

                    await CompleteLogin(professor.Value.Ip, professor.Value.Port);

                    // 들어갔거나, 처음 거절이라 빨간 글씨만 띄우고 다시 치게 하는 경우다.
                    if (!_askProfessorAddress) return;
                }
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

            // 연결은 배경 스레드에서 한다. 네이티브 쪽에 제한(CONNECT_TIMEOUT_MS)을 걸어 뒀어도,
            // 그 몇 초 동안 화면 스레드를 붙잡으면 창이 굳어 윈도우가 '응답 없음'을 띄운다.
            // 주소를 잘못 친 학생이 가장 먼저 겪는 자리라 특히 멈추면 안 된다.
            ConnectStatus = $"{ip}:{port} 에 연결하는 중…";
            bool connected = await Task.Run(() => NetworkService.Instance.Connect(ip, port));
            ConnectStatus = string.Empty;

            if (!connected)
            {
                MessageBox.Show($"서버에 연결하지 못했습니다 : {ip}:{port}\n주소와 서버 실행 여부를 확인해 주세요.",
                    "연결 실패");
                return;
            }

            // 학번·이름을 보내고 승인을 기다린다. 승인되면 로그인 뒤에 보낼 것들까지 LoginService 가 보낸다.
            string? rejection = await LoginService.LoginAsync(Student);

            // 같은 학번이 이미 접속해 있으면 교수 PC 가 받지 않는다(ProfessorServer::HandleLogin).
            // 연결을 끊고 로그인 화면에 머문다.
            if (rejection != null)
            {
                NetworkService.Instance.Disconnect();

                if (rejection == LoginService.NoResponse)
                {
                    // 포트는 열려 있어 연결은 됐는데 교수 앱이 아니었다(그 포트를 다른 프로그램이 쓰는 경우).
                    // 학번 탓이 아니므로 학번 칸에는 빨간 글씨를 띄우지 않는다.
                    MessageBox.Show("서버가 응답하지 않습니다.",
                                    "로그인 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else if (rejection == LoginResponsePayload.DuplicateStudentId)
                {
                    NumberError = "이미 접속 중인 학번입니다.";
                    MessageBox.Show(
                        $"{Student.StudentNumber}(으)로 이미 접속한 학생이 있어 연결에 실패 했습니다.\n" +
                        "학번을 다시 확인해 주세요.",
                        "로그인 거절", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else if (rejection == LoginService.NotInRoster)
                {
                    // 붙은 교수 PC 의 명단에 없는 학번이다. 대개 오타라서 팝업을 띄우지 않는다 —
                    // 학번 칸 밑의 빨간 글씨만 보고 다시 치면 된다.
                    // 같은 학번으로 또 거절당하면 오타가 아니다. 그때만 교수 PC 주소를 직접 받는다.
                    NumberError = "명단에 없는 학번입니다.";
                    _askProfessorAddress = _rejectedStudentId == Student.StudentNumber;
                    _rejectedStudentId = Student.StudentNumber;
                }
                else if (rejection == LoginService.RosterNotReady)
                {
                    // 학번 탓이 아니다. 빨간 글씨를 띄우면 학생이 제 학번을 의심하게 되므로 띄우지 않는다.
                    MessageBox.Show("서버에 명단이 없어 현재 접속이 불가능합니다.",
                                    "로그인 대기", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("서버와의 연결에 실패하였습니다.",
                                    "로그인 거절", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                return;
            }

            // 이제부터 교수 PC 와의 연결이 끊기면 스스로 다시 찾아 붙는다.
            ReconnectService.Enable(Student, ip, port);

            // 교수 UI와 동일한 '시험 준비 마법사' 대시보드로 진입 (1단계: 대기부터 시작)
            _navigationStore.CurrentViewModel = new ExamViewModel(_navigationStore, Student);
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
    }
}
