// 프로그램 데이터: 감시 목록 후보 하나(ProgramEntry) · 어디서 찾았는지(ProgramSource)
using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

namespace ProfessorUI.Model
{
    public enum ProgramSource
    {
        Installed,   // 시작 메뉴 바로가기에서 찾음
        Running,     // 지금 실행 중인 프로세스
        Picked,      // 교수가 파일 선택창에서 직접 고름
        Known,       // 사전에 적어 둔 이름 — 이 PC 에는 없다
        BuiltIn,     // 윈도우 기본 앱 — C:\Windows 안이라 위 경로에서는 숨기고 표로 보여 준다
        Classroom,   // 강의실 PC 에 설치된 것 — 학생 PC 가 보내 온 목록(ClassroomProgramStore)
    }

    // 감시 목록에 넣을 후보 하나.
    //
    // 교수는 "카카오톡"으로 찾고, 학생 PC의 감시는 "KakaoTalk.exe"로 한다.
    // 그래서 보여 주는 이름과 실제로 저장할 이름을 따로 들고 있는다.
    public class ProgramEntry : INotifyPropertyChanged
    {
        public string DisplayName { get; init; } = string.Empty;
        public string ExecutableName { get; init; } = string.Empty;
        public string ExecutablePath { get; init; } = string.Empty;
        public ProgramSource Source { get; set; }

        // 실행 파일에 박혀 있는 원래 이름. 파일 이름을 바꿔도 따라 변하지 않는다.
        // 학생 PC의 감시는 허용 목록을 판정할 때 이 이름까지 요구하므로
        // (허용된 이름으로 위장하는 것을 막기 위함) 함께 목록에 넣어야 한다.
        // 버전 리소스가 없는 실행 파일도 흔해서 빈 문자열이 정상 결과다.
        public string OriginalName { get; init; } = string.Empty;

        // 이 프로그램을 찾을 때 쓰일 다른 이름들("챗지피티", "VSCode" 처럼).
        // 파일에 박힌 설명과 사전의 별칭이 여기 함께 담긴다.
        public string Aliases { get; set; } = string.Empty;

        // 디지털 서명 게시자(예: "Google LLC"). 서명이 없으면 빈 문자열.
        // 직접 찾아보기로 파일을 고를 때만 읽는다. 게시자 차단 규칙("서명:")을 제안하는 데 쓴다.
        public string Publisher { get; init; } = string.Empty;

        // 실행 파일 이름과 원래 이름이 달라 목록에 두 개를 넣어야 하는 경우
        public bool HasDistinctOriginalName =>
            OriginalName.Length > 0 &&
            !string.Equals(Path.GetFileNameWithoutExtension(OriginalName),
                           Path.GetFileNameWithoutExtension(ExecutableName),
                           StringComparison.OrdinalIgnoreCase);

        public string SourceText => Source switch
        {
            ProgramSource.Running => "실행 중",
            ProgramSource.Picked => "직접 선택",
            ProgramSource.Known => "미설치",
            ProgramSource.BuiltIn => "윈도우 기본",
            ProgramSource.Classroom => "강의실 PC",
            _ => "설치됨",
        };

        // 이미 감시 목록에 들어 있는 것. 다시 고를 수 없게 하고 그렇다고 알려 준다.
        // 조용히 무시하면 눌리지 않은 것처럼 보이기 때문이다.
        public bool IsAlreadyAdded { get; set; }

        // 허용 목록을 고르는 중인데 원래 이름이 없는 경우.
        // 넣어도 감시가 허용으로 인정하지 않으므로 미리 알려 준다.
        public bool CannotBeAllowed { get; set; }

        public string StatusText =>
            IsAlreadyAdded ? "추가됨" :
            CannotBeAllowed ? "허용 불가" : string.Empty;

        // 선택창에서 고른 상태. 목록의 체크 표시가 이 값을 따라간다.
        private bool _isChosen;
        public bool IsChosen
        {
            get => _isChosen;
            set
            {
                if (_isChosen == value) return;
                _isChosen = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChosen)));
            }
        }

        // 견주기 전에 공백을 모두 없앤다.
        private static string Squeeze(string text) =>
            new string(text.Where(c => !char.IsWhiteSpace(c)).ToArray());

        public event PropertyChangedEventHandler? PropertyChanged;

        // 한글 이름과 실행 파일명 어느 쪽으로 쳐도 걸리게 한다.
        // "카카오"로도 "kakao"로도 찾을 수 있어야 하기 때문이다.
        //
        // 띄어쓰기는 무시한다. "비주얼 스튜디오"와 "비주얼스튜디오"를 다르게 볼 이유가 없고,
        // 프로그램 이름의 띄어쓰기는 사람마다 다르게 기억한다.
        public bool Matches(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword)) return true;

            string k = Squeeze(keyword);
            return Squeeze(DisplayName).Contains(k, StringComparison.OrdinalIgnoreCase)
                || Squeeze(ExecutableName).Contains(k, StringComparison.OrdinalIgnoreCase)
                || Squeeze(Aliases).Contains(k, StringComparison.OrdinalIgnoreCase);
        }
    }

    // 감시 목록에 넣을 프로그램 후보를 모은다.
    //
    // 한글 이름을 별도 표로 관리하지 않는다. 시작 메뉴 바로가기의 이름이 곧
    // 사용자가 부르는 이름이고("카카오톡" → KakaoTalk.exe), 그 안에 실행 파일 경로가
    // 들어 있어 Windows 가 이미 매핑을 갖고 있는 셈이기 때문이다.
    // 표를 두면 새 프로그램이 나올 때마다 우리가 채워 넣어야 한다.
}
