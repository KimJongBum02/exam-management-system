using System;
using System.Collections.ObjectModel;

namespace ProfessorUI.Service
{
    // 경고 종류. 상세 화면의 안내 문구가 이것으로 갈린다.
    public enum AlertKind
    {
        Cheating,   // 금지 프로그램 실행 등. 학생 PC 가 이미 막았다
        Reference,  // 허용 프로그램 종료. 참고용이라 부정행위로 표시하지 않는다
        Security,   // 감시·차단이 걸리지 않았거나 답안을 내지 않고 끊김. 교수가 자리에서 확인해야 한다
    }

    // 알림 한 건. 화면에 그대로 바인딩된다.
    public class AlertItem : System.ComponentModel.INotifyPropertyChanged
    {
        public string Time { get; init; } = string.Empty;
        public string StudentId { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public AlertKind Kind { get; init; }

        // 상세 화면에서 교수에게 보여 줄 안내
        public string Guide => Kind switch
        {
            AlertKind.Reference => "학생이 허용 프로그램을 닫은 기록입니다. 작업을 마치고 닫았을 수 있어 부정행위로 표시하지 않았습니다.",
            AlertKind.Security => "학생 PC의 감시가 걸려 있지 않을 수 있습니다. 학생 프로그램이 꺼지면 인터넷 차단도 함께 풀립니다. 자리에서 직접 확인해 주세요.",
            _ => "차단은 학생 PC에서 이미 이루어졌습니다. 이 화면에서는 확인 여부만 기록합니다.",
        };

        // "202407021 김종범" — 목록에서 한 줄로 보여줄 때 쓴다.
        public string Who => $"{StudentId} {Name}";

        // 교수가 확인했는지. 상단바 배지는 확인하지 않은 것만 센다.
        private bool _isAcknowledged;
        public bool IsAcknowledged
        {
            get => _isAcknowledged;
            set { _isAcknowledged = value; OnPropertyChanged(nameof(IsAcknowledged)); OnPropertyChanged(nameof(StateText)); }
        }

        // 교수가 남긴 처리 메모
        private string _note = string.Empty;
        public string Note
        {
            get => _note;
            set { _note = value; OnPropertyChanged(nameof(Note)); }
        }

        public string StateText => _isAcknowledged ? "확인 완료" : "미확인";

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name)
            => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
    }

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
