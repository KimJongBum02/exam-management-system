using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ProfessorUI.ViewModel
{
    // 시험 진행 단계 하나.
    // 상단 미니 단계 표시(동그라미)와 그 단계에서 보여줄 화면을 함께 들고 있다.
    public class ExamStepViewModel : INotifyPropertyChanged
    {
        private bool _isCurrent;

        public ExamStepViewModel(int number, string title, object content, double contentMaxWidth)
        {
            Number = number;
            Title = title;
            Content = content;
            ContentMaxWidth = contentMaxWidth;
        }

        // 동그라미 안에 찍히는 번호 (1부터).
        public int Number { get; }

        // 동그라미에 마우스를 올렸을 때 뜨는 단계 이름.
        public string Title { get; }

        // 본문에 끼워 넣을 화면의 뷰모델. App.xaml의 DataTemplate이 실제 View로 바꿔 준다.
        public object Content { get; }

        // 본문 최대 너비.
        // 목록이 들어가는 화면은 넓게, 버튼 하나짜리 카드는 좁게 두어야 읽기 좋다.
        public double ContentMaxWidth { get; }

        // 첫 단계 앞에는 연결선을 그리지 않는다.
        public bool HasConnector => Number > 1;

        // 지금 보고 있는 단계인지. 동그라미에 색이 들어오는 조건이다.
        public bool IsCurrent
        {
            get => _isCurrent;
            set { _isCurrent = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
