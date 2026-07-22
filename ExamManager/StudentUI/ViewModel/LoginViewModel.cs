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

                if (TryLogin())
                {
                    ShowIPInput?.Invoke();
                }
            });
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

            _navigationStore.CurrentViewModel = new WaitingViewModel(_navigationStore, Student);
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
