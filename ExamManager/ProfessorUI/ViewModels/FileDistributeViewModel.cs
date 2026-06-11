using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using ProfessorUI.Stores;

namespace ProfessorUI.ViewModels
{
    // ⭐ 개별 학생 데이터를 관리하는 클래스
    public class StudentItem : INotifyPropertyChanged
    {
        private bool _isSelected = true; // 기본적으로 모두 선택
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public string Name { get; set; }

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

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public class FileDistributeViewModel : INotifyPropertyChanged
    {
        private bool _isFilePrepared = false; // 테스트용 플래그
        private bool _isDeploying = false; // 중복 실행 방지용

        // ⭐ 학생 목록을 담을 컬렉션
        public ObservableCollection<StudentItem> Students { get; } = new ObservableCollection<StudentItem>();

        private string _validationMessage;
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
            StartDeployCommand = new RelayCommand(ExecuteStartDeploy);
            SelectAllCommand = new RelayCommand(o => SetAllSelection(true));
            DeselectAllCommand = new RelayCommand(o => SetAllSelection(false));

            // 테스트용 학생 10명 생성
            for (int i = 1; i <= 10; i++)
            {
                Students.Add(new StudentItem { Name = $"학생 {i} (PC-{i:D2})" });
            }
        }

        // 전체 선택/해제 로직
        private void SetAllSelection(bool isSelected)
        {
            if (_isDeploying) return; // 전송 중에는 선택 변경 불가
            foreach (var student in Students)
            {
                student.IsSelected = isSelected;
            }
        }

        private async void ExecuteStartDeploy(object obj)
        {
            // ⭐ 1. 이제 내부에 있는 가짜 변수가 아닌, 공용 저장소의 상태를 확인합니다.
            if (!FileDeployState.IsFilePrepared)
            {
                ValidationMessage = "⚠️ 1단계: 파일 준비 및 암호화를 먼저 완료해 주세요.";
                await Task.Delay(5000); // 2초 대기
                ValidationMessage = ""; // 메시지 지우기

                // ❌ 임시 통과되도록 했던 아래 코드를 완전히 삭제합니다!
                // _isFilePrepared = true;

                return; // 파일이 준비 안 됐으면 무조건 배포 중단
            }

            // 2. 선택된 학생이 있는지 체크
            var selectedStudents = Students.Where(s => s.IsSelected).ToList();
            if (selectedStudents.Count == 0)
            {
                ValidationMessage = "⚠️ 배포할 학생을 최소 한 명 이상 선택해 주세요.";
                await Task.Delay(5000);
                ValidationMessage = "";
                return;
            }

            if (_isDeploying) return;
            _isDeploying = true;

            DeployStatusMessage = $"{selectedStudents.Count}명의 학생 PC로 파일 전송 중...";

            // 3. 선택된 학생들의 진행 상황 초기화
            foreach (var student in selectedStudents)
            {
                student.ProgressValue = 0;
                student.StatusText = "전송 준비...";
            }

            // 4. 각각의 학생에게 '동시에' 배포하는 애니메이션 (Task.WhenAll 사용)
            Random rand = new Random();
            var deployTasks = selectedStudents.Select(async student =>
            {
                student.StatusText = "전송 중";

                // 실제 전송처럼 보이기 위해 학생마다 미세하게 다른 속도 부여
                int speedDelay = rand.Next(10, 40);

                for (int i = 0; i <= 100; i += 2)
                {
                    student.ProgressValue = i;
                    student.StatusText = $"{i}%";
                    await Task.Delay(speedDelay);
                }

                student.StatusText = "완료";
            });

            // 모든 배포 작업이 끝날 때까지 대기
            await Task.WhenAll(deployTasks);

            DeployStatusMessage = "선택한 모든 학생에게 배포가 완료되었습니다.";
            _isDeploying = false;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}