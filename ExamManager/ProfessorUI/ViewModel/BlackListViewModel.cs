using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using ProfessorUI.Service;

namespace ProfessorUI.ViewModel
{
    public class BlackListViewModel : INotifyPropertyChanged
    {
        private string _inputProcessName = string.Empty;
        private bool _isProcessPickerOpen = false;
        private string _searchQuery = string.Empty;

        private List<ProcessDisplayItem> _allProcesses = new();
        public ObservableCollection<ProcessDisplayItem> FilteredProcesses { get; } = new();

        public ObservableCollection<string> BlackList => ProgramControlStore.BlackList;

        public string InputProcessName
        {
            get => _inputProcessName;
            set { _inputProcessName = value; OnPropertyChanged(); }
        }

        public bool IsProcessPickerOpen
        {
            get => _isProcessPickerOpen;
            set { _isProcessPickerOpen = value; OnPropertyChanged(); }
        }

        public string SearchQuery
        {
            get => _searchQuery;
            set
            {
                _searchQuery = value;
                OnPropertyChanged();
                FilterProcesses();
            }
        }

        public ICommand AddCommand { get; }
        public ICommand RemoveCommand { get; }
        public ICommand ClearAllCommand { get; } // 전체 삭제 커맨드
        public ICommand OpenPickerCommand { get; }
        public ICommand ClosePickerCommand { get; }
        public ICommand ConfirmPickerCommand { get; }

        public BlackListViewModel()
        {
            AddCommand = new RelayCommand(_ => AddProcess());
            RemoveCommand = new RelayCommand(param => RemoveProcess(param as string));
            ClearAllCommand = new RelayCommand(_ => ClearAll());

            OpenPickerCommand = new RelayCommand(_ => OpenPicker());
            ClosePickerCommand = new RelayCommand(_ => IsProcessPickerOpen = false);
            ConfirmPickerCommand = new RelayCommand(param => ConfirmSelection(param as IList));
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

            var result = MessageBox.Show("등록된 모든 블랙리스트 항목을 삭제하시겠습니까?", "전체 삭제 확인",
                                         MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                // Store 수정 없이 ObservableCollection을 직접 비웁니다.
                BlackList.Clear();
            }
        }

        private void OpenPicker()
        {
            _allProcesses = Process.GetProcesses()
                .Where(p => !string.IsNullOrWhiteSpace(p.ProcessName))
                .Select(p => new ProcessDisplayItem
                {
                    ProcessName = p.ProcessName,
                    MainWindowTitle = p.MainWindowTitle
                })
                .GroupBy(p => p.ProcessName, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(p => p.ProcessName)
                .ToList();

            SearchQuery = string.Empty;
            FilterProcesses();
            IsProcessPickerOpen = true;
        }

        private void FilterProcesses()
        {
            FilteredProcesses.Clear();
            var filtered = string.IsNullOrWhiteSpace(SearchQuery)
                ? _allProcesses
                : _allProcesses.Where(p => p.ProcessName.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) ||
                                           p.MainWindowTitle.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase));

            foreach (var item in filtered)
                FilteredProcesses.Add(item);
        }

        private void ConfirmSelection(IList? selectedItems)
        {
            if (selectedItems != null)
            {
                var items = selectedItems.Cast<ProcessDisplayItem>().ToList();
                foreach (var item in items)
                {
                    ProgramControlStore.AddToBlackList(item.ProcessName);
                }
            }
            IsProcessPickerOpen = false;
        }


        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}