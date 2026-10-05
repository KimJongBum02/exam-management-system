using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace ProfessorUI.Converter
{
    // 끝난 일에는 파란 글씨를 입힌다.
    //
    // 수집 상태·전송 상태·압축 상태가 모두 한 칸에 글자로만 나와서,
    // '완료'와 '미수집'이 같은 검정이면 표를 훑을 때 어디까지 끝났는지 한눈에 들어오지 않는다.
    // 색은 학생 프로그램이 쓰는 파랑과 같은 값이라 두 화면을 나란히 놓아도 따로 놀지 않는다.
    //
    // 칸마다 값이 달라지므로(같은 열에 '완료'와 '미수집'이 섞인다) 스타일 트리거가 아니라
    // 글자를 보고 고르는 방식으로 둔다.
    public class DoneTextConverter : IValueConverter
    {
        private static readonly SolidColorBrush Done = Freeze(Color.FromRgb(0x1D, 0x4E, 0xD8));

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // 끝나지 않은 글자는 손대지 않는다. UnsetValue 를 돌려주면 원래 색이 그대로 쓰인다.
            if (value is not string text || !text.Contains("완료")) return DependencyProperty.UnsetValue;
            return Done;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();

        private static SolidColorBrush Freeze(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}
