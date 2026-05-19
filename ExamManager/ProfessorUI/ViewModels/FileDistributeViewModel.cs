using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;

namespace ProfessorUI.ViewModels
{
    public class FileDistributeViewModel : INotifyPropertyChanged
    {
        // (가짜 데이터) 1번 화면에서 파일 준비가 끝났는지 여부
        // 실제로는 두 뷰모델이 데이터를 공유해야 하지만, 테스트를 위해 임시로 false로 둡니다.
        private bool _isFilePrepared = false;

        private string _validationMessage;
        public string ValidationMessage
        {
            get => _validationMessage;
            set { _validationMessage = value; OnPropertyChanged(); }
        }

        private int _deployProgressValue = 0;
        public int DeployProgressValue
        {
            get => _deployProgressValue;
            set { _deployProgressValue = value; OnPropertyChanged(); }
        }

        private string _deployProgressText = "0 / 20";
        public string DeployProgressText
        {
            get => _deployProgressText;
            set { _deployProgressText = value; OnPropertyChanged(); }
        }

        private string _deployStatusMessage = "전송 대기 중";
        public string DeployStatusMessage
        {
            get => _deployStatusMessage;
            set { _deployStatusMessage = value; OnPropertyChanged(); }
        }

        public ICommand StartDeployCommand { get; }

        public FileDistributeViewModel()
        {
            StartDeployCommand = new RelayCommand(ExecuteStartDeploy);
        }

        private async void ExecuteStartDeploy(object obj)
        {
            if (!_isFilePrepared)
            {
                ValidationMessage = "⚠️ 파일 준비 및 암호화를 먼저 완료해 주세요.";
                await Task.Delay(2000); // 2초 대기
                ValidationMessage = ""; // 메시지 지우기

                // 테스트 편의상: 한 번 경고를 본 뒤에는 배포가 되도록 스위치를 켭니다.
                _isFilePrepared = true;
                return;
            }

            DeployStatusMessage = "학생 PC로 파일 전송 중...";

            for (int i = 0; i <= 20; i++)
            {
                DeployProgressValue = i;
                DeployProgressText = $"{i} / 20";
                await Task.Delay(100);
            }

            DeployStatusMessage = "모든 학생에게 배포가 완료되었습니다.";
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}