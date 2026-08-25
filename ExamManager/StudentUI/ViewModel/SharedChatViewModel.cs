using NetworkLib;
using StudentUI.Model;
using StudentUI.Service;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;

namespace StudentUI.ViewModel
{
    public class SharedChatViewModel : INotifyPropertyChanged
    {
        public static SharedChatViewModel Instance { get; } = new SharedChatViewModel();

        private ObservableCollection<ChatMessageModel> _messages;
        public ObservableCollection<ChatMessageModel> Messages
        {
            get => _messages;
            set { _messages = value; OnPropertyChanged(); }
        }

        private string _inputMessage = string.Empty;
        public string InputMessage
        {
            get => _inputMessage;
            set { _inputMessage = value; OnPropertyChanged(); }
        }

        public ICommand SendMessageCommand { get; }

        private SharedChatViewModel()
        {
            _messages = new ObservableCollection<ChatMessageModel>();
            SendMessageCommand = new RelayCommand(SendMessage);

            // 네트워크 수신 이벤트 구독
            NetworkService.Instance.PacketReceived += OnPacketReceived;
        }

        private void SendMessage()
        {
            if (string.IsNullOrWhiteSpace(InputMessage)) return;

            string msgToSend = InputMessage;
            InputMessage = string.Empty; // 비우기

            // UI에 내 메시지 추가
            var myMsg = new ChatMessageModel
            {
                SenderName = "나",
                Message = msgToSend,
                Timestamp = DateTime.Now,
                IsMine = true
            };
            Messages.Add(myMsg);

            // 네트워크 전송 (StudentClient는 SendChat 메서드가 있으므로 이를 사용)
            NetworkService.Instance.SendChat(msgToSend);
        }

        private void OnPacketReceived(PacketType type, IntPtr payload, uint payloadLen)
        {
            if (type == PacketType.ChatBroadcast || type == PacketType.ChatDirect)
            {
                // 페이로드 길이로 읽기를 제한한다 (종료 문자가 없는 패킷이 와도 버퍼 밖을 읽지 않도록)
                if (payload == IntPtr.Zero || payloadLen == 0) return;
                string message = (Marshal.PtrToStringUTF8(payload, (int)payloadLen) ?? "").Split('\0')[0];
                string senderName = (type == PacketType.ChatBroadcast) ? "[전체 공지]" : "[교수님]";

                // 콜백은 네이티브 스레드에서 올라오므로 UI 스레드로 넘겨 처리한다.
                // 동기 Invoke는 종료 중 수신 스레드를 붙잡아 앱이 멈추므로 BeginInvoke를 쓴다.
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.HasShutdownStarted) return;

                dispatcher.BeginInvoke(() =>
                {
                    Messages.Add(new ChatMessageModel
                    {
                        SenderName = senderName,
                        Message = message,
                        Timestamp = DateTime.Now,
                        IsMine = false
                    });
                });
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
