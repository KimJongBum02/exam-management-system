using ExamManager.Shared;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using ProfessorUI.Service;

namespace ProfessorUI.ViewModel
{
    // 어느 목록을 다루는지. 저장소(ProgramControlStore)의 낱말을 그대로 쓴다.
    public enum ProgramListKind
    {
        White,   // 허용 목록
        Black,   // 금지 목록
    }

    // 허용·금지 목록 한 벌. 두 목록은 하는 일이 같고 어느 목록을 다루는지만 달라서 한 클래스로 둔다.
    // UiContext 가 목록마다 한 벌씩 만들고, 시험 준비 마법사와 프로그램 관리 두 화면이 그것을 함께 쓴다.
    public class ProgramControlViewModel : UiBinding
    {
        private readonly ProgramListKind _kind;
        private string _inputProcessName = string.Empty;

        public ProgramControlViewModel(ProgramListKind kind)
        {
            _kind = kind;

            AddCommand = new UiCommand(_ => AddProcess());
            RemoveCommand = new UiCommand(param => RemoveProcess(param as string));
            ClearAllCommand = new UiCommand(_ => ClearAll());

            // 기본값 복원은 저장소가 금지 목록만 지원하고, 버튼도 금지 쪽 화면에만 있다.
            // 허용 목록에서는 null 이고, WPF 는 Command 가 null 인 버튼을 스스로 잠근다.
            RestoreDefaultsCommand = kind == ProgramListKind.Black
                ? new UiCommand(_ => ProgramControlStore.RestoreBlackListDefaults())
                : null;

            DefaultItems = new ListCollectionView(Items)
            {
                Filter = item => item is string entry && IsDefault(entry),
            };
            AddedItems = new ListCollectionView(Items)
            {
                Filter = item => item is string entry && !IsDefault(entry),
            };
        }

        public ObservableCollection<string> Items => _kind == ProgramListKind.White
            ? ProgramControlStore.WhiteList
            : ProgramControlStore.BlackList;

        // 기본으로 들어 있는 항목과 교수가 직접 넣은 항목을 따로 보여 준다.
        // 저장은 한 목록(Items) 그대로이고 보여 줄 때만 가른다.
        public ICollectionView DefaultItems { get; }
        public ICollectionView AddedItems { get; }

        public string InputProcessName
        {
            get => _inputProcessName;
            set { _inputProcessName = value; OnPropertyChanged(); }
        }

        public ICommand AddCommand { get; }
        public ICommand RemoveCommand { get; }
        public ICommand ClearAllCommand { get; }          // 전체 삭제 커맨드
        public ICommand? RestoreDefaultsCommand { get; }  // 기본값 복원 커맨드 (금지 목록만)

        private bool IsDefault(string entry) => _kind == ProgramListKind.White
            ? ProgramControlStore.IsDefaultWhite(entry)
            : ProgramControlStore.IsDefaultBlack(entry);

        private void AddProcess()
        {
            bool added = _kind == ProgramListKind.White
                ? ProgramControlStore.AddToWhiteList(InputProcessName)
                : ProgramControlStore.AddToBlackList(InputProcessName);

            if (added)
                InputProcessName = string.Empty;
        }

        private void RemoveProcess(string? processName)
        {
            if (!string.IsNullOrEmpty(processName))
                Items.Remove(processName);
        }

        private void ClearAll()
        {
            if (Items.Count == 0) return;

            string message = _kind == ProgramListKind.White
                ? "허용 프로그램 목록을 모두 비우시겠습니까?"
                : "금지 프로그램 목록을 모두 비우시겠습니까?";

            var result = MessageBox.Show(message, "전체 삭제 확인",
                                         MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                // Store 수정 없이 ObservableCollection을 직접 비웁니다.
                Items.Clear();
            }
        }
    }
}
