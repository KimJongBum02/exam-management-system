using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

// 상태가 바뀌었음을 눈에 띄게 알리는 일을 담당하는 파일입니다. 두 앱이 함께 씁니다.

namespace ExamManager.Shared
{
    // 화면 색을 줄이면서 경고 · 채팅 · 접속 변화가 거의 티 나지 않게 되어,
    // 바뀌는 순간에만 잠깐 깜빡이고 창이 뒤에 있으면 작업표시줄 아이콘을 깜빡입니다.
    public static class UiSignal
    {
        // 깜빡일 때만 쓰는 색입니다. 흰 칸 · 패널 위에 올리는 옅은 색입니다.
        public static readonly Color AlertSoftColor   = Color.FromRgb(0xFE, 0xE2, 0xE2);   // 부정행위 경고
        public static readonly Color MessageSoftColor = Color.FromRgb(0xDB, 0xEA, 0xFE);   // 채팅·공지
        public static readonly Color GoodSoftColor    = Color.FromRgb(0xDC, 0xFC, 0xE7);   // 접속

        // 작업표시줄 아이콘을 깜빡이는 일을 담당합니다. 창이 뒤에 있거나 최소화돼 있을 때만 깜빡입니다.
        // 창을 앞으로 가져오면 Windows 가 알아서 멈춥니다. 어느 스레드에서 불러도 됩니다.
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

        // 요소의 브러시 속성(Background 등)을 몇 번 깜빡였다가 되돌리는 일을 담당합니다.
        // 깜빡이는 중에 또 불리면 처음부터 다시 깜빡입니다. 화면 스레드에서 불러야 합니다.
        public static void Blink(FrameworkElement element, DependencyProperty brushProperty, Color color, int times = 3)
        {
            if (element.GetValue(BlinkStateProperty) is not BlinkState state)
            {
                // 원래 값을 기억해 둡니다. 스타일이 준 값이면 나중에 지우기만 하면 스타일 값으로 돌아갑니다.
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
                // 그사이 새 깜빡임이 시작됐으면 그쪽이 끝날 때 정리합니다
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

        // FLASHW_ALL 은 제목 표시줄과 작업표시줄 둘 다, FLASHW_TIMERNOFG 는 창이 앞으로 올 때까지입니다.
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
