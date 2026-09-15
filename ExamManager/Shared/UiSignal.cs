using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ExamManager.Shared
{
    // 상태가 바뀌었음을 눈에 띄게 알린다. 교수·학생 프로그램이 함께 쓴다.
    //
    // 화면 색을 줄이면서 경고·채팅·접속 변화가 거의 티 나지 않게 됐다.
    // 그래서 바뀌는 순간에만 잠깐 깜빡이고, 창이 뒤에 있으면 작업표시줄 아이콘을 깜빡인다.
    public static class UiSignal
    {
        // 평소 화면은 무채색이라 깜빡일 때만 색을 쓴다.
        // 진한 색은 검은 버튼 위에, 옅은 색은 흰 칸·패널 위에 쓴다.
        public static readonly Color AlertColor       = Color.FromRgb(0xDC, 0x26, 0x26);   // 부정행위 경고
        public static readonly Color AlertSoftColor   = Color.FromRgb(0xFE, 0xE2, 0xE2);
        public static readonly Color MessageColor     = Color.FromRgb(0x25, 0x63, 0xEB);   // 채팅·공지
        public static readonly Color MessageSoftColor = Color.FromRgb(0xDB, 0xEA, 0xFE);
        public static readonly Color GoodSoftColor    = Color.FromRgb(0xDC, 0xFC, 0xE7);   // 접속

        // 창이 뒤에 있거나 최소화돼 있으면 작업표시줄 아이콘을 깜빡인다.
        // 창을 앞으로 가져오면 Windows 가 알아서 멈춘다. 이미 보고 있으면 아무것도 하지 않는다.
        // 어느 스레드에서 불러도 된다.
        public static void FlashTaskbar()
        {
            var app = Application.Current;
            if (app == null) return;

            app.Dispatcher.BeginInvoke(new Action(() =>
            {
                var windows = app.Windows.OfType<Window>().ToList();
                if (windows.Any(w => w.IsActive)) return;

                Window? target = windows.FirstOrDefault(w => w.IsVisible && w.Owner == null);
                if (target == null) return;

                var info = new FlashInfo
                {
                    Size = (uint)Marshal.SizeOf<FlashInfo>(),
                    Window = new WindowInteropHelper(target).Handle,
                    Flags = FlashAll | FlashUntilForeground,
                    Count = uint.MaxValue,
                };
                FlashWindowEx(ref info);
            }));
        }

        // 요소의 브러시 속성(Background 등)을 color 로 몇 번 깜빡였다가 원래대로 돌린다.
        // 깜빡이는 중에 또 불리면 처음부터 다시 깜빡인다. 화면 스레드에서 불러야 한다.
        public static void Blink(FrameworkElement element, DependencyProperty brushProperty, Color color, int times = 3)
        {
            if (element.GetValue(BlinkStateProperty) is not BlinkState state)
            {
                // 원래 값을 기억해 둔다. 스타일이 준 값이면 나중에 지우기만 하면 스타일 값으로 돌아간다.
                object local = element.ReadLocalValue(brushProperty);
                Color from = (element.GetValue(brushProperty) as SolidColorBrush)?.Color ?? Colors.Transparent;

                state = new BlinkState(new SolidColorBrush(from), local);
                element.SetValue(BlinkStateProperty, state);
                element.SetValue(brushProperty, state.Brush);
            }

            int version = ++state.Version;
            var animation = new ColorAnimation(color, TimeSpan.FromMilliseconds(260))
            {
                AutoReverse = true,
                RepeatBehavior = new RepeatBehavior(times),
            };
            animation.Completed += (_, _) =>
            {
                // 그사이 새 깜빡임이 시작됐으면 그쪽이 끝날 때 정리한다
                if (state.Version != version) return;

                state.Brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
                if (state.Local == DependencyProperty.UnsetValue) element.ClearValue(brushProperty);
                else element.SetValue(brushProperty, state.Local);
                element.ClearValue(BlinkStateProperty);
            };
            state.Brush.BeginAnimation(SolidColorBrush.ColorProperty, animation);
        }

        private sealed class BlinkState
        {
            public BlinkState(SolidColorBrush brush, object local) { Brush = brush; Local = local; }
            public SolidColorBrush Brush { get; }
            public object Local { get; }
            public int Version { get; set; }
        }

        private static readonly DependencyProperty BlinkStateProperty =
            DependencyProperty.RegisterAttached("BlinkState", typeof(object), typeof(UiSignal));

        // FLASHW_ALL: 제목 표시줄과 작업표시줄 둘 다 / FLASHW_TIMERNOFG: 창이 앞으로 올 때까지
        private const uint FlashAll = 0x3;
        private const uint FlashUntilForeground = 0xC;

        [StructLayout(LayoutKind.Sequential)]
        private struct FlashInfo
        {
            public uint Size;
            public IntPtr Window;
            public uint Flags;
            public uint Count;
            public uint Timeout;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FlashWindowEx(ref FlashInfo info);
    }
}
