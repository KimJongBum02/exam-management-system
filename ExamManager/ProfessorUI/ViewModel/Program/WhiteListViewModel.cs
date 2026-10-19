using ExamManager.Shared;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using ProfessorUI.Service;

namespace ProfessorUI.ViewModel
{
    public class WhiteListViewModel : UiBinding
    {
        private string _inputProcessName = string.Empty;

        public ObservableCollection<string> WhiteList => ProgramControlStore.WhiteList;

        // 금지 목록과 같이 기본 항목과 교수가 직접 넣은 항목을 따로 보여 준다.
        // 저장은 한 목록(ProgramControlStore.WhiteList) 그대로이고 보여 줄 때만 가른다.
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


        public WhiteListViewModel()
        {
            AddCommand = new UiCommand(_ => AddProcess());
            RemoveCommand = new UiCommand(param => RemoveProcess(param as string));
            ClearAllCommand = new UiCommand(_ => ClearAll());

            DefaultItems = new ListCollectionView(ProgramControlStore.WhiteList)
            {
                Filter = item => item is string entry && ProgramControlStore.IsDefaultWhite(entry),
            };
            AddedItems = new ListCollectionView(ProgramControlStore.WhiteList)
            {
                Filter = item => item is string entry && !ProgramControlStore.IsDefaultWhite(entry),
            };
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
    }
}