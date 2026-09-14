using System.Collections;
using System.Windows;
using System.Windows.Input;

namespace ProfessorUI.View.Professor
{
    // 감시 목록(기본 제공·직접 추가·허용)을 팝업으로 보여 준다.
    //
    // 목록을 화면에 펼쳐 두면 기본 항목이 길어 다음 단계 버튼까지 아래로 밀린다.
    // 그래서 화면에는 [보기] 버튼만 두고, 실제 목록은 이 창에서 본다.
    // 삭제는 넘겨받은 커맨드로 원본 목록에서 지운다 — 이 창의 목록은 그 원본에 연결돼 바로 갱신된다.
    public partial class ProgramListWindow : Window
    {
        // 삭제 버튼이 바인딩한다. 넘겨받은 RemoveCommand(금지/허용 각각)를 그대로 쓴다.
        public ICommand? RemoveCommand { get; }

        // title  — 창과 머리글에 보일 이름
        // items  — 보여 줄 목록(원본 컬렉션이나 그 뷰). 삭제하면 여기서 사라져 바로 반영된다.
        // remove — 삭제 커맨드(항목 문자열을 파라미터로 받는다)
        public ProgramListWindow(string title, IEnumerable items, ICommand remove)
        {
            InitializeComponent();

            RemoveCommand = remove;
            Title = title;
            HeaderText.Text = title;
            ProgramList.ItemsSource = items;

            // 비어 있으면 표 대신 안내를 보여 준다.
            bool empty = !items.GetEnumerator().MoveNext();
            EmptyText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            ProgramList.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        }
    }
}
