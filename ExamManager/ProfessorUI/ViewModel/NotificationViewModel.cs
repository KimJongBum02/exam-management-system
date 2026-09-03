using ProfessorUI.Service;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace ProfessorUI.ViewModel
{
    public class NotificationViewModel : RightPanelViewMondel
    {
        public string Title => "알림창";

        // 목록은 저장소가 갖고 있다. 패널을 닫았다 열어도 지금까지 쌓인 알림이 남는다.
        public ObservableCollection<AlertItem> Alerts => AlertStore.Instance.Alerts;

        public bool HasAlerts => Alerts.Count > 0;

        public ICommand ClearCommand { get; }

        public NotificationViewModel()
        {
            ClearCommand = new RelayCommand(_ => AlertStore.Instance.Clear());

            // 알림이 늘거나 지워지면 '알림 없음' 문구를 켜고 끈다.
            Alerts.CollectionChanged += (s, e) => OnPropertyChanged(nameof(HasAlerts));
        }
    }
}
