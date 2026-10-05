using System.Windows;
using ProfessorUI.Common;
using ProfessorUI.ViewModel;

namespace ProfessorUI.View.Professor
{
    // 중요 공지를 등록·수정·내리는 창. 상단바 가운데의 공지 칸을 누르면 뜬다.
    //
    // 전체 공지(알림·채팅 화면)는 학생 화면에 팝업으로 한 번 뜨고 닫히지만,
    // 중요 공지는 내릴 때까지 학생·교수 앱 상단 가운데에 계속 보인다.
    // 답안 파일 이름 형식처럼 시험 내내 지켜야 할 것을 적어 둔다.
    public partial class ImportantNoticeWindow : Window
    {
        private readonly ChatViewModel _chat = UiContext.Instance.Chat;

        public ImportantNoticeWindow()
        {
            InitializeComponent();

            // 올린 공지가 있으면 그 문구를 고쳐 다시 올리거나 내린다.
            NoticeBox.Text = _chat.ImportantNotice;
            ClearButton.Visibility = _chat.HasImportantNotice ? Visibility.Visible : Visibility.Collapsed;
            Loaded += (_, _) =>
            {
                NoticeBox.Focus();
                NoticeBox.CaretIndex = NoticeBox.Text.Length;
            };

            ApplyButton.Click += (_, _) => Apply();
            ClearButton.Click += (_, _) =>
            {
                _chat.SetImportantNotice(string.Empty);
                Close();
            };
        }

        private void Apply()
        {
            if (string.IsNullOrWhiteSpace(NoticeBox.Text))
            {
                ErrorText.Text = "공지 내용을 입력해 주세요.";
                return;
            }

            _chat.SetImportantNotice(NoticeBox.Text);
            Close();
        }
    }
}
