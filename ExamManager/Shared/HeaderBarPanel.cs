using System;
using System.Windows;
using System.Windows.Controls;

namespace ExamManager.Shared
{
    // 두 앱 상단바 한 줄의 배치: 왼쪽 제목 · 가운데 중요 공지 · 오른쪽 메뉴.
    // 자식을 이 순서대로 세 개 넣는다.
    //
    // 공지는 상단바 한가운데에 둔다. 오른쪽 메뉴가 왼쪽 제목보다 훨씬 길어서
    // 둘 사이 빈칸의 가운데에 두면 창 가운데보다 한참 왼쪽에 놓인다.
    // 공지가 길어 한가운데서는 메뉴와 겹치면, 겹치지 않는 데까지만 왼쪽으로 민다.
    // 빈칸보다 길면 빈칸만큼만 주고 넘치는 글자는 공지 쪽에서 … 으로 줄인다.
    public class HeaderBarPanel : Panel
    {
        // 공지와 양옆 사이에 남기는 틈
        private const double Gap = 24;

        protected override Size MeasureOverride(Size availableSize)
        {
            UIElement left = InternalChildren[0], center = InternalChildren[1], right = InternalChildren[2];

            // 양옆은 제 크기대로 둔다. 공지는 그 사이에 남는 폭 안에서 잰다.
            var unlimited = new Size(double.PositiveInfinity, availableSize.Height);
            left.Measure(unlimited);
            right.Measure(unlimited);
            double room = Math.Max(0, availableSize.Width - left.DesiredSize.Width - right.DesiredSize.Width - 2 * Gap);
            center.Measure(new Size(room, availableSize.Height));

            return new Size(
                left.DesiredSize.Width + center.DesiredSize.Width + right.DesiredSize.Width + 2 * Gap,
                Math.Max(left.DesiredSize.Height, Math.Max(center.DesiredSize.Height, right.DesiredSize.Height)));
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            UIElement left = InternalChildren[0], center = InternalChildren[1], right = InternalChildren[2];

            double leftWidth = left.DesiredSize.Width;
            double rightWidth = right.DesiredSize.Width;
            left.Arrange(new Rect(0, 0, leftWidth, finalSize.Height));
            right.Arrange(new Rect(finalSize.Width - rightWidth, 0, rightWidth, finalSize.Height));

            // 공지가 놓일 수 있는 구간 [from, to]. 한가운데를 노리되 이 구간 밖으로는 나가지 않는다.
            double from = leftWidth + Gap;
            double to = Math.Max(from, finalSize.Width - rightWidth - Gap);
            double width = Math.Min(center.DesiredSize.Width, to - from);
            double x = Math.Clamp((finalSize.Width - width) / 2, from, to - width);
            center.Arrange(new Rect(x, 0, width, finalSize.Height));

            return finalSize;
        }
    }
}
