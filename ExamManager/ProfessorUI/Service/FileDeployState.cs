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
    }
}
