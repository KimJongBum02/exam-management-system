using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using ProfessorUI.Service;

namespace ProfessorUI.ViewModel
{
    // 대시보드 학생 칸 목록. 수강생 명단(StudentExcelStore)과 접속한 학생(StudentStore)을 학번으로 이어 붙인다.
    //
    // 화면은 메뉴를 옮길 때마다 새로 만들어지므로 목록은 여기(UiContext)에 두고 늘 맞춰 둔다.
    // 명단을 불러오면 칸을 다시 만들고, 학생이 처음 접속하면 명단 칸에 붙이거나 새 칸을 뒤에 더한다.
    public class StudentBoardViewModel : INotifyPropertyChanged
    {
        public ObservableCollection<StudentCardViewModel> Cards { get; } = new();

        public StudentBoardViewModel()
        {
            StudentStore.Instance.Students.CollectionChanged += OnStudentsChanged;
            StudentExcelStore.Changed += Rebuild;
            Rebuild();
        }

        public bool HasRoster => StudentExcelStore.HasRoster;

        // 명단 한 줄 요약. 칸을 하나씩 세지 않아도 빠진 학생과 잘못 들어온 학생 수를 바로 본다.
        public string RosterSummaryText
        {
            get
            {
                if (!StudentExcelStore.HasRoster) return string.Empty;

                int connected = Cards.Count(c => c.Roster != null && c.IsConnected);
                int notInRoster = Cards.Count(c => c.IsNotInRoster && c.IsConnected);
                int nameMismatch = Cards.Count(c => c.IsNameMismatch && c.IsConnected);

                string text = $"수강생 명단 {StudentExcelStore.FileName} · {StudentExcelStore.Entries.Count}명 중 {connected}명 접속";
                if (notInRoster > 0) text += $" · 명단에 없는 학번 {notInRoster}명";
                if (nameMismatch > 0) text += $" · 이름이 명단과 다른 학생 {nameMismatch}명";
                return text;
            }
        }

        // 명단 학생은 엑셀 순서대로, 명단에 없는 학생은 그 뒤에 둔다. 명단이 없으면 모두 뒤쪽 무리라 예전처럼 IP 순이 된다.
        private void Rebuild()
        {
            foreach (var card in Cards)
            {
                card.Detach();
                card.PropertyChanged -= OnCardChanged;
            }
            Cards.Clear();

            var students = StudentStore.Instance.Students;
            var roster = StudentExcelStore.Entries;
            for (int i = 0; i < roster.Count; i++)
                Add(new StudentCardViewModel(roster[i], i, students.FirstOrDefault(s => s.StudentId == roster[i].StudentId)));

            foreach (var student in students.Where(s => StudentExcelStore.Find(s.StudentId) == null))
                Add(new StudentCardViewModel(null, int.MaxValue, student));

            NotifySummary();
        }

        // 학생 목록에는 처음 접속한 학생만 더해진다(끊겨도 줄은 남는다).
        private void OnStudentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems == null) return;

            foreach (StudentStatusViewModel student in e.NewItems)
            {
                var rosterCard = Cards.FirstOrDefault(c => c.Roster != null && !c.HasStudent && c.StudentId == student.StudentId);
                if (rosterCard != null) rosterCard.Attach(student);
                else Add(new StudentCardViewModel(null, int.MaxValue, student));
            }
            NotifySummary();
        }

        private void Add(StudentCardViewModel card)
        {
            card.PropertyChanged += OnCardChanged;
            Cards.Add(card);
        }

        private void OnCardChanged(object? sender, PropertyChangedEventArgs e) => NotifySummary();

        private void NotifySummary()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasRoster)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RosterSummaryText)));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
