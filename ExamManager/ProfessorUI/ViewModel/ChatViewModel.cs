using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ProfessorUI.ViewModel
{
    public class ChatViewModel : RightPanelViewMondel
    {
        // 예시: 채팅창 제목
        public string Title => "채팅창";

        // 나중에 여기 ObservableCollection 등을 써서 메시지 목록을 관리하게 됩니다.
        public ChatViewModel()
        {
        }
    }
}