using System;

namespace ProfessorUI.Service
{
    // 배포할 시험 파일의 준비/전송 상태.
    // 시험 단계 화면들이 이 값을 보고 버튼을 열고 닫는다.
    public static class FileDeployState
    {
        private static bool _isFilePrepared;
        private static bool _isFileDistributed;

        // 압축·암호화가 끝난 시험 파일이 있는지. 값은 SetPackage/Clear로만 바뀐다.
        public static bool IsFilePrepared => _isFilePrepared;

        // 한 명 이상에게 전송을 시작했는지. 시험 시작 버튼이 이 값을 본다.
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

        public static string? ExamId { get; private set; }
        public static string? PackagePath { get; private set; }
        public static string? Password { get; private set; }

        // 버튼 활성화 조건이 바뀌었음을 화면들에 알린다.
        public static event Action? StateChanged;

        // 새 시험 파일이 준비됐다. 배포 목록을 처음 상태로 되돌리는 데 쓴다.
        public static event Action? PackageChanged;

        // 시험이 완전히 끝나 모든 준비 상태가 지워졌다.
        public static event Action? Cleared;

        // 압축·암호화가 끝난 파일을 등록한다.
        //
        // 앞서 배포한 파일과는 다른 파일이므로 배포 완료 표시를 함께 지운다.
        // 그래야 한 번 배포를 끝낸 뒤에도 다른 파일을 다시 압축해 배포할 수 있다.
        public static void SetPackage(string examId, string packagePath, string password)
        {
            ExamId = examId;
            PackagePath = packagePath;
            Password = password;

            _isFilePrepared = true;
            _isFileDistributed = false;

            PackageChanged?.Invoke();
            StateChanged?.Invoke();
        }

        // 모든 준비 상태를 지워 시험 전 상태로 되돌린다.
        // 모든 학생의 승인이 끝났을 때 호출된다.
        public static void Clear()
        {
            ExamId = null;
            PackagePath = null;
            Password = null;

            _isFilePrepared = false;
            _isFileDistributed = false;

            Cleared?.Invoke();
            StateChanged?.Invoke();
        }
    }
}
