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
                string message = Marshal.PtrToStringUTF8(payload) ?? "";
                string senderName = (type == PacketType.ChatBroadcast) ? "[전체 공지]" : "[교수님]";

                Application.Current.Dispatcher.Invoke(() =>
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
