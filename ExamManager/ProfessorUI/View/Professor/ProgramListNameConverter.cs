using System;
using System.Globalization;
using System.Windows.Data;
using ProfessorUI.Service;

namespace ProfessorUI.View.Professor
{
    // 감시 목록 한 줄을 화면에 보일 이름으로 바꾼다.
    //
    // 제품명 키워드는 "제품명:ChatGPT" 로 저장된다. 학생 PC 의 감시가 이 표시로 키워드를 알아보기 때문이다.
    // 교수에게는 표시를 떼고 "ChatGPT" 만 보여 준다. 저장값은 그대로라 삭제 버튼 등은 원래 값으로 동작한다.
    public class ProgramListNameConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is string entry && entry.StartsWith(ProgramControlStore.ProductKeywordPrefix, StringComparison.Ordinal)
                ? entry.Substring(ProgramControlStore.ProductKeywordPrefix.Length)
                : value;

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
