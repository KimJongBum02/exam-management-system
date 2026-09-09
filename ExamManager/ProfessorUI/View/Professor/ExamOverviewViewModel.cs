using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using ProfessorUI.Service;
using ProfessorUI.ViewModel;

namespace ProfessorUI.View.Professor
{
    // 대시보드 · 시험 관리 창 · 종료 및 정산이 함께 쓰는 집계.
    //
    // 세 화면이 같은 숫자를 각자 세면 서로 어긋나기 쉬워서 한곳에서만 센다.
    // 학생 목록이 바뀌거나 학생 한 명의 상태가 바뀔 때마다 다시 센다.
    public class ExamOverviewViewModel : INotifyPropertyChanged
    {
        public ExamOverviewViewModel()
        {
            Students = StudentStore.Instance.Students;
            Alerts = AlertStore.Instance.Alerts;

            Students.CollectionChanged += OnStudentsChanged;
            foreach (var s in Students) s.PropertyChanged += OnStudentChanged;
            Alerts.CollectionChanged += (_, _) => Recount();

            ExamState.StateChanged += Recount;
            FileDeployState.StateChanged += Recount;

            Recount();
        }

        public ObservableCollection<StudentItemViewModel> Students { get; }
        public ObservableCollection<AlertItem> Alerts { get; }

        // ── 접속 · 진행 ──
        public int TotalCount { get; private set; }
        public int ConnectedCount { get; private set; }
        public int NotConnectedCount { get; private set; }
        public int InProgressCount { get; private set; }
        public int SubmittedCount { get; private set; }
        public int FileMissingCount { get; private set; }
        public int FileReceivedCount { get; private set; }

        // ── 경고 ──
        // 학생 PC 에서 네트워크 감시가 실제로 켜진 인원
        public int NetworkMonitorOnCount { get; private set; }
        public string NetworkMonitorText => !ExamState.IsExamStarted
                                          ? "시험 시작 시 자동 적용"
                                          : $"{NetworkMonitorOnCount} / {ConnectedCount}명 적용";

        public int AlertCount { get; private set; }
        public int UnreadAlertCount { get; private set; }

        // ── 수집 · 정산 ──
        public int CollectedCount { get; private set; }
        public int NotCollectedCount { get; private set; }
        public int CleanupFailedCount { get; private set; }
        public int ApprovedCount { get; private set; }
        public string CompletionRate { get; private set; } = "0%";

        // ── 화면에 그대로 나갈 문구 ──
        // 단위까지 붙여 둔다. 변환기를 두지 않기 위함이다.
        public string ConnectedText => $"{ConnectedCount} / {TotalCount}";
        public string CollectedText => $"{CollectedCount} / {TotalCount}";
        public string LastUpdatedText { get; private set; } = "-";
        public string ApproveTargetText => $"흔적 삭제 및 PC 종료 승인 ({CollectedCount}명)";
        public string ApprovedText => $"{ApprovedCount} / {CollectedCount}명";
        public string EndSummaryText => $"승인 완료 {ApprovedCount}명 · 미수집 {NotCollectedCount}명 · 정리 실패 {CleanupFailedCount}명";

        private void OnStudentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
                foreach (StudentItemViewModel s in e.OldItems) s.PropertyChanged -= OnStudentChanged;
            if (e.NewItems != null)
                foreach (StudentItemViewModel s in e.NewItems) s.PropertyChanged += OnStudentChanged;

            Recount();
        }

        private void OnStudentChanged(object? sender, PropertyChangedEventArgs e) => Recount();

        private void Recount()
        {
            TotalCount = Students.Count;
            ConnectedCount = Students.Count(s => s.IsConnected);
            NotConnectedCount = TotalCount - ConnectedCount;
            SubmittedCount = Students.Count(s => s.IsAnswerSubmitted);
            FileReceivedCount = Students.Count(s => s.IsFileReceived);

            // 파일을 못 받은 채 접속해 있는 학생 — 재배포 대상이다
            FileMissingCount = Students.Count(s => s.IsConnected && !s.IsFileReceived);

            // 파일을 받고 아직 제출하지 않은 채 접속해 있으면 시험을 보고 있는 것이다
            InProgressCount = Students.Count(s => s.IsConnected && s.IsFileReceived && !s.IsAnswerSubmitted);

            NetworkMonitorOnCount = Students.Count(s => s.IsConnected && s.NetworkMonitorOn);

            AlertCount = Alerts.Count;
            UnreadAlertCount = Alerts.Count(a => !a.IsAcknowledged);

            CollectedCount = SubmittedCount;
            NotCollectedCount = TotalCount - SubmittedCount;
            CleanupFailedCount = Students.Count(s => s.IsCleanupFailed);
            ApprovedCount = Students.Count(s => s.IsApproved);
            CompletionRate = TotalCount == 0 ? "0%" : $"{CollectedCount * 100 / TotalCount}%";

            LastUpdatedText = DateTime.Now.ToString("HH:mm:ss");

            // 이름을 하나씩 적기보다 전체를 다시 읽게 하는 편이 셈이 늘어나도 어긋나지 않는다
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
