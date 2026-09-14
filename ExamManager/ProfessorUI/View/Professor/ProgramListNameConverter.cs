using System;
using System.Globalization;
using System.Windows.Data;
using ProfessorUI.Service;
using ExamManager.Shared;

namespace ProfessorUI.View.Professor
{
    // 감시 목록 한 줄을 교수가 알아볼 이름으로 바꾼다.
    //
    // 목록에는 실행 파일 이름("Code.exe")이 저장되지만, 교수는 그게 뭔지 모른다.
    // 사전(KnownPrograms)에 있는 것은 사람이 부르는 이름("Visual Studio Code")으로 보여 준다.
    // 제품명 키워드("제품명:ChatGPT")·서명 게시자("서명:OpenAI")는 표시를 떼고 뒷부분만 보여 준다.
    // 저장값은 그대로라 삭제 등은 원래 값으로 동작한다.
    public class ProgramListNameConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not string entry) return value;

            if (entry.StartsWith(ProgramControlStore.ProductKeywordPrefix, StringComparison.Ordinal))
                return entry.Substring(ProgramControlStore.ProductKeywordPrefix.Length);
            if (entry.StartsWith(ProgramControlStore.SignaturePrefix, StringComparison.Ordinal))
                return entry.Substring(ProgramControlStore.SignaturePrefix.Length);

            return KnownPrograms.DisplayNameFor(entry) ?? entry;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
