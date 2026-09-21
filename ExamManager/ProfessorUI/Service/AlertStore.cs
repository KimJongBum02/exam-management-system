using System;
using System.Collections.ObjectModel;
using ProfessorUI.Model;

namespace ProfessorUI.Service
{
    // 부정행위 알림을 모아 둔다.
    //
    // 화면은 메뉴를 옮길 때마다 새로 만들어지므로, 목록을 화면이 아니라 여기에 둔다.
    // 그렇지 않으면 화면을 떠나는 순간 지금까지 쌓인 알림이 전부 사라진다.
    public class AlertStore
    {
        public static AlertStore Instance { get; } = new AlertStore();

        // 최근 것이 위로 오도록 앞에 넣는다. 시험 중에는 최근 알림이 중요하다.
        public ObservableCollection<AlertItem> Alerts { get; } = new ObservableCollection<AlertItem>();

        private AlertStore() { }

        public void Add(string studentId, string name, string description, AlertKind kind = AlertKind.Cheating)
        {
            Alerts.Insert(0, new AlertItem
            {
                Time = DateTime.Now.ToString("HH:mm:ss"),
                StudentId = studentId,
                Name = name,
                Description = description,
                Kind = kind,
            });
        }

        public void Clear() => Alerts.Clear();
    }
}
