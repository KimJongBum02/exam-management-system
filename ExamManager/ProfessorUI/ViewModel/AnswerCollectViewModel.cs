using NetworkLib;
using ProfessorUI.Service;
using ProfessorUI.ViewModel;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Input;

public class AnswerCollectViewModel : INotifyPropertyChanged
{
        public bool IsContainerEnabled => ExamState.IsExamStarted;
        public ICommand CollectAllCommand { get; }

    public AnswerCollectViewModel()
    {
        CollectAllCommand = new RelayCommand(ExecuteCollectAll);

        // ⭐ 전역 상태가 변하면 나 자신도 알림을 보냅니다. (이게 빠졌었습니다!)
        ExamState.StateChanged += () => OnPropertyChanged(nameof(IsContainerEnabled));
    }

    private void ExecuteCollectAll(object obj)
    {
        // 접속 중인 학생에게만 보낸다.
        // 시험 20분 뒤부터 개별 제출하고 나간 학생은 이미 접속이 끊겨 있고,
        // 답안도 이미 받아 두었으므로 다시 요청할 필요가 없다.
        var targets = StudentStore.Instance.Students.Where(s => s.IsConnected).ToList();
        if (targets.Count == 0)
        {
            MessageBox.Show("접속 중인 학생이 없습니다.", "알림");
            return;
        }

        // 답안 묶음을 잠글 암호. 배포 때 쓴 것과 같은 암호를 써야 교수가 열 수 있다.
        string password = FileDeployState.Password ?? "";

        foreach (var student in targets)
        {
            // 폴더명은 비워 보낸다 — 학생마다 시험 폴더가 다를 수 있어
            // 교수가 지정하지 않고 학생이 자기 해제 폴더를 묶는다.
            NetworkService.Instance.SendToSession(
                student.SessionId,
                PacketType.ExamSubmitRequest,
                ExamSubmitPayload.Encode("", password));

            student.Status = "제출 대기";
        }

        MessageBox.Show($"{targets.Count}명에게 답안 제출을 요청했습니다.", "알림");
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

