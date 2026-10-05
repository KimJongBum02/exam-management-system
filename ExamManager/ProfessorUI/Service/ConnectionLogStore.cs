using System;
using System.Collections.ObjectModel;
using ProfessorUI.Model;

namespace ProfessorUI.Service
{
    // 학생이 접속한 사건을 쌓아 둔다.
    //
    // 학생 목록(StudentStore)은 한 학생에 한 줄이라, 다시 접속하면 앞의 접속 시각이 덮인다.
    // 누가 몇 번 끊겼다 들어왔는지는 그 목록으로 알 수 없으므로 사건을 그대로 남기는 곳을 따로 둔다.
    //
    // 화면은 메뉴를 옮길 때마다 새로 만들어지므로 목록을 화면이 아니라 여기에 둔다.
    public class ConnectionLogStore
    {
        public static ConnectionLogStore Instance { get; } = new ConnectionLogStore();

        // 최근 것이 위로 오도록 앞에 넣는다. 방금 누가 들어왔는지가 가장 급하다.
        public ObservableCollection<ConnectionLogEntry> Entries { get; } = new ObservableCollection<ConnectionLogEntry>();

        private ConnectionLogStore() { }

        public void Record(string studentId, string name, string ip, bool isFirst)
        {
            Entries.Insert(0, new ConnectionLogEntry
            {
                Time = DateTime.Now,
                StudentId = studentId,
                Name = name,
                Ip = ip,
                IsFirst = isFirst,
            });
        }

        public void Clear() => Entries.Clear();
    }
}
