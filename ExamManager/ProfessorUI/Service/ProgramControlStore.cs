using System.Collections.ObjectModel;

namespace ProfessorUI.Service
{
    public static class ProgramControlStore
    {
        public static ObservableCollection<string> BlackList { get; } = new();
        public static ObservableCollection<string> WhiteList { get; } = new();

        // 프로그램이 뜰 때 자주 쓰이는 것들을 미리 채워 둔다.
        //
        // 빈 목록으로 시작하면 교수가 매번 같은 것을 손으로 넣어야 하고,
        // 바쁘면 그냥 비운 채 시험을 시작하게 된다. 기본값이 있으면
        // 적어도 생성형 AI 는 막힌 상태로 출발한다.
        //
        // 채워 둘 뿐 고정하지 않는다. 교수가 지우면 그대로 지워지고,
        // [전체 삭제] 도 평소처럼 동작한다.
        static ProgramControlStore()
        {
            LoadDefaults();
        }

        // 이 표시로 시작하는 금지 항목은 실행 파일 이름이 아니라 제품명·파일 설명에서 찾는다.
        // 학생 PC 의 ProcessMonitor 가 같은 글자로 알아보므로 한쪽만 바꾸면 안 된다.
        public const string ProductKeywordPrefix = "제품명:";

        // 기본값을 다시 채운다. 이미 들어 있는 것은 건드리지 않는다.
        public static void LoadDefaults()
        {
            foreach (var program in KnownPrograms.Ai)
                AddToBlackList(program.ExecutableName);

            // 실행 파일 이름의 철자를 몰라도 걸리도록 제품명으로도 막는다.
            foreach (string keyword in KnownPrograms.AiProductKeywords)
                AddToBlackList(ProductKeywordPrefix + keyword);

            // 허용은 실행 파일 이름과 원래 이름이 모두 목록에 있어야 인정된다.
            // 하나만 넣으면 학생 PC 에서 허용으로 잡히지 않아 '목록에 없는 프로그램'
            // 알림이 뜬다. 두 이름이 같으면 두 번째 호출은 그냥 무시된다.
            foreach (var program in KnownPrograms.DevTools)
            {
                AddToWhiteList(program.ExecutableName);
                AddToWhiteList(program.EffectiveOriginalName);
            }
        }

        public static bool AddToBlackList(string processName)
        {
            if (string.IsNullOrWhiteSpace(processName)) return false;
            string cleanName = processName.Trim();

            if (WhiteList.Contains(cleanName)) return false;
            if (!BlackList.Contains(cleanName))
            {
                BlackList.Add(cleanName);
                return true;
            }
            return false;
        }

        public static bool AddToWhiteList(string processName)
        {
            if (string.IsNullOrWhiteSpace(processName)) return false;
            string cleanName = processName.Trim();

            if (BlackList.Contains(cleanName)) return false;
            if (!WhiteList.Contains(cleanName))
            {
                WhiteList.Add(cleanName);
                return true;
            }
            return false;
        }

        // 전체 삭제 메서드
        public static void ClearBlackList() => BlackList.Clear();
        public static void ClearWhiteList() => WhiteList.Clear();
    }
}