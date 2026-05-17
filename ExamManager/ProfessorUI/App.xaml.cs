using System.Configuration;
using System.Data;
using System.Windows;

namespace ProfessorUI
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 1. 상태 저장소 생성 (프로그램 전체에서 딱 1개만 존재)
            Stores.NavigationStore navigationStore = new Stores.NavigationStore();

            // 처음 프로그램이 켜졌을 때 보여줄 기본 화면 설정 (예: 현황판)
            //navigationStore.CurrentViewModel = new StudentBoardViewModel();

            // 2. 메인 뷰모델 생성 (저장소와 사이드바 뷰모델을 연결)
            ViewModels.MainViewModel mainViewModel = new ViewModels.MainViewModel(navigationStore);

            // 3. 메인 창(MainWindow) 생성 후 데이터(ViewModel) 연결
            MainWindow mainWindow = new MainWindow()
            {
                DataContext = mainViewModel // MainWindow의 데이터는 MainViewModel이 담당한다!
            };

            // 화면 띄우기
            mainWindow.Show();
        }
    }

}
