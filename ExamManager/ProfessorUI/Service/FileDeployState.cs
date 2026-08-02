using System;

namespace ProfessorUI.Service
{
    public static class FileDeployState
    {
        private static bool _isFilePrepared = false;

        // ⭐ 파일 준비 여부 (값이 바뀌면 StateChanged 이벤트 발생!)
        public static bool IsFilePrepared
        {
            get => _isFilePrepared;
            set
            {
                if (_isFilePrepared != value)
                {
                    _isFilePrepared = value;
                    StateChanged?.Invoke(); // 뷰모델들에 변경 알림
                }
            }
        }

        public static string? ExamId { get; set; }
        public static string? PackagePath { get; set; }
        public static string? Password { get; set; }

        private static bool _isFileDistributed = false;
        public static bool IsFileDistributed
        {
            get => _isFileDistributed;
            set
            {
                if (_isFileDistributed != value)
                {
                    _isFileDistributed = value;
                    StateChanged?.Invoke();
                }
            }
        }

        public static event Action? StateChanged;
    }
}