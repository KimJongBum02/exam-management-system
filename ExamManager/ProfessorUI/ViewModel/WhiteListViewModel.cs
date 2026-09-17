using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using ProfessorUI.Service;

namespace ProfessorUI.ViewModel
{
    public class WhiteListViewModel : INotifyPropertyChanged
    {
        private string _inputProcessName = string.Empty;

        public ObservableCollection<string> WhiteList => ProgramControlStore.WhiteList;

        public string InputProcessName
        {
            get => _inputProcessName;
            set { _inputProcessName = value; OnPropertyChanged(); }
        }

        public ICommand AddCommand { get; }
        public ICommand RemoveCommand { get; }
        public ICommand ClearAllCommand { get; } // 전체 삭제 커맨드


        public WhiteListViewModel()
        {
            AddCommand = new RelayCommand(_ => AddProcess());
            RemoveCommand = new RelayCommand(param => RemoveProcess(param as string));
            ClearAllCommand = new RelayCommand(_ => ClearAll());
        }



        private void AddProcess()
        {
            if (ProgramControlStore.AddToWhiteList(InputProcessName))
                InputProcessName = string.Empty;
        }

        private void RemoveProcess(string? processName)
        {
            if (!string.IsNullOrEmpty(processName))
                WhiteList.Remove(processName);
        }

        private void ClearAll()
        {
            if (WhiteList.Count == 0) return;

            var result = MessageBox.Show("허용 프로그램 목록을 모두 비우시겠습니까?", "전체 삭제 확인",
                                         MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                // Store 수정 없이 ObservableCollection을 직접 비웁니다.
                WhiteList.Clear();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}