using NetworkLib; // 추가
using ProfessorUI.Service;
using ProfessorUI.ViewModel;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

public class AnswerCollectViewModel : INotifyPropertyChanged
{
    public bool IsContainerEnabled => ExamState.IsExamStarted;

    // 시험이 종료(SubmitRequested 이상)되면 true → 시험 종료 버튼을 비활성화한다.
    // 상태 초기화(Waiting)가 되면 다시 false로 돌아온다.
    public bool IsExamEnded => ExamState.CurrentPhase >= ExamPhase.SubmitRequested;

    public ICommand EndExamCommand { get; }

    public AnswerCollectViewModel()
    {
        EndExamCommand = new RelayCommand(ExecuteEndExam, canExecute: o => IsContainerEnabled && !IsExamEnded);

        ExamState.StateChanged += () =>
        {
            OnPropertyChanged(nameof(IsContainerEnabled));
            OnPropertyChanged(nameof(IsExamEnded));
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        };
    }

    // [시험 종료 및 답안 수집] 버튼 — 시험을 끝내고 곧바로 전원의 답안을 걷는다.
    private void ExecuteEndExam(object obj)
    {
        var result = MessageBox.Show("시험을 종료하고 모든 학생의 답안을 수집하시겠습니까?", "시험 종료 확인",
                                     MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        EndExam();
        RequestAllAnswers();

        MessageBox.Show("시험을 종료하고 답안 수집을 요청했습니다.\n학생이 답안을 보내면 자동으로 저장됩니다.",
                        "안내", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // 시험을 실제로 끝낸다. 확인 창은 부르는 쪽에서 띄운다.
    private static void EndExam()
    {
        // 학생 PC에 시험이 끝났음을 알린다. 학생은 이 신호를 받고 프로세스 감시를 멈춘다.
        // 이걸 보내지 않으면 시험이 끝나도 학생 PC에서 메모장이 계속 강제 종료되고,
        // 답안 수집 때 압축에 쓰는 7za.exe가 부정행위로 적발된다.
        // 알림창보다 먼저 보내야 교수가 확인을 누를 때까지 학생이 기다리지 않는다.
        NetworkService.Instance.Broadcast(
            PacketType.ExamPhaseChange,
            ExamPhasePayload.Encode(ExamPhase.SubmitRequested, "시험이 종료되었습니다."));

        // 시험 단계를 SubmitRequested(3)로 변경 -> StateChanged 이벤트 자동 발생
        // -> IsExamEnded가 true가 되어 버튼이 비활성화된다
        ExamState.CurrentPhase = ExamPhase.SubmitRequested;
    }

    // 접속 중인 모든 학생에게 답안 제출을 요청한다.
    // 학생이 답안을 보내오면 AnswerCollectService 가 받아 저장하고
    // StudentStore.MarkAnswerSubmitted 를 호출하여 IsAnswerSubmitted 가 true 로 바뀐다.
    private static void RequestAllAnswers()
    {
        byte[] payload = ExamSubmitPayload.Encode("", FileDeployState.Password ?? "");
        NetworkService.Instance.Broadcast(PacketType.ExamSubmitRequest, payload);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}