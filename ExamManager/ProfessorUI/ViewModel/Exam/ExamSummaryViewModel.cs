using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using ProfessorUI.Service;
using ProfessorUI.Model;

namespace ProfessorUI.ViewModel
{
    // 대시보드 · 시험 관리 창 · 시험 종료·답안 수집이 함께 쓰는 집계.
    //
    // 세 화면이 같은 숫자를 각자 세면 서로 어긋나기 쉬워서 한곳에서만 센다.
    // 학생 목록이 바뀌거나 학생 한 명의 상태가 바뀔 때마다 다시 센다.
    public class ExamSummaryViewModel : INotifyPropertyChanged
    {
        public ExamSummaryViewModel()
        {
            Students = StudentStore.Instance.Students;
            Alerts = AlertStore.Instance.Alerts;

            Students.CollectionChanged += OnStudentsChanged;
            foreach (var s in Students) s.PropertyChanged += OnStudentChanged;
            // 경고를 확인하면(IsAcknowledged) 목록은 그대로라 항목 변화도 따로 듣는다. 그래야 미확인 수가 바로 준다.
            Alerts.CollectionChanged += OnAlertsChanged;
            foreach (var a in Alerts) a.PropertyChanged += OnAlertChanged;

            ExamState.StateChanged += Recount;
            SendFileState.StateChanged += Recount;
            // 수강생 명단을 불러오면 '전체' 인원이 명단 기준으로 바뀐다
            StudentExcelStore.Changed += Recount;

            Recount();
        }

        public ObservableCollection<StudentStatusViewModel> Students { get; }
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

        // ── 수집 · 승인 ──
        public int CollectedCount { get; private set; }
        public int NotCollectedCount { get; private set; }
        public int CleanupFailedCount { get; private set; }
        public int ApprovedCount { get; private set; }
        public string CompletionRate { get; private set; } = "0%";

        // 답안을 걷었고 아직 흔적 삭제를 승인하지 않은 학생. 시험 중에 먼저 낸 학생도 여기 든다.
        // 시험 종료·답안 수집 메뉴 배지가 이 수를 보인다.
        public int ApprovalWaitingCount => CollectedCount - ApprovedCount;

        // ── 화면에 그대로 나갈 문구 ──
        // 단위까지 붙여 둔다. 변환기를 두지 않기 위함이다.
        public string ConnectedText => $"{ConnectedCount} / {TotalCount}";
        public string CollectedText => $"{CollectedCount} / {TotalCount}";
        public string LastUpdatedText { get; private set; } = "-";
        public string ApproveTargetText => $"흔적 삭제 ({CollectedCount}명)";
        public string EndSummaryText => $"승인 완료 {ApprovedCount}명 · 미수집 {NotCollectedCount}명 · 정리 실패 {CleanupFailedCount}명";

        private void OnStudentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
                foreach (StudentStatusViewModel s in e.OldItems) s.PropertyChanged -= OnStudentChanged;
            if (e.NewItems != null)
                foreach (StudentStatusViewModel s in e.NewItems) s.PropertyChanged += OnStudentChanged;

            Recount();
        }

        private void OnStudentChanged(object? sender, PropertyChangedEventArgs e) => Recount();

        private void OnAlertsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
                foreach (AlertItem a in e.OldItems) a.PropertyChanged -= OnAlertChanged;
            if (e.NewItems != null)
                foreach (AlertItem a in e.NewItems) a.PropertyChanged += OnAlertChanged;

            Recount();
        }

        private void OnAlertChanged(object? sender, PropertyChangedEventArgs e) => Recount();

        private void Recount()
        {
            // 수강생 명단을 불러왔으면 명단 학생은 접속하지 않았어도 '전체'에 들어 미접속·미수집으로 센다.
            // 명단에 없는 학번은 대시보드에 칸이 보이는 학생만 센다(StudentCardViewModel.IsActive).
            TotalCount = StudentExcelStore.HasRoster
                ? StudentExcelStore.Entries.Count +
                  Students.Count(s => StudentExcelStore.Find(s.StudentId) == null && StudentCardViewModel.IsActive(s))
                : Students.Count;
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
