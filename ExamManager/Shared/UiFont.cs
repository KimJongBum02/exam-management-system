using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

// 앱의 모든 창이 같은 글꼴로 그려지도록 한 곳에서 정하는 파일입니다. 두 앱이 함께 씁니다.

namespace ExamManager.Shared
{
    public static class UiFont
    {
        // 글꼴을 모든 창에 거는 일을 담당합니다. 앱 시작 때 창을 하나라도 만들기 전에 한 번만 부릅니다.
        // 글꼴 자체는 각 앱 App.xaml 의 AppFont 가 정합니다.
        public static void Apply(FontFamily font)
        {
            // 창에 직접 건 값은 안의 글자 · 버튼이 모두 물려받습니다.
            // 기본값만 바꾸는 방법(OverrideMetadata)은 안쪽에 물려주지 않아 쓸 수 없습니다.
            // 좌표 반올림은 배율 100% 화면에서 글자가 두 픽셀에 걸쳐 번지지 않게 합니다.
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
                new RoutedEventHandler((sender, _) =>
                {
                    var window = (Window)sender;
                    window.FontFamily = font;
                    window.UseLayoutRounding = true;
                }));

            // 툴팁은 창과 따로 떠서 창의 글꼴을 물려받지 않으므로 스타일로 덮습니다.
            var toolTip = new Style(typeof(ToolTip));
            toolTip.Setters.Add(new Setter(Control.FontFamilyProperty, font));
            Application.Current.Resources[typeof(ToolTip)] = toolTip;
        }
    }
}
