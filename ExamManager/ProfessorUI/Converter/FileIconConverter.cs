using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ProfessorUI.Model;

namespace ProfessorUI.Converter
{
    // 실행 파일 경로를 그 파일의 아이콘으로 바꾼다.
    //
    // 이름을 몰라도 아이콘으로 알아보는 경우가 많아 프로그램 선택창에서 함께 보여 준다.
    // 그림은 화면에서만 쓰는 것이라 데이터(ProgramEntry)에 넣지 않고 여기서 만든다.
    // System.Drawing 을 끌어오지 않으려고 셸 API 로 직접 뽑는다.
    //
    // 목록이 수백 줄이어도 화면에 보이는 줄만 변환기를 거치므로, 한 번 만든 것은 경로로 기억해 둔다.
    public class FileIconConverter : IValueConverter
    {
        private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not string path || path.Length == 0) return null;

            if (Cache.TryGetValue(path, out ImageSource? cached)) return cached;

            ImageSource? icon = LoadIcon(path);
            Cache[path] = icon;
            return icon;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();

        private static ImageSource? LoadIcon(string path)
        {
            IntPtr handle = IntPtr.Zero;
            try
            {
                var info = new SHFILEINFO();
                if (SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf(info), SHGFI_ICON | SHGFI_SMALLICON) == IntPtr.Zero)
                    return null;

                handle = info.hIcon;
                if (handle == IntPtr.Zero) return null;

                var image = Imaging.CreateBitmapSourceFromHIcon(
                    handle, System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                image.Freeze();   // 다른 스레드에서 만들어도 화면에서 쓸 수 있게 얼려 둔다
                return image;
            }
            catch { return null; }
            finally { if (handle != IntPtr.Zero) DestroyIcon(handle); }
        }

        private const uint SHGFI_ICON = 0x000000100;
        private const uint SHGFI_SMALLICON = 0x000000001;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SHGetFileInfo(string path, uint attributes,
                                                   ref SHFILEINFO info, uint size, uint flags);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);
    }
}
