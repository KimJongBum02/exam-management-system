using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using ProfessorUI.ViewModel;

namespace ProfessorUI.View.Professor
{
    // 알림·채팅 화면. 학생 채팅은 학생별로 한 줄씩 묶고, 전체 공지는 누가 받았는지까지 보여 준다.
    //
    // 시간순으로 한 줄씩 쌓으면 학생 여럿이 동시에 말을 걸 때 누가 몇 번 불렀는지 알 수 없다.
    // 그래서 학생마다 한 줄을 두고, 안 읽은 대화가 있는 학생을 위로 올린다.
    public partial class ChatPage : UserControl
    {
        private readonly UiContext _ctx = UiContext.Instance;
        private readonly ListCollectionView _conversations;

        // 오른쪽에 열어 둔 대화. 새 메시지가 오면 맨 아래로 내려 주려고 들고 있는다.
        private ChatTabItem? _openTab;

        public ChatPage()
        {
            InitializeComponent();
            DataContext = _ctx;

            // 원본 목록은 건드리지 않고 보기만 거르고 줄 세운다.
            _conversations = (ListCollectionView)CollectionViewSource.GetDefaultView(_ctx.Chat.Tabs);
            _conversations.Filter = MatchesFilter;
            _conversations.SortDescriptions.Clear();
            _conversations.SortDescriptions.Add(new SortDescription(nameof(ChatTabItem.HasUnread), ListSortDirection.Descending));
            _conversations.SortDescriptions.Add(new SortDescription(nameof(ChatTabItem.LastTimestamp), ListSortDirection.Descending));

            // 새 메시지가 오거나 읽음 상태가 바뀌면 줄 순서와 필터를 바로 다시 맞춘다.
            _conversations.IsLiveSorting = true;
            _conversations.LiveSortingProperties.Clear();
            _conversations.LiveSortingProperties.Add(nameof(ChatTabItem.HasUnread));
            _conversations.LiveSortingProperties.Add(nameof(ChatTabItem.LastTimestamp));
            _conversations.IsLiveFiltering = true;
            _conversations.LiveFilteringProperties.Clear();
            _conversations.LiveFilteringProperties.Add(nameof(ChatTabItem.HasUnread));

            ConversationList.ItemsSource = _conversations;
            ((INotifyCollectionChanged)_conversations).CollectionChanged += OnConversationsChanged;
            UpdateConversationEmpty();
        }

        private bool MatchesFilter(object item)
        {
            // 전체 공지 탭(SessionId 없음)은 학생 대화가 아니다.
            if (item is not ChatTabItem tab || tab.SessionId == null) return false;

            if (FilterUnread?.IsChecked == true && !tab.HasUnread) return false;
            if (FilterRead?.IsChecked == true && tab.HasUnread) return false;

            string keyword = SearchBox?.Text?.Trim() ?? string.Empty;
            if (keyword.Length > 0 &&
                !tab.StudentName.Contains(keyword) &&
                !tab.StudentId.Contains(keyword))
                return false;

            return true;
        }

        // 필터 버튼은 화면을 만드는 도중에도 한 번 눌린다(전체가 기본 선택). 그때는 목록이 아직 없다.
        private void Filter_Changed(object sender, RoutedEventArgs e)
        {
            _conversations?.Refresh();
            UpdateConversationEmpty();
        }

        private void OnConversationsChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateConversationEmpty();

        private void UpdateConversationEmpty()
        {
            if (_conversations == null || ConversationEmpty == null) return;
            ConversationEmpty.Visibility = _conversations.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        }

        // ── 1:1 대화 ──
        private void OpenConversation_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not ChatTabItem tab) return;

            DetachConversation();
            _openTab = tab;
            _ctx.Chat.OpenConversation(tab);

            ConversationTitle.Text = $"{tab.StudentName} ({tab.StudentId}) 학생과의 대화";
            MessageList.ItemsSource = tab.Messages;
            tab.Messages.CollectionChanged += OnOpenMessagesChanged;

            ComposeCard.Visibility = Visibility.Collapsed;
            NoticePanel.Visibility = Visibility.Collapsed;
            ConversationPanel.Visibility = Visibility.Visible;
            ScrollConversationToEnd();
        }

        private void CloseConversation_Click(object sender, RoutedEventArgs e) => CloseConversation();

        private void CloseConversation()
        {
            DetachConversation();
            _ctx.Chat.CloseConversation();

            ConversationPanel.Visibility = Visibility.Collapsed;
            NoticePanel.Visibility = Visibility.Visible;
        }

        private void DetachConversation()
        {
            if (_openTab != null) _openTab.Messages.CollectionChanged -= OnOpenMessagesChanged;
            _openTab = null;
        }

        private void OnOpenMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e) => ScrollConversationToEnd();

        // 새 메시지가 붙고 배치가 끝난 뒤라야 끝까지 내려갈 수 있다.
        private void ScrollConversationToEnd()
            => Dispatcher.BeginInvoke(new Action(() => ConversationScroll.ScrollToEnd()), DispatcherPriority.Loaded);

        // ── 전체 공지 ──
        private void Compose_Click(object sender, RoutedEventArgs e)
        {
            // 공지 칸은 공지 내역 위에 열린다. 대화를 보고 있었다면 닫고 돌아온다.
            if (ConversationPanel.Visibility == Visibility.Visible) CloseConversation();

            ComposeCard.Visibility = Visibility.Visible;
            NoticeBox.Focus();
        }

        private void CancelCompose_Click(object sender, RoutedEventArgs e)
            => ComposeCard.Visibility = Visibility.Collapsed;

        private void SendNotice_Click(object sender, RoutedEventArgs e)
        {
            if (_ctx.Chat.SendNotice()) ComposeCard.Visibility = Visibility.Collapsed;
        }

        // 화면을 떠나면 대화도 닫는다. 열린 채로 두면 그 학생의 새 메시지가 안 읽음으로 잡히지 않는다.
        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            CloseConversation();
            ((INotifyCollectionChanged)_conversations).CollectionChanged -= OnConversationsChanged;
        }
    }
}
