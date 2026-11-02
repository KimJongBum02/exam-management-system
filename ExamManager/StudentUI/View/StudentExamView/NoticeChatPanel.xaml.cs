using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using StudentUI.ViewModel;

namespace StudentUI.View.StudentExamView
{
    // 두 PIP 창(자유 · 시험)이 함께 쓰는 알림·채팅 칸.
    // 주 창이 숨은 동안에도 교수의 공지와 말이 학생에게 닿게 하는 것이 이 칸의 목적이다.
    public partial class NoticeChatPanel : UserControl
    {
        // 보는 곳은 하나뿐이다. 시험 화면의 메신저와 같은 객체라 어디서 보든 같은 대화가 보인다.
        private readonly SharedChatViewModel _chat = SharedChatViewModel.Instance;

        // 떼어낼 때 쓰려고 들고 있는다. Loaded 는 두 번 이상 불릴 수 있어 한 번만 건다.
        private Window? _owner;

        public NoticeChatPanel()
        {
            InitializeComponent();
            DataContext = _chat;

            // 채팅창을 열어 둔 채로 새 말이 오면 맨 아래로 따라 내려간다.
            _chat.Messages.CollectionChanged += OnChatMessagesChanged;

            // 열어 둔 채로 받은 말은 읽은 것으로 처리한다.
            // 목록 변화(CollectionChanged)가 아니라 이 알림을 쓰는 이유: 목록에 넣은 **뒤에**
            // 안 읽음 수를 올리므로, 목록 쪽에서 읽음 처리를 하면 곧바로 1 로 되올라간다.
            _chat.MessageArrived += OnMessageArrivedWhileOpen;
            _chat.NoticeAdded += OnNoticeAddedWhileOpen;

            // 이 칸은 창이 아니라 Closed 가 없다. Unloaded 는 창이 살아 있는 동안에도 불릴 수 있어
            // 쓰지 않는다 — 담고 있는 창이 닫힐 때 떼어낸다.
            Loaded += (_, _) =>
            {
                if (_owner != null) return;
                _owner = Window.GetWindow(this);
                if (_owner != null) _owner.Closed += OnOwnerClosed;
            };
        }

        // 떼어내지 않으면 SharedChatViewModel 이 싱글턴이라 닫힌 창의 칸이 계속 매달려 있는다.
        private void OnOwnerClosed(object? sender, EventArgs e)
        {
            if (_owner != null) _owner.Closed -= OnOwnerClosed;
            _chat.Messages.CollectionChanged -= OnChatMessagesChanged;
            _chat.MessageArrived -= OnMessageArrivedWhileOpen;
            _chat.NoticeAdded -= OnNoticeAddedWhileOpen;
        }

        // ── 알림 · 채팅 ──
        // 카드가 좁아 한 번에 하나만 편다. 편 쪽은 읽은 것으로 처리해 배지를 지운다.
        private void OnToggleNotice(object sender, RoutedEventArgs e)
        {
            bool open = NoticePanel.Visibility != Visibility.Visible;
            NoticePanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            ChatPanel.Visibility = Visibility.Collapsed;

            if (open) _chat.MarkNoticesAsRead();
        }

        private void OnToggleChat(object sender, RoutedEventArgs e)
        {
            bool open = ChatPanel.Visibility != Visibility.Visible;
            ChatPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            NoticePanel.Visibility = Visibility.Collapsed;

            if (!open) return;

            // 열어 본 시점에 교수에게 '읽음'을 돌려보낸다(SharedChatViewModel 참고).
            _chat.MarkAsRead();
            ScrollChatToEnd();

            // 펼친 패널이 그려진 뒤라야 입력칸이 포커스를 받는다.
            Dispatcher.BeginInvoke(new Action(() => ChatInput.Focus()), DispatcherPriority.Loaded);
        }

        private void OnChatMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action != NotifyCollectionChangedAction.Add) return;
            ScrollChatToEnd();
        }

        // 채팅 패널을 펼쳐 둔 채로 말이 오면 바로 읽은 것으로 보고 교수에게 '읽음'을 돌려보낸다.
        // 이게 없으면 학생이 채팅을 보고 있는데도 배지가 1, 2, 3 으로 쌓인다.
        // 시험 화면에도 같은 처리가 있지만(StudentExamViewModel.OnChatMessageArrived) 그쪽은
        // 주 창의 드로어만 본다 — 주 창이 숨어 있을 때는 이 칸이 따로 챙겨야 한다.
        //
        // 담고 있는 창이 숨어 있으면 IsVisible 이 false 다. 펼쳐만 두었을 뿐 보고 있는 것이
        // 아니므로 그때는 세어 둔다(자유 PIP 와 시험 PIP 중 한쪽은 늘 숨어 있다).
        private void OnMessageArrivedWhileOpen()
        {
            if (IsVisible && ChatPanel.Visibility == Visibility.Visible) _chat.MarkAsRead();
        }

        // 알림 패널도 같다 — 펼쳐 둔 채로 공지가 오면 바로 읽은 것으로 본다.
        // 채팅과 달리 교수에게 돌려보낼 '읽음'은 없다(공지 수신 회신은 받는 즉시 따로 나간다).
        private void OnNoticeAddedWhileOpen()
        {
            if (IsVisible && NoticePanel.Visibility == Visibility.Visible) _chat.MarkNoticesAsRead();
        }

        // 말풍선이 그려진 뒤라야 끝까지 내려간다(시험 화면의 채팅과 같은 이유로 Loaded 우선순위).
        private void ScrollChatToEnd()
            => Dispatcher.BeginInvoke(new Action(() => ChatScroll.ScrollToEnd()), DispatcherPriority.Loaded);
    }
}
