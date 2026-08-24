using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using NetworkLib;
using ProfessorUI.Service;

namespace ProfessorUI.ViewModel
{
    public class ChatViewModel : RightPanelViewMondel
    {
        public string Title => "채팅창";

        private ObservableCollection<ChatTabItem> _tabs;
        private ChatTabItem? _selectedTab;
        private string _inputMessage = string.Empty;

        public ObservableCollection<ChatTabItem> Tabs
        {
            get => _tabs;
            set { _tabs = value; OnPropertyChanged(); }
        }

        public ChatTabItem? SelectedTab
        {
            get => _selectedTab;
            set 
            { 
                _selectedTab = value; 
                if (_selectedTab != null)
                {
                    _selectedTab.UnreadCount = 0; // 탭을 선택하면 안 읽음 뱃지 초기화
                }
                OnPropertyChanged(); 
            }
        }

        public string InputMessage
        {
            get => _inputMessage;
            set { _inputMessage = value; OnPropertyChanged(); }
        }

        public ICommand SendMessageCommand { get; }

        public ChatViewModel()
        {
            _tabs = new ObservableCollection<ChatTabItem>();
            
            // 기본 전체 공지 탭 추가
            var globalTab = new ChatTabItem { TabName = "전체 공지 (Global)", SessionId = null };
            _tabs.Add(globalTab);
            SelectedTab = globalTab;

            SendMessageCommand = new RelayCommand(o => SendMessage());

            // 네트워크 수신 이벤트 구독
            NetworkService.Instance.PacketReceived += OnPacketReceived;
        }

        private void SendMessage()
        {
            if (string.IsNullOrWhiteSpace(InputMessage) || SelectedTab == null) return;

            string msgToSend = InputMessage;
            InputMessage = string.Empty; // 보낸 후 지우기

            // UI에 내 메시지 추가
            var myMsg = new ChatMessageModel
            {
                SenderName = "나(교수)",
                Message = msgToSend,
                Timestamp = DateTime.Now,
                IsMine = true
            };
            SelectedTab.Messages.Add(myMsg);

            // 실제 네트워크 전송
            if (SelectedTab.SessionId == null) // 전체 공지
            {
                NetworkService.Instance.BroadcastChat(msgToSend);
            }
            else // 특정 학생 1:1
            {
                NetworkService.Instance.SendChatToSession(SelectedTab.SessionId, msgToSend);
            }
        }

        private void OnPacketReceived(string sessionId, string studentId, string studentName, PacketType type, IntPtr payload, uint payloadLen)
        {
            if (type == PacketType.ChatFromStudent)
            {
                string message = Marshal.PtrToStringUTF8(payload) ?? "";
                
                // UI 스레드에서 처리
                Application.Current.Dispatcher.Invoke(() =>
                {
                    // 해당 학생의 탭이 있는지 확인
                    var tab = Tabs.FirstOrDefault(t => t.SessionId == sessionId);
                    if (tab == null)
                    {
                        // 없으면 새 탭 생성
                        tab = new ChatTabItem { TabName = $"{studentName}({studentId})", SessionId = sessionId };
                        Tabs.Add(tab);
                    }

                    // 메시지 추가
                    tab.Messages.Add(new ChatMessageModel
                    {
                        SenderName = studentName,
                        Message = message,
                        Timestamp = DateTime.Now,
                        IsMine = false
                    });

                    // 선택된 탭이 아니라면 안 읽음 뱃지 증가
                    if (SelectedTab != tab)
                    {
                        tab.UnreadCount++;
                    }
                });
            }
        }
        
        // 메모리 누수 방지를 위해 이벤트 구독 해제 (현재는 MainViewModel에서 싱글턴으로 관리될 예정이므로 안 불릴 수도 있음)
        public void Cleanup()
        {
            NetworkService.Instance.PacketReceived -= OnPacketReceived;
        }
    }
}