using System.IO;

namespace ProfessorUI.ViewModel
{
    // 압축할 목록의 한 줄. 파일이면 그 파일, 폴더면 폴더 전체(하위 구조 그대로)가 묶음에 들어간다.
    public class SelectedFileViewModel
    {
        public string FullPath { get; init; } = string.Empty;
        public bool IsFolder { get; init; }
        public int FileCount { get; init; }   // 폴더면 안의 파일 수, 파일이면 1

        public string Name => Path.GetFileName(FullPath);
        public string Display => IsFolder ? $"{Name}\\  (폴더 · 파일 {FileCount}개)" : Name;

        // 어디서 가져왔는지. 여러 폴더에서 더할 수 있어 줄마다 보여 준다.
        public string Location => Path.GetDirectoryName(FullPath) ?? string.Empty;
    }
}
