// ScreenCaptureService.cs
// 화면을 주기적으로 캡처해 교수 서버로 전송하는 서비스.
// GDI P/Invoke + WPF JpegBitmapEncoder 사용 — 외부 NuGet 패키지 없음.
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NetworkLib;

namespace StudentUI.Service
{
    public class ScreenCaptureService
    {
        // ── 싱글톤 ──────────────────────────────────────────────────────────
        public static ScreenCaptureService Instance { get; } = new();

        // ── 설정 ─────────────────────────────────────────────────────────────
        public const int NormalWidth   = 320;
        public const int NormalHeight  = 180;
        public const int NormalQuality = 60;

        public const int HighWidth   = 1920;
        public const int HighHeight  = 1080;
        public const int HighQuality = 80;

        /// <summary>캡처 주기 (초). 기본 2초.</summary>
        public double IntervalSeconds { get; set; } = 2.0;

        /// <summary>전송할 가로 픽셀. 기본 320.</summary>
        public int ThumbWidth  { get; set; } = NormalWidth;

        /// <summary>전송할 세로 픽셀. 기본 180.</summary>
        public int ThumbHeight { get; set; } = NormalHeight;

        /// <summary>JPEG 품질 (1~100). 기본 60.</summary>
        public int JpegQuality { get; set; } = NormalQuality;

        // ── 내부 ─────────────────────────────────────────────────────────────
        private DispatcherTimer? _timer;
        private bool _running;

        // 캡처 한 장이 끝나기 전에 다음 차례가 오면 건너뛴다.
        // 쌓아 두면 느린 PC 에서 캡처 작업이 밀려 늘어난다 — 그때 필요한 것은 최신 한 장뿐이다.
        private int _capturing;

        private ScreenCaptureService() { }

