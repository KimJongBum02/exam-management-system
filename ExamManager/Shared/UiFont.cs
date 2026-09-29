using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ExamManager.Shared
{
    // 앱의 모든 창이 같은 글꼴로 그려지도록 한 곳에서 정한다. 교수·학생 프로그램이 함께 쓴다.
    //
    // 창마다 FontFamily 를 적어 두면 새 창을 만들 때 빠뜨리기 쉽고, 빠진 창은 윈도우 기본 글꼴로 나온다.
    // 글꼴 자체는 각 앱 App.xaml 의 AppFont 가 정하고, 여기서는 그 값을 모든 창에 건다.
    //
    // 앱 시작 때 창을 하나라도 만들기 전에 한 번만 부른다.
    public static class UiFont
    {
        public static void Apply(FontFamily font)
        {
            // 창이 뜰 때 글꼴과 좌표 반올림을 창에 직접 건다. 창에 직접 건 값은 안의 글자·버튼이 모두 물려받는다.
            // 창의 기본값만 바꾸는 방법(OverrideMetadata)은 기본값을 안쪽에 물려주지 않아 쓸 수 없다.
            // 좌표 반올림은 배율 100% 화면에서 글자가 두 픽셀에 걸쳐 번지지 않도록 위치와 크기를 정수 픽셀에 맞춘다.
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
                new RoutedEventHandler((sender, _) =>
                {
                    var window = (Window)sender;
                    window.FontFamily = font;
                    window.UseLayoutRounding = true;
                }));

            // 툴팁은 창과 따로 떠서 창의 글꼴을 물려받지 않고, 윈도우 테마가 시스템 글꼴을 직접 정해 둔다.
            // 그래서 툴팁은 스타일로 덮는다.
            var toolTip = new Style(typeof(ToolTip));
            toolTip.Setters.Add(new Setter(Control.FontFamilyProperty, font));
            Application.Current.Resources[typeof(ToolTip)] = toolTip;
        }
    }
}
