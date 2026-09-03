using System;
using System.Collections.ObjectModel;

namespace ProfessorUI.Service
{
    // 알림 한 건. 화면에 그대로 바인딩된다.
    public class AlertItem
    {
        public string Time { get; init; } = string.Empty;
        public string StudentId { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;

        // "202407021 김종범" — 목록에서 한 줄로 보여줄 때 쓴다.
        public string Who => $"{StudentId} {Name}";
    }

    // 부정행위 알림을 모아 둔다.
    //
    // 알림 패널은 열고 닫을 때마다 새로 만들어지므로, 목록을 화면이 아니라 여기에 둔다.
    // 그렇지 않으면 패널을 닫는 순간 지금까지 쌓인 알림이 전부 사라진다.
    public class AlertStore
    {
        public static AlertStore Instance { get; } = new AlertStore();

        // 최근 것이 위로 오도록 앞에 넣는다. 시험 중에는 최근 알림이 중요하다.
        public ObservableCollection<AlertItem> Alerts { get; } = new ObservableCollection<AlertItem>();

        private AlertStore() { }

        public void Add(string studentId, string name, string description)
        {
            Alerts.Insert(0, new AlertItem
            {
                Time = DateTime.Now.ToString("HH:mm:ss"),
                StudentId = studentId,
                Name = name,
                Description = description,
            });
        }

        public void Clear() => Alerts.Clear();
    }
}