        // ── GDI P/Invoke ─────────────────────────────────────────────────────
        [DllImport("user32.dll")] private static extern IntPtr GetDesktopWindow();
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
        [DllImport("gdi32.dll")]  private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")]  private static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")]  private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int w, int h);
        [DllImport("gdi32.dll")]  private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
        [DllImport("gdi32.dll")]  private static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")]  private static extern bool BitBlt(
            IntPtr hdcDest, int xDest, int yDest, int w, int h,
            IntPtr hdcSrc,  int xSrc,  int ySrc,  uint rop);
        [DllImport("gdi32.dll")]  private static extern int GetDeviceCaps(IntPtr hdc, int nIndex);
        private const uint SRCCOPY        = 0x00CC0020;
        private const int  DESKTOPHORZRES = 118; // 물리적 가로 픽셀 (DPI 무관)
        private const int  DESKTOPVERTRES = 117; // 물리적 세로 픽셀 (DPI 무관)

        // ── 시작 / 정지 ───────────────────────────────────────────────────────
        /// <summary>캡처 타이머를 시작합니다. UI 스레드에서 호출하세요.</summary>
        public void Start()
        {
            if (_running) return;
            _running = true;

            NetworkService.Instance.PacketReceived += OnPacketReceived;

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(IntervalSeconds)
            };
            _timer.Tick += (_, _) => CaptureAndSendAsync();
            _timer.Start();
        }

        /// <summary>캡처 타이머를 멈춥니다.</summary>
        public void Stop()
        {
            _running = false;
            NetworkService.Instance.PacketReceived -= OnPacketReceived;
            _timer?.Stop();
            _timer = null;

            // 정지 시 기본 썸네일 해상도로 복원
            SetQualityMode(false);
        }

        private void OnPacketReceived(PacketType type, IntPtr payload, uint payloadLen)
        {
            if (type == PacketType.ScreenQualityMode && payloadLen >= 1)
            {
                byte mode = Marshal.ReadByte(payload);
                SetQualityMode(mode > 0);
            }
        }

        /// <summary>교수의 상세 화면 요청 여부에 따라 해상도 및 화질을 동적으로 전환합니다.</summary>
        public void SetQualityMode(bool highQuality)
        {
            if (highQuality)
            {
                ThumbWidth  = HighWidth;
                ThumbHeight = HighHeight;
                JpegQuality = HighQuality;
                CaptureAndSendAsync(); // 교수가 창을 열었을 때 바로 고화질 프레임 1회 즉시 전송
            }
            else
            {
                ThumbWidth  = NormalWidth;
                ThumbHeight = NormalHeight;
                JpegQuality = NormalQuality;
            }
        }

        // ── 핵심 로직 ─────────────────────────────────────────────────────────
        // 캡처·축소·JPEG 인코딩·전송을 배경 스레드에서 한다. 화면 스레드에서 하면 안 된다 —
        // 이 PC 에서 실측한 한 장 값이 썸네일 56ms · 고화질 68ms 고, 강의실 PC 는 더 느리다.
        // 시험·통제 중에는 저수준 키보드·마우스 훅이 화면 스레드에 걸려 있어서(ClassControlService)
        // 그동안 학생의 키 입력이 그만큼 밀리고, 윈도우의 훅 응답 한계(기본 300ms)를 넘기면
        // 운영체제가 훅을 조용히 떼어 버린다 — 탈출키 차단이 말없이 풀린다는 뜻이다.
        // 게다가 전송은 블로킹이라, 교수 PC 가 느릴 때 화면 스레드가 그 안에서 멈추면
        // 학생 PC 의 키보드·마우스가 함께 얼어붙는다(고화질 한 장이 약 300KB).
        //
        // 여기서 만드는 WPF 이미지 객체는 이 스레드 안에서만 쓰고 버리므로 화면과 엉키지 않는다.
        private void CaptureAndSendAsync()
        {
            if (!NetworkService.Instance.IsConnected) return;

            // 앞 장이 아직 안 끝났으면 이번 차례는 건너뛴다.
            if (Interlocked.CompareExchange(ref _capturing, 1, 0) != 0) return;

            // 값은 여기서 읽어 둔다. 배경에서 읽으면 그사이 교수가 화질을 바꿔
            // 크기와 품질이 서로 다른 조합으로 섞일 수 있다.
            int width = ThumbWidth, height = ThumbHeight, quality = JpegQuality;

            Task.Run(() =>
            {
                try
                {
                    byte[] jpeg = CaptureScreenJpeg(width, height, quality);
                    NetworkService.Instance.SendPacket(PacketType.ScreenCapture, jpeg);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ScreenCapture] 오류: {ex.Message}");
                }
                finally
                {
                    Interlocked.Exchange(ref _capturing, 0);
                }
            });
        }

        /// <summary>전체 화면을 캡처해 지정 크기로 축소한 뒤 JPEG 바이트 배열로 반환합니다.</summary>
        private byte[] CaptureScreenJpeg(int thumbW, int thumbH, int quality)
        {
            // GDI로 물리 디스플레이 DC를 직접 가져옵니다.
            // CreateDC("DISPLAY", ...) 또는 GetDC(IntPtr.Zero)를 사용하고 DESKTOPHORZRES/VERTRES로 실제 물리 해상도를 구합니다.
            IntPtr hdcScreen = GetDC(IntPtr.Zero);
            int sw = GetDeviceCaps(hdcScreen, DESKTOPHORZRES);
            int sh = GetDeviceCaps(hdcScreen, DESKTOPVERTRES);

            if (sw <= 0) sw = (int)SystemParameters.PrimaryScreenWidth;
            if (sh <= 0) sh = (int)SystemParameters.PrimaryScreenHeight;

            IntPtr hdcMem = CreateCompatibleDC(hdcScreen);
            IntPtr hBmp   = CreateCompatibleBitmap(hdcScreen, sw, sh);
            IntPtr hOld   = SelectObject(hdcMem, hBmp);

            BitBlt(hdcMem, 0, 0, sw, sh, hdcScreen, 0, 0, SRCCOPY);

            SelectObject(hdcMem, hOld);
            DeleteDC(hdcMem);
            ReleaseDC(IntPtr.Zero, hdcScreen);

            // 2. HBITMAP → BitmapSource (WPF Imaging)
            BitmapSource full;
            try
            {
                full = Imaging.CreateBitmapSourceFromHBitmap(
                    hBmp, IntPtr.Zero, Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
            }
            finally
            {
                DeleteObject(hBmp);
            }

            // 3. 썸네일 크기로 축소
            double scaleX = (double)thumbW / sw;
            double scaleY = (double)thumbH / sh;
            var scaled = new TransformedBitmap(full, new ScaleTransform(scaleX, scaleY));

            // 4. JPEG 인코딩
            var encoder = new JpegBitmapEncoder { QualityLevel = quality };
            encoder.Frames.Add(BitmapFrame.Create(scaled));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            return ms.ToArray();
        }
    }
}
