using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ExamManager.Shared;

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

        // 이 표시로 시작하는 금지 항목은 디지털 서명 게시자다. 파일 이름·원본 이름을 바꿔도
        // 서명은 위조할 수 없어 게시자로 잡힌다. 학생 PC 의 ProcessMonitor 가 같은 글자로 알아본다.
        public const string SignaturePrefix = "서명:";

        // 기본으로 채우는 금지 항목 — 생성형 AI 의 실행 파일 이름과 제품명 키워드.
        // 화면이 기본 항목과 교수가 직접 넣은 항목을 나눠 보여 줄 때도 이 목록으로 가른다.
        //
        // 같은 AI 가 두 줄씩 들어가는 것은 의도한 것이다. 실행 파일 이름("ChatGPT.exe")은 버전 정보가
        // 없는 파일도 잡고, 제품명 키워드("제품명:ChatGPT")는 이름을 바꾸거나 철자가 다른 실행 파일도 잡는다.
        public static IReadOnlyList<string> DefaultBlackList { get; } =
            KnownPrograms.Ai.Select(program => program.ExecutableName)
                .Concat(KnownPrograms.AiProductKeywords.Select(keyword => ProductKeywordPrefix + keyword))
                .ToList();

        public static bool IsDefaultBlack(string entry) => DefaultBlackList.Contains(entry);

        // 기본값을 다시 채운다. 이미 들어 있는 것은 건드리지 않는다.
        public static void LoadDefaults()
        {
            foreach (string entry in DefaultBlackList)
                AddToBlackList(entry);

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

        // 기본 제공 금지 항목 중 지워진 것을 다시 채운다. 이미 있는 것은 건드리지 않는다.
        // 교수가 실수로 지운 AI 기본값을 손으로 다시 타이핑하지 않아도 되게 한다.
        // 프로그램 선택창에서 고른 것들을 목록에 넣는다.
        // 반대쪽 목록에 이미 있는 것은 넣지 않고 돌려준다 — 조용히 무시하면
        // 아무 일도 일어나지 않은 것처럼 보이기 때문이다.
        public static List<string> AddPicked(IEnumerable<string> executables, bool toWhiteList)
        {
            var other = toWhiteList ? BlackList : WhiteList;
            var conflicts = new List<string>();

            foreach (string exe in executables)
            {
                if (other.Contains(exe)) { conflicts.Add(exe); continue; }

                if (toWhiteList) AddToWhiteList(exe);
                else AddToBlackList(exe);
            }

            return conflicts;
        }

        public static void RestoreBlackListDefaults()
        {
            foreach (string entry in DefaultBlackList)
                AddToBlackList(entry);
        }

        // 전체 삭제 메서드
        public static void ClearBlackList() => BlackList.Clear();
        public static void ClearWhiteList() => WhiteList.Clear();
    }
}