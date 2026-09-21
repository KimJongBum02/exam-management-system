using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using ProfessorUI.Service;
using ProfessorUI.Common;


namespace ProfessorUI.ViewModel
{
    // 시험 준비 2단계(파일 배포) 화면의 뷰모델.
    // 배포 표를 만들고 결과를 문구로 보여 준다 — 실제 전송과 판단은 SendFileService 가 맡는다.
    public class SendFileViewModel : INotifyPropertyChanged
    {
        private readonly SendFileService _deploy = SendFileService.Instance;

        private bool _isDeploying = false; // 중복 실행 방지용

        // ⭐ 핵심 1: XAML이 바라보는 활성화 여부 프로퍼티 (SendFileState 연동)
        public bool IsContainerEnabled => SendFileState.IsFilePrepared;

        // 배포 표가 쓰는 학생 목록. 현황판과 같은 목록을 그대로 본다 —
        // 따로 한 벌 더 들고 있으면 접속·이름 변화를 계속 맞춰 줘야 한다.
        public ObservableCollection<StudentStatusViewModel> Students => StudentStore.Instance.Students;

        private string _validationMessage = string.Empty;
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

        // 학생 한 명에게만 다시 보낸다. 지각생이나 전송에 실패한 학생을 시험 중에 합류시킬 때 쓴다.
        public ICommand RedeployOneCommand { get; }

        public SendFileViewModel()
        {
            // ⭐ 핵심 2: Command CanExecute 조건에 IsContainerEnabled 연결
            StartDeployCommand = new RelayCommand(
                ExecuteStartDeploy,
                canExecute: o => IsContainerEnabled && !_isDeploying
            );
            SelectAllCommand = new RelayCommand(
                o => SetAllSelection(true),
                canExecute: o => IsContainerEnabled
            );
            DeselectAllCommand = new RelayCommand(
                o => SetAllSelection(false),
                canExecute: o => IsContainerEnabled
            );

            RedeployOneCommand = new RelayCommand(
                ExecuteRedeployOne,
                canExecute: o => IsContainerEnabled
            );

            // ⭐ 핵심 3: static 클래스의 상태가 바뀌면 UI 전체 갱신 호출
            SendFileState.StateChanged += OnSendFileStateChanged;

            StudentStore.Instance.FileReceivedConfirmed += OnFileReceivedConfirmed;

            // 전송 진행률·실패를 학생 행에 반영
            _deploy.Progress += OnSendProgress;
            _deploy.Failed += OnSendFailed;
        }

        // 전송 진행률 (네이티브 스레드에서 올라온다)
        private void OnSendProgress(string sessionId, string studentId, int percent) => PostToUi(() =>
        {
            var row = Students.FirstOrDefault(s => s.StudentId == studentId);
            if (row == null || row.SendingSessionId != sessionId) return;

            row.DeployProgress = percent;
            row.DeployStatus = percent >= 100 ? "전송 완료" : $"전송 중 {percent}%";
        });

        // 전송 실패 (네이티브 스레드에서 올라온다)
        private void OnSendFailed(string sessionId, string studentId) => PostToUi(() =>
        {
            var row = Students.FirstOrDefault(s => s.StudentId == studentId);
            if (row == null || row.SendingSessionId != sessionId) return;

            row.SendingSessionId = null; // 실패했으므로 재전송을 다시 허용한다
            row.DeployProgress = 0;
            row.DeployStatus = "전송 실패";
        });

        // 콜백은 네이티브 스레드에서 올라오므로 UI 스레드로 넘겨 처리한다.
        private static void PostToUi(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted) return;
            dispatcher.BeginInvoke(action);
        }

        // static 상태 변경 시 호출되는 이벤트 핸들러
        private void OnSendFileStateChanged()
        {
            // XAML 바인딩 속성 갱신 알림
            // 1. IsContainerEnabled 속성 변경 알림 (XAML 바인딩 갱신)
            OnPropertyChanged(nameof(IsContainerEnabled));

            // 2. Command들의 CanExecute 상태를 다시 평가하도록 WPF UI 프레임워크에 알림
            CommandManager.InvalidateRequerySuggested();
        }

        // 학생이 '수신 완료' 응답을 보내오면 해당 행을 완료 표시
        // (늦게 받은 학생의 시험 시작은 SendFileService 가 맡는다)
        private void OnFileReceivedConfirmed(string studentId)
        {
            var row = Students.FirstOrDefault(s => s.StudentId == studentId);
            if (row == null) return;

            row.SendingSessionId = null; // 전송이 끝났으므로 재배포를 다시 허용한다
            row.DeployProgress = 100;
            row.DeployStatus = "수신완료";
        }

