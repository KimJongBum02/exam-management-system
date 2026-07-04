using ProfessorUI.Service;
using ProfessorUI.ViewModel;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
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
        foreach (var student in StudentStore.Instance.Students)
        {
            if (student.Status == "대기")
            {
                student.IsFileReceived = true;
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

