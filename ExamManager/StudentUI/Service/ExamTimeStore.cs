using NetworkLib;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;

namespace StudentUI.Service
{
    // 시험 남은 시간을 앱 전체에서 공유한다.
    //
    // 교수가 '시험 시작'을 누른 순간(ExamPhaseChange(InProgress) 수신)부터 세기 시작한다.
    // 화면이 바뀌어도 시간이 초기화되면 안 되므로 화면이 아니라 여기에 둔다.
    //
    // 시작 시각을 교수가 보내 주는 대신 학생이 신호를 받은 시각으로 잡는다.
    // 강의실 PC마다 시계가 맞다는 보장이 없어, 교수가 보낸 시각을 그대로 믿으면
    // 시계가 틀어진 PC에서 엉뚱한 시간이 나온다. 신호는 전원에게 거의 동시에
    // 도착하므로 학생 간 차이는 무시할 수 있다.
    public class ExamTimeStore : INotifyPropertyChanged
    {
        public static ExamTimeStore Instance { get; } = new ExamTimeStore();

        // 시험 시간. 바뀌면 이 값만 고치면 된다.
        public static readonly TimeSpan ExamDuration = TimeSpan.FromMinutes(50);

        private readonly DispatcherTimer _ticker;
        private DateTime _startedAt;

        private ExamTimeStore()
        {
            _ticker = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _ticker.Tick += (s, e) => Tick();
        }

        // 앱 시작 시 한 번 호출 — 구독만 해둔다.
        public void Start()
        {
            NetworkService.Instance.PacketReceived += OnPacketReceived;

            // 학생이 스스로 답안을 내고 나가는 경우, 교수의 종료 신호가 오지 않는다.
            // 그래도 그 학생의 시험은 끝난 것이므로 타이머를 멈춘다.
            AnswerSubmitService.Instance.StateChanged += (state, _) =>
            {
                if (state != AnswerSubmitState.Succeeded) return;

                // 제출은 배경 작업에서 끝난다. 타이머는 화면 스레드에서만 다룰 수 있다.
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.HasShutdownStarted) return;
                dispatcher.BeginInvoke(Finish);
            };
        }

        // ── 바인딩용 상태 ──
        private bool _isRunning;
        public bool IsRunning
        {
            get => _isRunning;
            private set { _isRunning = value; OnPropertyChanged(); }
        }

        private TimeSpan _remaining = ExamDuration;
        public TimeSpan Remaining
        {
            get => _remaining;
            private set
            {
                _remaining = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RemainingText));
                OnPropertyChanged(nameof(IsTimeUp));
            }
        }

        // "00 : 49 : 58" 형태. 화면의 기존 표기와 맞춘다.
        public string RemainingText =>
            $"{(int)_remaining.TotalHours:00} : {_remaining.Minutes:00} : {_remaining.Seconds:00}";

        // 시간이 다 됐는지. 다 돼도 답안을 자동 제출하지는 않는다 —
        // 학생이 마지막으로 저장하는 중일 수 있고, 교수가 시간을 더 주는 경우도 있다.
        // 종료는 교수가 '시험 종료' 버튼으로 결정한다.
        public bool IsTimeUp => IsRunning && _remaining <= TimeSpan.Zero;

        // 시험이 끝났는지. 멈춘 것과 끝난 것을 구분해야 문구가 "대기 중"으로 되돌아가지 않는다.
        private bool _isFinished;
        public bool IsFinished
        {
            get => _isFinished;
            private set { _isFinished = value; OnPropertyChanged(); }
        }

        public string StatusText =>
            IsFinished ? "시험 종료" :
            IsTimeUp   ? "시험 시간 종료" :
            IsRunning  ? "시험 진행 중" : "시험 대기 중";

        // ── 교수 신호 처리 (네이티브 스레드에서 호출됨) ──
        private void OnPacketReceived(PacketType type, IntPtr payload, uint payloadLen)
        {
            if (type != PacketType.ExamPhaseChange) return;
            if (!ExamPhasePayload.TryDecode(payload, payloadLen, out ExamPhase phase)) return;

            // 타이머는 UI 스레드에서만 다룰 수 있다.
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted) return;

            dispatcher.BeginInvoke(() =>
            {
                if (phase == ExamPhase.InProgress) Begin();
                else if (phase >= ExamPhase.SubmitRequested) Finish();
            });
        }

        private void Begin()
        {
            // 같은 신호가 두 번 와도 시간을 되돌리지 않는다.
            if (IsRunning) return;

            _startedAt = DateTime.UtcNow;
            IsFinished = false;
            IsRunning = true;
            Remaining = ExamDuration;
            _ticker.Start();
            OnPropertyChanged(nameof(StatusText));
        }

        // 시험이 끝났다. 교수의 종료 신호나 학생 본인의 제출 완료로 불린다.
        private void Finish()
        {
            _ticker.Stop();
            IsRunning = false;
            IsFinished = true;
            OnPropertyChanged(nameof(StatusText));
        }

        private void Tick()
        {
            TimeSpan left = ExamDuration - (DateTime.UtcNow - _startedAt);

            if (left <= TimeSpan.Zero)
            {
                // 0에서 멈춘다. 음수로 내려가면 화면에 이상한 값이 찍힌다.
                Remaining = TimeSpan.Zero;
                _ticker.Stop();
                OnPropertyChanged(nameof(StatusText));
                return;
            }

            Remaining = left;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
