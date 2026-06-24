using System.Windows.Controls;
using StudentUI.Model;
using StudentUI.ViewModel;
using StudentUI.View.LoginView;
using StudentUI.View.StudentExamView;

namespace StudentUI.View.LockScreenView
{
    public partial class LockScreenPage : Page
    {
        public LockScreenPage(Student student)
        {
            InitializeComponent();

            var vm = new LockScreenViewModel(student);
            DataContext = vm;
            
            vm.LogoutSucceeded += () =>
            {
                NavigationService.Navigate(new LoginPage());
            };

            vm.StartSuccceeded += () =>
            {
                NavigationService.Navigate(new StudentPage(vm.Student));
            };
        }
    }
}