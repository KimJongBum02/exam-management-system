using System.Collections.ObjectModel;

namespace ProfessorUI.Service
{
    public static class ProgramControlStore
    {
        public static ObservableCollection<string> BlackList { get; } = new();
        public static ObservableCollection<string> WhiteList { get; } = new();

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