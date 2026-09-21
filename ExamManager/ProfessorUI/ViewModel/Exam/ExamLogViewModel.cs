using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using ProfessorUI.Model;
using ProfessorUI.Service;

namespace ProfessorUI.ViewModel
{
    // 시험 로그 한 줄. 학생 한 명의 출석과 부정행위를 모아 놓은 것이다.
    //
    // 학생 정보는 StudentStore, 부정행위는 AlertStore 에 따로 쌓인다.
    // 둘을 학번으로 이어 붙이는 일을 여기서 한다.
    public class ExamLogRow : INotifyPropertyChanged
    {
        public ExamLogRow(StudentStatusViewModel student)
        {
            Student = student;

            // 이름이 늦게 들어오거나 답안을 내면 출석 표시가 달라진다. 학생 쪽 변화를 그대로 따라간다.
            student.PropertyChanged += (_, _) => NotifyAll();
        }

        public StudentStatusViewModel Student { get; }

        public string StudentId => Student.StudentId;
        public string Name => Student.Name;

        // 출석 여부. 수강생 명단이 없어 "접속해서 시험을 시작했는가"로 판단한다.
        // 답안을 낸 학생은 시작 보고가 늦거나 빠졌더라도 응시한 것이 분명하므로 함께 본다.
        public string AttendanceText => Student.HasEverStarted || Student.IsAnswerSubmitted
                                      ? "출석" : "미응시";

        // 부정행위 건수와 내역. 목록을 다시 셀 때 ExamLogViewModel 이 채워 준다.
        private int _cheatCount;
        public int CheatCount
        {
            get => _cheatCount;
            internal set { _cheatCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasCheated)); }
        }

        private string _cheatDetail = string.Empty;
        public string CheatDetail
        {
            get => _cheatDetail;
            internal set { _cheatDetail = value; OnPropertyChanged(); }
        }

        // 부정행위가 없는 학생 줄에는 아무것도 띄우지 않기 위한 값이다.
        public bool HasCheated => _cheatCount > 0;

        private void NotifyAll() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // 시험 로그 화면이 쓰는 표.
    //
    // 화면은 메뉴를 옮길 때마다 새로 만들어지므로 목록은 여기(UiContext 가 들고 있는 뷰모델)에 둔다.
    // 학생이 들어오거나 경고가 올라오면 표를 다시 맞춘다.
    public class ExamLogViewModel : INotifyPropertyChanged
    {
        public ObservableCollection<ExamLogRow> Rows { get; } = new();

        public ExamLogViewModel()
        {
            StudentStore.Instance.Students.CollectionChanged += (_, _) => Rebuild();
            AlertStore.Instance.Alerts.CollectionChanged += OnAlertsChanged;

            Rebuild();
        }

        public int TotalCount => Rows.Count;
        public int AttendedCount => Rows.Count(r => r.AttendanceText == "출석");
        public int CheatedCount => Rows.Count(r => r.HasCheated);

        // 학생 목록이 바뀌면 줄을 다시 만든다. 학번순으로 둔다 — 출석부와 같은 순서라 대조하기 쉽다.
        private void Rebuild()
        {
            Rows.Clear();
            foreach (var student in StudentStore.Instance.Students.OrderBy(s => s.StudentId))
                Rows.Add(new ExamLogRow(student));

            ApplyAlerts();
            NotifyCounts();
        }

        private void OnAlertsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            ApplyAlerts();
            NotifyCounts();
        }

        // 부정행위 건수·내역을 학번으로 이어 붙인다.
        //
        // 세는 것은 Cheating 뿐이다. 허용 프로그램을 닫은 기록(Reference)과
        // 감시가 걸리지 않았다는 경고(Security)는 부정행위가 아니라 참고·확인용이다.
        private void ApplyAlerts()
        {
            var cheats = AlertStore.Instance.Alerts
                .Where(a => a.Kind == AlertKind.Cheating)
                .GroupBy(a => a.StudentId)
                .ToDictionary(g => g.Key, g => g.Reverse().ToList());   // 먼저 일어난 것이 위로

            foreach (var row in Rows)
            {
                if (cheats.TryGetValue(row.StudentId, out var items))
                {
                    row.CheatCount = items.Count;
                    row.CheatDetail = string.Join(Environment.NewLine,
                        items.Select(a => $"{a.Time} {a.Description}"));
                }
                else
                {
                    row.CheatCount = 0;
                    row.CheatDetail = string.Empty;
                }
            }
        }

        private void NotifyCounts()
        {
            OnPropertyChanged(nameof(TotalCount));
            OnPropertyChanged(nameof(AttendedCount));
            OnPropertyChanged(nameof(CheatedCount));
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
