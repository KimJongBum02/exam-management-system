using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using NetworkLib;
using ProfessorUI.Service;
using ProfessorUI.ViewModel;

namespace ProfessorUI.View.Professor
{
    // 우선 대응 항목 한 줄
    public class PriorityItem
    {
        public string Title { get; init; } = string.Empty;
        public string Detail { get; init; } = string.Empty;
        public string Badge { get; init; } = string.Empty;
    }

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
        public ObservableCollection<PriorityItem> Priorities { get; } = new();

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
        public string PhaseText { get; private set; } = "시험 준비 전";
        public string PhaseSubText { get; private set; } = string.Empty;
        public string ConnectedText => $"{ConnectedCount} / {TotalCount}";
        public string CollectedText => $"{CollectedCount} / {TotalCount}";
        public string LastUpdatedText { get; private set; } = "-";

        // 화면에 그대로 넣을 수 있게 단위까지 붙여 둔다. 변환기를 두지 않기 위함이다.
        public string NotConnectedText => $"미접속 {NotConnectedCount}명";
        public string AlertText => $"{AlertCount}건";
        public string AlertStudentText => $"미확인 {UnreadAlertCount}건 · 학생 {AlertStudentCount}명";
        public string FileMissingText => $"파일 미수신 {FileMissingCount}명";
        public string CollectedSubText => ExamState.IsExamStarted ? $"미수집 {NotCollectedCount}명" : "시험 진행 전";
        public string ApproveTargetText => $"흔적 삭제 및 PC 종료 승인 ({CollectedCount}명)";
        public string ApprovedText => $"{ApprovedCount} / {CollectedCount}명";
        public string EndSummaryText => $"승인 완료 {ApprovedCount}명 · 미수집 {NotCollectedCount}명 · 정리 실패 {CleanupFailedCount}명";

        // 같은 학생이 여러 번 걸릴 수 있으므로 사람 수로 따로 센다
        public int AlertStudentCount { get; private set; }

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
            AlertStudentCount = Alerts.Select(a => a.StudentId).Distinct().Count();

            CollectedCount = SubmittedCount;
            NotCollectedCount = TotalCount - SubmittedCount;
            CleanupFailedCount = Students.Count(s => s.IsCleanupFailed);
            ApprovedCount = Students.Count(s => s.IsApproved);
            CompletionRate = TotalCount == 0 ? "0%" : $"{CollectedCount * 100 / TotalCount}%";

            UpdatePhaseText();
            LastUpdatedText = DateTime.Now.ToString("HH:mm:ss");
            BuildPriorities();

            // 이름을 하나씩 적기보다 전체를 다시 읽게 하는 편이 셈이 늘어나도 어긋나지 않는다
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        }

        private void UpdatePhaseText()
        {
            switch (ExamState.CurrentPhase)
            {
                case ExamPhase.InProgress:
                    PhaseText = "시험 진행 중";
                    PhaseSubText = $"제출 완료 {SubmittedCount}명";
                    break;
                case ExamPhase.SubmitRequested:
                case ExamPhase.Closed:
                    PhaseText = "시험 종료";
                    PhaseSubText = $"답안 수집 {CollectedCount} / {TotalCount}";
                    break;
                default:
                    if (FileDeployState.IsFileDistributed)
                    {
                        PhaseText = "파일 배포 완료";
                        PhaseSubText = "3단계 / 4단계 — 시험 시작 대기 중";
                    }
                    else if (FileDeployState.IsFilePrepared)
                    {
                        PhaseText = "파일 준비 완료";
                        PhaseSubText = "1단계 / 4단계 — 학생 접속 대기 중";
                    }
                    else
                    {
                        PhaseText = "시험 준비 전";
                        PhaseSubText = "1단계 / 4단계 — 시험 파일을 준비하십시오";
                    }
                    break;
            }
        }

        // 교수가 먼저 봐야 할 것만 추린다. 아무 문제가 없으면 비워 둔다.
        private void BuildPriorities()
        {
            Priorities.Clear();

            var offline = Students.Where(s => !s.IsConnected).ToList();
            if (offline.Count > 0)
                Priorities.Add(new PriorityItem
                {
                    Title = "미접속 학생",
                    Detail = Describe(offline),
                    Badge = "미접속"
                });

            foreach (var alert in Alerts.Take(3))
                Priorities.Add(new PriorityItem
                {
                    Title = "부정행위 경고",
                    Detail = $"{alert.Who} · {alert.Description}",
                    Badge = "경고"
                });

            var missing = Students.Where(s => s.IsConnected && !s.IsFileReceived).ToList();
            if (missing.Count > 0)
                Priorities.Add(new PriorityItem
                {
                    Title = "파일 미수신 학생",
                    Detail = Describe(missing) + " · 재배포 필요",
                    Badge = "주의"
                });
        }

        // "20210042 김지수 외 3명" 처럼 앞사람만 보여 준다
        private static string Describe(System.Collections.Generic.List<StudentItemViewModel> list)
        {
            var head = $"{list[0].StudentId} {list[0].Name}";
            return list.Count == 1 ? head : $"{head} 외 {list.Count - 1}명";
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
