using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace ProfessorUI.View.QuizView
{
    public class CategoryColorInfo
    {
        public Brush Background { get; set; }
        public Brush Border { get; set; }
    }

    public class CategoryToColorConverter : IValueConverter
    {
        private static readonly Dictionary<string, CategoryColorInfo> ColorMap = new Dictionary<string, CategoryColorInfo>();
        
        private static readonly CategoryColorInfo[] Palette = new CategoryColorInfo[]
        {
            new CategoryColorInfo { Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFF0F5")), Border = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFB6C1")) }, // 핑크 계열
            new CategoryColorInfo { Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F0F8FF")), Border = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#87CEFA")) }, // 블루 계열
            new CategoryColorInfo { Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F5FFFA")), Border = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#66CDAA")) }, // 그린 계열
            new CategoryColorInfo { Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFF0")), Border = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F0E68C")) }, // 옐로우 계열
            new CategoryColorInfo { Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F8F8FF")), Border = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DDA0DD")) }, // 퍼플 계열
            new CategoryColorInfo { Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFF5EE")), Border = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFA07A")) }, // 오렌지 계열
        };

        private static int _nextPaletteIndex = 0;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string category = (value as string) ?? "기본";
            if (string.IsNullOrWhiteSpace(category)) category = "기본";

            if (!ColorMap.ContainsKey(category))
            {
                if (category == "네트워크") ColorMap[category] = Palette[1];
                else if (category == "운영체제") ColorMap[category] = Palette[2];
                else if (category == "데이터베이스") ColorMap[category] = Palette[3];
                else if (category == "자료구조") ColorMap[category] = Palette[4];
                else
                {
                    ColorMap[category] = Palette[_nextPaletteIndex % Palette.Length];
                    _nextPaletteIndex++;
                }
            }
            
            var info = ColorMap[category];
            string target = parameter as string;
            
            if (target == "Border") return info.Border;
            return info.Background;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
