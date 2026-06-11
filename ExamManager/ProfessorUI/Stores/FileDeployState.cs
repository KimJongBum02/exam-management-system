using System;
using System.Collections.Generic;
using System.Text;

namespace ProfessorUI.Stores
{
    // ⭐ 두 뷰모델이 공통으로 바라보는 상태 저장소
    public static class FileDeployState
    {
        // 파일 준비(압축/암호화)가 완료되었는지 여부
        public static bool IsFilePrepared { get; set; } = false;
    }
}
