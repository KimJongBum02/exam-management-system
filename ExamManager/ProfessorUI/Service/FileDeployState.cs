using System;
using System.Collections.Generic;
using System.Text;

namespace ProfessorUI.Service
{
    // ⭐ 두 뷰모델이 공통으로 바라보는 상태 저장소
    public static class FileDeployState
    {
        // 파일 준비(압축/암호화)가 완료되었는지 여부
        public static bool IsFilePrepared { get; set; } = false;
        
        // 압축 완료된 패키지 정보 (배포 단계가 읽어감)
        public static string? ExamId { get; set; }
        public static string? PackagePath { get; set; }
        public static string? Password { get; set; }

        private static bool _isFileDistributed = false;
        public static bool IsFileDistributed
        {
            get => _isFileDistributed;
            set
            {
                _isFileDistributed = value;
                // 값이 바뀔 때마다 이벤트를 발생시켜 다른 화면들에 알림
                StateChanged?.Invoke();
            }
        }

        // 상태가 변했을 때 뷰모델들에게 알려줄 이벤트
        public static event Action StateChanged;
    }
}