        // 전체 선택/해제 로직
        private void SetAllSelection(bool isSelected)
        {
            if (_isDeploying || !IsContainerEnabled) return; // 전송 중이거나 비활성화 시 선택 변경 불가
            foreach (var student in Students)
            {
                student.IsDeployTarget = isSelected;
            }
        }

        private void ExecuteStartDeploy(object? obj)
        {
            // 1. 압축/암호화가 끝났는지 확인
            string? problem = _deploy.PackageProblem();
            if (problem != null)
            {
                ValidationMessage = "⚠️ 1단계: " + problem;
                return;
            }

            // 2. 선택된 학생이 있는지 체크
            var selectedStudents = Students.Where(s => s.IsDeployTarget).ToList();
            if (selectedStudents.Count == 0)
            {
                ValidationMessage = "⚠️ 배포할 학생을 최소 한 명 이상 선택해 주세요.";
                return;
            }

            if (_isDeploying) return;
            _isDeploying = true;
            ValidationMessage = "";

            // 3. 선택된 학생별로 전송
            int sentCount = 0;
            foreach (var student in selectedStudents)
            {
                var result = _deploy.Send(student.StudentId, student.SendingSessionId);
                if (!result.Ok)
                {
                    // 이미 보내는 중이면 표를 건드리지 않는다. 그 밖에는 이유를 칸에 적는다.
                    student.DeployStatus = result.Problem switch
                    {
                        SendFileService.SendProblem.NotConnected => "미접속",
                        SendFileService.SendProblem.SendFailed => "전송 실패",
                        _ => student.DeployStatus,
                    };
                    continue;
                }

                student.SendingSessionId = result.SessionId;
                student.DeployProgress = 0;
                student.DeployStatus = "전송 중";
                sentCount++;
            }

            DeployStatusMessage = $"{sentCount}명에게 파일을 전송했습니다. 학생 수신 응답 대기 중...";
            _isDeploying = false;
        }

        // 학생 한 명에게만 재전송한다.
        // 현황판 행(StudentStatusViewModel)이나 학번 문자열 어느 쪽으로 불러도 되게 받아 둔다.
        private void ExecuteRedeployOne(object? parameter)
        {
            string? studentId = parameter switch
            {
                StudentStatusViewModel s => s.StudentId,
                string s => s,
                _ => null
            };
            if (string.IsNullOrEmpty(studentId)) return;

            // 누른 뒤 표에 바로 보이는 변화가 없으므로 결과를 창으로 알린다.
            string? problem = Redeploy(studentId);
            if (problem != null)
                MessageBox.Show(problem, "재배포", MessageBoxButton.OK, MessageBoxImage.Warning);
            else
                MessageBox.Show($"{DeployStatusMessage}{SendFileService.StartNote}", "재배포",
                                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // 여러 학생에게 한꺼번에 다시 보낸다(시험 관리 화면의 [접속 중인 학생 모두 재배포]).
        // 못 보낸 학생은 이유와 함께 한 창에 모아 알린다.
        public void RedeployMany(IEnumerable<StudentStatusViewModel> students)
        {
            int sent = 0;
            var failed = new List<string>();
            foreach (var student in students)
            {
                string? problem = Redeploy(student.StudentId);
                if (problem == null) sent++;
                else failed.Add($"{student.StudentId} {student.Name} — {problem}");
            }

            string message = $"{sent}명에게 시험 파일을 다시 보냈습니다.";
            if (sent > 0) message += SendFileService.StartNote;
            if (failed.Count > 0)
                message += $"\n\n보내지 못한 학생 {failed.Count}명:\n" + string.Join("\n", failed);

            MessageBox.Show(message, "재배포", MessageBoxButton.OK,
                failed.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }

        // 학생 한 명에게 시험 파일을 다시 보내고 표를 맞춘다. 보냈으면 null, 못 보냈으면 그 이유.
        private string? Redeploy(string studentId)
        {
            var row = Students.FirstOrDefault(s => s.StudentId == studentId);
            var result = _deploy.Send(studentId, row?.SendingSessionId);

            if (!result.Ok)
            {
                if (row != null && result.Problem == SendFileService.SendProblem.SendFailed)
                    row.DeployStatus = "전송 실패";
                return result.Message;
            }

            // 현황판에 없는 학생에게는 보낼 수 없다(서비스가 이미 걸러 준다).
            if (row == null) return "접속 중인 학생이 아닙니다.";

            row.SendingSessionId = result.SessionId;
            row.DeployProgress = 0;
            row.DeployStatus = "전송 중";
            DeployStatusMessage = $"{row.Name}({studentId}) 에게 다시 전송했습니다.";
            return null;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
