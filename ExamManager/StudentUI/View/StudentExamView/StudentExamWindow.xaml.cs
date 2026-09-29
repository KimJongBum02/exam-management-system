using System;
//using System.Collections.Generic;
//using System.Text;
using System.ComponentModel;
using System.Windows;
//using System.Windows.Controls;
//using System.Windows.Data;
//using System.Windows.Documents;
//using System.Windows.Input;
//using System.Windows.Media;
//using System.Windows.Media.Imaging;
//using System.Windows.Navigation;
//using System.Windows.Shapes;
using ExamManager.Shared;
using StudentUI.ViewModel;

namespace StudentUI.View.StudentExamView
{
    public partial class StudentExamWindow : Window
    {
        public StudentExamWindow()
        {
            InitializeComponent();

            // 최대화로 시작하고, 복원하면 교수 화면과 같은 크기로 화면 가운데에 온다.
            UiWindow.ApplyMainWindowSize(this);

            // 교수의 채팅·공지가 오면 채팅 버튼을 깜빡인다.
            SharedChatViewModel.Instance.MessageArrived += OnMessageArrived;
            Closed += (_, _) => SharedChatViewModel.Instance.MessageArrived -= OnMessageArrived;

            // 창 크기가 바뀔 때(최대화·복원·끌어서 줄이기) 알림·채팅 패널을 놓는 방식을 다시 정한다.
            SizeChanged += (_, _) => PlaceSideDrawer();

            // 채팅 패널을 열면 바로 칠 수 있게 입력칸에 커서를 둔다.
            DataContextChanged += (_, e) =>
            {
                if (e.OldValue is INotifyPropertyChanged oldVm) oldVm.PropertyChanged -= OnViewModelChanged;
                if (e.NewValue is INotifyPropertyChanged newVm) newVm.PropertyChanged += OnViewModelChanged;
            };
            Closed += (_, _) =>
            {
                if (DataContext is INotifyPropertyChanged vm) vm.PropertyChanged -= OnViewModelChanged;
            };
        }

        // 패널이 보이게 된 뒤라야 입력칸이 포커스를 받는다.
        private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(StudentExamViewModel.IsChatOpen) && sender is StudentExamViewModel { IsChatOpen: true })
                Dispatcher.BeginInvoke(new Action(() => ChatInput.Focus()), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        // 알림·채팅 패널이 본문을 밀어도 본문이 이 폭은 남아야 표가 제 모양을 유지하고 세로 스크롤도 생기지 않는다.
        // 이보다 좁으면 표의 설명 칸이 눌려 줄이 늘어나고, 1440 화면에서는 그만큼 본문이 길어져 스크롤이 생긴다.
        private const double MinMainWidthWithDrawer = 1400;

        private static readonly System.Windows.Media.Effects.DropShadowEffect DrawerShadow = new()
        {
            BlurRadius = 24, ShadowDepth = 0, Opacity = 0.18, Color = System.Windows.Media.Colors.Black,
        };

        // 창이 넓으면 패널을 본문 옆에 둔다(본문과 함께 보인다).
        // 좁으면 본문 위에 겹쳐 띄운다. 본문 폭이 그대로라 표 모양이 흐트러지지 않고, 닫으면 가린 부분이 다시 보인다.
        private void PlaceSideDrawer()
        {
            // 창 테두리를 뺀 안쪽 폭으로 따진다
            double inner = (Content as FrameworkElement)?.ActualWidth ?? ActualWidth;
            bool overlay = inner - SideDrawer.Width < MinMainWidthWithDrawer;

            System.Windows.Controls.Grid.SetColumn(SideDrawer, overlay ? 0 : 1);
            SideDrawer.HorizontalAlignment = overlay ? HorizontalAlignment.Right : HorizontalAlignment.Stretch;
            System.Windows.Controls.Panel.SetZIndex(SideDrawer, overlay ? 1 : 0);
            SideDrawer.Effect = overlay ? DrawerShadow : null;
        }

        // 채팅 창을 열어 두고 있으면 이미 보이므로 깜빡이지 않는다.
        private void OnMessageArrived()
        {
            if (DataContext is StudentExamViewModel { IsChatOpen: true }) return;
            UiSignal.Blink(ChatButton, BackgroundProperty, UiSignal.MessageSoftColor, times: 4);
        }

        // 화면 전환에 의한 닫힘이면 true — 이때는 종료 확인창을 띄우지 않는다.
        public bool IsNavigating { get; set; }

        // 창을 닫으면 앱이 그대로 종료되고 수신한 시험 파일 상태도 사라지므로,
        // 사용자가 직접 닫을 때만(전환이 아닐 때) 실수 방지 확인을 한 번 한다.
        protected override void OnClosing(CancelEventArgs e)
        {
            if (IsNavigating)
            {
                base.OnClosing(e);
                return;
            }

            var answer = MessageBox.Show(
                "시험 화면을 닫으면 프로그램이 종료됩니다.\n정말 종료하시겠습니까?",
                "종료 확인", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (answer != MessageBoxResult.Yes)
                e.Cancel = true;

            base.OnClosing(e);
        }
    }
}
