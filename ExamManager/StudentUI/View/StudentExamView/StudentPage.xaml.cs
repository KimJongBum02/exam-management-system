using StudentUI.Model;
using StudentUI.ViewModel;
using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace StudentUI.View.StudentExamView
{
    /// <summary>
    /// StudentPage.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class StudentPage : Page
    {
        public StudentPage(Student student)
        {
            InitializeComponent();
            var vm = new StudentExamViewModel(student);
            DataContext = vm;
        }
    }
}
