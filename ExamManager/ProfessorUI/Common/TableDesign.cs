using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ProfessorUI.Common
{
    // 표 모양을 맞추는 도우미. 목록 표(FlatTable)와 퀴즈 결과 표가 쓴다.
    //   ClipRadius — 안쪽 내용을 둥근 모서리로 잘라 낸다. 머리글 회색 띠가 둥근 테두리 밖으로 삐져나오지 않게 한다.
    //   FitColumns — 칸 폭을 표 폭에 맞춰 늘이고 줄인다. XAML 에 적은 폭은 칸끼리의 비율로만 쓴다.
    //                고정 폭이면 표가 좁을 때 오른쪽 칸이 밀려 잘리고, 넓을 때는 오른쪽이 빈다.
    public static class TableDesign
    {
        public static readonly DependencyProperty ClipRadiusProperty = DependencyProperty.RegisterAttached(
            "ClipRadius", typeof(double), typeof(TableDesign), new PropertyMetadata(0.0, OnClipRadiusChanged));

        public static double GetClipRadius(DependencyObject d) => (double)d.GetValue(ClipRadiusProperty);
        public static void SetClipRadius(DependencyObject d, double value) => d.SetValue(ClipRadiusProperty, value);

        private static void OnClipRadiusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not FrameworkElement element) return;
            element.SizeChanged -= OnClipTargetSizeChanged;
            element.SizeChanged += OnClipTargetSizeChanged;
        }

        private static void OnClipTargetSizeChanged(object sender, SizeChangedEventArgs e)
        {
            var element = (FrameworkElement)sender;
            double radius = GetClipRadius(element);
            element.Clip = new RectangleGeometry(new Rect(e.NewSize), radius, radius);
        }

        public static readonly DependencyProperty FitColumnsProperty = DependencyProperty.RegisterAttached(
            "FitColumns", typeof(bool), typeof(TableDesign), new PropertyMetadata(false, OnFitColumnsChanged));

        public static bool GetFitColumns(DependencyObject d) => (bool)d.GetValue(FitColumnsProperty);
        public static void SetFitColumns(DependencyObject d, bool value) => d.SetValue(FitColumnsProperty, value);

        // 칸마다 XAML 에 적힌 폭. 비율의 기준이라 처음 한 번만 기억한다.
        private static readonly DependencyProperty BaseWidthProperty = DependencyProperty.RegisterAttached(
            "BaseWidth", typeof(double), typeof(TableDesign), new PropertyMetadata(double.NaN));

        private static void OnFitColumnsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not ListView list || e.NewValue is not true) return;

            list.SizeChanged += (_, _) => Fit(list);
            // 줄이 늘어 세로 스크롤 막대가 생기면 보이는 폭이 그만큼 줄어든다
            list.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, args) =>
            {
                if (args.ViewportWidthChange != 0) Fit(list);
            }));
        }

        private static void Fit(ListView list)
        {
            if (list.View is not GridView grid || grid.Columns.Count == 0) return;

            // WPF 기본 틀은 머리글 줄에만 좌우 2px 여백을 준다. 그대로 두면 회색 머리 띠가 테두리에서 떠 있고,
            // 칸을 보이는 폭에 맞춰도 머리글만 2px 밀려 끝이 넘친다.
            if (FirstChild<GridViewHeaderRowPresenter>(list) is { } header && header.Margin != default)
                header.Margin = default;

            double available = FirstChild<ScrollViewer>(list)?.ViewportWidth ?? 0;
            if (available <= 0) return;

            foreach (var column in grid.Columns)
                if (double.IsNaN((double)column.GetValue(BaseWidthProperty)))
                    column.SetValue(BaseWidthProperty, double.IsNaN(column.Width) ? column.ActualWidth : column.Width);

            double total = grid.Columns.Sum(c => (double)c.GetValue(BaseWidthProperty));
            if (total <= 0) return;

            // 정수 폭으로 나누고 남는 자투리는 마지막 칸에 준다. 합이 보이는 폭을 넘으면 마지막 칸이 잘린다.
            double used = 0;
            for (int i = 0; i < grid.Columns.Count; i++)
            {
                var column = grid.Columns[i];
                double width = i == grid.Columns.Count - 1
                    ? Math.Floor(available - used)
                    : Math.Floor((double)column.GetValue(BaseWidthProperty) * available / total);
                if (column.Width != width) column.Width = width;
                used += width;
            }
        }

        private static T? FirstChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T match) return match;
                if (FirstChild<T>(child) is T found) return found;
            }
            return null;
        }
    }
}
