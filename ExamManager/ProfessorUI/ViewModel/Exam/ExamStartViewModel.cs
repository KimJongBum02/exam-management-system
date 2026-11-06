using ProfessorUI.Service;
using ExamManager.Shared;
using System.Windows;
using System.Windows.Input;

namespace ProfessorUI.ViewModel
{
    public class ExamStartViewModel : UiBinding
    {
        // ⭐ 공용 저장소의 상태에 따라 버튼 활성화 여부 결정
        public bool IsStartButtonEnabled => SendFileState.IsFileDistributed && !ExamState.IsExamStarted;

        // ⭐ 컨테이너 전체를 흐리게(비활성화) 만들 속성
        public bool IsContainerEnabled => !ExamState.IsExamStarted;

        // 왜 [시험 시작 실행]을 누를 수 없는지. 누를 수 있으면 빈 문자열이고, 그때 화면은 이 줄을 숨긴다.
        //
        // 잠기는 이유가 두 가지인데 화면에 한 가지만 적어 두었더니, 앞 시험을 정리하지 않아 잠긴 경우에도
        // "파일 배포를 실행해야 한다"고 적혀 배포를 이미 끝낸 교수가 원인을 찾을 수 없었다.
        public string StartLockReason =>
              ExamState.IsExamRunning
                  ? "시험이 진행 중입니다. 지각생에게는 시험 관리 창의 파일 재배포를 쓰십시오."
            : ExamState.IsExamStarted
                  ? "앞 시험이 아직 정리되지 않았습니다. 시험 종료 · 답안 수집 화면 맨 아래 [처음 화면으로]를 눌러 주세요."
            : !SendFileState.IsFileDistributed
                  ? "2단계에서 파일 배포를 한 번 이상 실행해야 시험을 시작할 수 있습니다."
            : string.Empty;

        public ICommand StartExamCommand { get; }

        public ExamStartViewModel()
        {
            SendFileState.StateChanged += OnStateChanged;
            ExamState.StateChanged += OnStateChanged; // 시험 상태 변화도 구독
            StartExamCommand = new UiCommand(ExecuteStartExam);
        }

        private void OnStateChanged()
        {
            OnPropertyChanged(nameof(IsStartButtonEnabled));
            OnPropertyChanged(nameof(IsContainerEnabled));
            OnPropertyChanged(nameof(StartLockReason));
        }

        private void ExecuteStartExam(object obj)
        {
            // 보내는 순서와 단계 올리기는 ExamFlowService 가 맡는다.
            // 알림창보다 먼저 보내야 교수가 확인을 누를 때까지 학생이 기다리지 않는다.
            ExamFlowService.Instance.StartExam();

            MessageBox.Show("시험을 시작합니다.", "알림");
        }
    }
}