using System.Runtime.InteropServices;

namespace StudentUI.Service
{
    // FileControl.dll (C++) 의 P/Invoke 선언
    internal static class FileControlService
    {
        // .7z 복호화 + 압축 해제. 반환: 7za 종료 코드 (0 = 성공)
        [DllImport("FileControl.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        internal static extern int FC_ExtractDecrypt(
            string sevenZaPath, string archivePath, string outputFolder, string password);
    }
}
