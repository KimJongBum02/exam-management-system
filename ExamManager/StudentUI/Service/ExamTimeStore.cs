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

        public string StatusText => IsTimeUp ? "시험 시간 종료" : IsRunning ? "시험 진행 중" : "시험 대기 중";

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
                else if (phase >= ExamPhase.SubmitRequested) Stop();
            });
        }

        private void Begin()
        {
            // 같은 신호가 두 번 와도 시간을 되돌리지 않는다.
            if (IsRunning) return;

            _startedAt = DateTime.UtcNow;
            IsRunning = true;
            Remaining = ExamDuration;
            _ticker.Start();
            OnPropertyChanged(nameof(StatusText));
        }

        private void Stop()
        {
            _ticker.Stop();
            IsRunning = false;
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
