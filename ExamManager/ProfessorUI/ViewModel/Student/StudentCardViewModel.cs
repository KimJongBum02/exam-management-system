using System.ComponentModel;
using ProfessorUI.Model;
using ProfessorUI.Service;

namespace ProfessorUI.ViewModel
{
    // 대시보드 학생 칸 하나. 명단의 한 줄(Roster)과 실제로 접속한 학생(Student)을 학번으로 묶는다.
    //   명단에만 있음           → 아직 접속 전. 빨강
    //   명단에도 있고 접속함     → 초록, 끊기면 다시 빨강. 로그인 때 친 이름이 명단과 다르면 경고 줄
    //   접속했는데 명단에 없음   → 학번을 잘못 쳤을 수 있어 경고 줄
    // 명단을 불러오지 않았으면 Roster 는 늘 비어 있고, 예전처럼 접속한 학생만 칸이 된다.
    public class StudentCardViewModel : INotifyPropertyChanged
    {
        public StudentCardViewModel(RosterEntry? roster, int rosterIndex, StudentStatusViewModel? student)
        {
            Roster = roster;
            RosterIndex = rosterIndex;
            if (student != null) Attach(student);
        }

        public RosterEntry? Roster { get; }

        // 엑셀에서 몇 번째 줄인지. 명단에 없는 학생은 int.MaxValue 라 명단 학생 뒤에 놓인다.
        public int RosterIndex { get; }

        // 접속한 적이 있는 학생. 명단 학생이 아직 접속하지 않았으면 null 이다.
        public StudentStatusViewModel? Student { get; private set; }
        public bool HasStudent => Student != null;

        public string StudentId => Roster?.StudentId ?? Student!.StudentId;
        public string Name => Roster?.Name ?? Student!.Name;
        public string Ip => Student?.Ip ?? string.Empty;

        public bool IsConnected => Student?.IsConnected == true;
        public bool IsAnswerSubmitted => Student?.IsAnswerSubmitted == true;
        public string ConnectionText => IsConnected ? "접속 중" : "미접속";
        public string Status => Student?.Status ?? "접속 전";

        // 마지막으로 접속한 시각. 아직 접속하지 않은 명단 학생은 학생 쪽 기본 문구와 같게 둔다.
        public string LastConnectedText => Student?.LastConnectedText ?? "접속 기록 없음";

        // 명단을 불러왔는데 명단에 없는 학번으로 들어왔다.
        public bool IsNotInRoster => Roster == null && StudentExcelStore.HasRoster && Student != null;

        // 학번은 명단에 있는데 로그인 때 친 이름이 다르다. 칸에는 명단 이름이 크게 보인다.
        public bool IsNameMismatch => Roster != null && Student != null && !StudentExcelStore.SameName(Roster.Name, Student.Name);

        public bool HasWarning => IsNotInRoster || IsNameMismatch;
        public string WarningText => IsNotInRoster ? "⚠ 명단에 없는 학번"
                                   : IsNameMismatch ? $"⚠ 입력 이름: {Student!.Name}"
                                   : string.Empty;

        // 칸을 보일지. 명단이 있을 때 명단에 없는 학생은 접속해 있거나 시험을 치른 동안만 보인다.
        // 학번을 잘못 쳤다가 다시 로그인한 학생의 빈 칸이 끝까지 남지 않게 한다.
        public bool IsShown => Roster != null || !StudentExcelStore.HasRoster ||
                               IsConnected || Student!.HasEverStarted || Student.IsAnswerSubmitted;

        // 명단 학생이 처음 접속하면 칸에 학생을 붙인다. 화면은 접속으로 받아 초록으로 깜빡인다.
        public void Attach(StudentStatusViewModel student)
        {
            Student = student;
            student.PropertyChanged += OnStudentChanged;
            Raise(nameof(Student), nameof(HasStudent), nameof(Ip), nameof(IsConnected), nameof(ConnectionText),
                  nameof(Status), nameof(LastConnectedText), nameof(IsNotInRoster), nameof(IsNameMismatch),
                  nameof(HasWarning), nameof(WarningText), nameof(IsShown));
        }

        // 명단을 새로 불러와 칸을 다시 만들 때 옛 칸이 학생 변화를 계속 받지 않게 끊는다.
        public void Detach()
        {
            if (Student != null) Student.PropertyChanged -= OnStudentChanged;
        }

        // 학생 쪽에서 바뀐 것만 그 이름으로 알린다. 화면은 IsConnected·IsAnswerSubmitted 이름을 보고 깜빡이므로
        // 상태 문구가 바뀔 때마다 모든 이름을 알리면 접속이 바뀌지 않았는데도 깜빡인다.
        private void OnStudentChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(StudentStatusViewModel.IsConnected):
                    Raise(nameof(IsConnected), nameof(ConnectionText), nameof(IsShown));
                    break;
                case nameof(StudentStatusViewModel.IsAnswerSubmitted):
                    Raise(nameof(IsAnswerSubmitted), nameof(IsShown));
                    break;
                case nameof(StudentStatusViewModel.HasExamStarted):
                    Raise(nameof(IsShown));
                    break;
                case nameof(StudentStatusViewModel.Name):
                    Raise(nameof(Name), nameof(IsNameMismatch), nameof(HasWarning), nameof(WarningText));
                    break;
                case nameof(StudentStatusViewModel.Status):
                    Raise(nameof(Status));
                    break;
                case nameof(StudentStatusViewModel.LastConnectedText):
                    Raise(nameof(LastConnectedText));
                    break;
                case nameof(StudentStatusViewModel.Ip):
                    Raise(nameof(Ip));
                    break;
            }
        }

        private void Raise(params string[] names)
        {
            foreach (string name in names)
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
