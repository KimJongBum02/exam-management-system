using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using ProfessorUI.Service;
using ProfessorUI.Common;

namespace ProfessorUI.ViewModel
{
    public class BlackListViewModel : INotifyPropertyChanged
    {
        private string _inputProcessName = string.Empty;

        public ObservableCollection<string> BlackList => ProgramControlStore.BlackList;

        // 기본으로 들어 있는 항목과 교수가 직접 넣은 항목을 따로 보여 준다.
        // 저장은 한 목록(ProgramControlStore.BlackList) 그대로이고 보여 줄 때만 가른다.
        // 시험 준비·프로그램 관리 두 화면이 이 뷰모델 하나를 함께 쓰므로 여기서 한 번만 만든다.
        public ICollectionView DefaultItems { get; }
        public ICollectionView AddedItems { get; }

        public string InputProcessName
        {
            get => _inputProcessName;
            set { _inputProcessName = value; OnPropertyChanged(); }
        }

        public ICommand AddCommand { get; }
        public ICommand RemoveCommand { get; }
        public ICommand ClearAllCommand { get; } // 전체 삭제 커맨드
        public ICommand RestoreDefaultsCommand { get; } // 기본값 복원 커맨드

        public BlackListViewModel()
        {
            AddCommand = new RelayCommand(_ => AddProcess());
            RemoveCommand = new RelayCommand(param => RemoveProcess(param as string));
            ClearAllCommand = new RelayCommand(_ => ClearAll());
            RestoreDefaultsCommand = new RelayCommand(_ => ProgramControlStore.RestoreBlackListDefaults());

            DefaultItems = new ListCollectionView(ProgramControlStore.BlackList)
            {
                Filter = item => item is string entry && ProgramControlStore.IsDefaultBlack(entry),
            };
            AddedItems = new ListCollectionView(ProgramControlStore.BlackList)
            {
                Filter = item => item is string entry && !ProgramControlStore.IsDefaultBlack(entry),
            };
        }



        private void AddProcess()
        {
            if (ProgramControlStore.AddToBlackList(InputProcessName))
                InputProcessName = string.Empty;
        }

        private void RemoveProcess(string? processName)
        {
            if (!string.IsNullOrEmpty(processName))
                BlackList.Remove(processName);
        }

        private void ClearAll()
        {
            if (BlackList.Count == 0) return;

            var result = MessageBox.Show("금지 프로그램 목록을 모두 비우시겠습니까?", "전체 삭제 확인",
                                         MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                // Store 수정 없이 ObservableCollection을 직접 비웁니다.
                BlackList.Clear();
            }
        }


        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}