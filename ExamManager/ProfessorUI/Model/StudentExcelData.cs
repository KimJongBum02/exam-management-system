// 수강생 명단 데이터: 명단 한 줄(RosterEntry)
namespace ProfessorUI.Model
{
    // 학교 포털에서 받은 수강생 명단 엑셀의 한 줄. 학번·이름 열만 담는다.
    public sealed record RosterEntry(string StudentId, string Name);
}
