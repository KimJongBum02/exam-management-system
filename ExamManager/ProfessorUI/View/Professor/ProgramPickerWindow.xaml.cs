using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using ProfessorUI.Service;

namespace ProfessorUI.View.Professor
{
    // 감시 목록에 넣을 프로그램을 이름으로 골라 주는 창.
    //
    // 교수가 "chrome.exe" 같은 실행 파일 이름을 외우고 있을 이유가 없어서 만든 화면이다.
    // 고른 결과는 SelectedExecutables 에 실행 파일 이름으로 담긴다 —
    // 학생 PC의 감시가 프로세스 이름으로 맞춰 보기 때문이다.
    public partial class ProgramPickerWindow : Window
    {
        public List<string> SelectedExecutables { get; } = new();

        // 확인을 누르기 전까지 담아 두는 것들. 창 아래에 그대로 보여 준다.
        private readonly ObservableCollection<ProgramEntry> _chosen = new();
        private ICollectionView? _view;

        // 창을 띄우고 고른 것을 허용·금지 목록에 넣는다.
        // 보안 정책 화면과 마법사 1단계가 같은 방식으로 부른다.
        public static void PickInto(DependencyObject caller, bool toWhiteList)
        {
            var picker = new ProgramPickerWindow { Owner = Window.GetWindow(caller) };
            if (picker.ShowDialog() != true) return;

            // 반대쪽 목록에 있는 것은 Store 가 조용히 거른다.
            // 아무 일도 일어나지 않은 것처럼 보이면 곤란하니 미리 추려 알려 준다.
            var other = toWhiteList ? ProgramControlStore.BlackList : ProgramControlStore.WhiteList;
            var conflicts = picker.SelectedExecutables.Where(other.Contains).ToList();

            foreach (string exe in picker.SelectedExecutables)
            {
                if (toWhiteList) ProgramControlStore.AddToWhiteList(exe);
                else ProgramControlStore.AddToBlackList(exe);
            }

            if (conflicts.Count > 0)
                MessageBox.Show(
                    $"{string.Join(", ", conflicts)} 은(는) {(toWhiteList ? "금지" : "허용")} 목록에 이미 있어 추가하지 않았습니다.\n" +
                    "먼저 그쪽에서 지운 뒤 다시 넣어 주세요.",
                    "이미 반대 목록에 있음", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        public ProgramPickerWindow()
        {
            InitializeComponent();
            ChosenList.ItemsSource = _chosen;
            UpdateChosenView();
            Loaded += async (_, _) => { await LoadAsync(false); SearchBox.Focus(); };
        }

        // ── 목록 읽기 ────────────────────────────────────────────────

        private async Task LoadAsync(bool refresh)
        {
            LoadingCover.Visibility = Visibility.Visible;

            var programs = await Task.Run(() => LoadOnStaThread(refresh));

            // 다시 읽으면 목록이 새 객체로 바뀌므로, 이미 골라 둔 것에 표시를 다시 입힌다.
            var chosenExecutables = _chosen.Select(c => c.ExecutableName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var program in programs)
                program.IsChosen = chosenExecutables.Contains(program.ExecutableName);

            ProgramList.ItemsSource = programs;
            _view = CollectionViewSource.GetDefaultView(programs);
            _view.Filter = o => o is ProgramEntry e && e.Matches(SearchBox.Text);

            if (programs.Count == 0)
            {
                // 목록을 못 만들었어도 [직접 찾아보기]라는 길이 남아 있으므로 안내만 한다.
                ((TextBlock)LoadingCover.Child).Text =
                    "프로그램을 찾지 못했습니다. [직접 찾아보기]로 골라 주세요.";
                return;
            }

            LoadingCover.Visibility = Visibility.Collapsed;
        }

        // 바로가기를 읽는 WScript.Shell 은 STA 스레드에서 부르는 것이 안전하다.
        // 그렇다고 UI 스레드에서 돌리면 목록을 훑는 동안 창이 멈춘 것처럼 보여
        // 전용 STA 스레드를 하나 띄운다.
        private static Task<List<ProgramEntry>> LoadOnStaThread(bool refresh)
        {
            var done = new TaskCompletionSource<List<ProgramEntry>>();

            var worker = new Thread(() =>
            {
                try { done.SetResult(ProgramCatalog.Load(refresh).ToList()); }
                catch (Exception) { done.SetResult(new List<ProgramEntry>()); }
            });
            worker.SetApartmentState(ApartmentState.STA);
            worker.IsBackground = true;
            worker.Start();

            return done.Task;
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _view?.Refresh();
        }

        // 목록을 다시 읽으면서 처음 상태로 되돌린다.
        // 검색어가 남아 있으면 새로 잡힌 것이 걸러져 안 보일 수 있어,
        // 무엇이 늘었는지 확인하려던 의도와 어긋난다.
        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            SearchBox.Clear();
            await LoadAsync(true);
            SearchBox.Focus();
        }

        // ── 고르기 ───────────────────────────────────────────────────

        // 줄 아무 데나 누르면 담기고, 다시 누르면 빠진다.
        // Ctrl 을 눌러야 여러 개가 골라지는 방식은 알아채기 어려워 쓰지 않았다.
        private void Row_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is ListViewItem { DataContext: ProgramEntry entry })
                Toggle(entry);
        }

        private void Toggle(ProgramEntry entry)
        {
            var already = FindChosen(entry.ExecutableName);
            if (already != null) Drop(already);
            else Add(entry);
        }

        private void Add(ProgramEntry entry)
        {
            entry.IsChosen = true;
            _chosen.Add(entry);
            UpdateChosenView();
        }

        private void Drop(ProgramEntry entry)
        {
            _chosen.Remove(entry);
            entry.IsChosen = false;

            // 목록에 같은 실행 파일이 따로 있으면 그 줄의 체크도 함께 푼다
            // (직접 찾아보기로 담은 것은 목록의 객체와 다른 객체이기 때문이다).
            if (ProgramList.ItemsSource is IEnumerable<ProgramEntry> programs)
                foreach (var p in programs)
                    if (string.Equals(p.ExecutableName, entry.ExecutableName, StringComparison.OrdinalIgnoreCase))
                        p.IsChosen = false;

            UpdateChosenView();
        }

        private ProgramEntry? FindChosen(string executableName)
            => _chosen.FirstOrDefault(
                   c => string.Equals(c.ExecutableName, executableName, StringComparison.OrdinalIgnoreCase));

        private void RemoveChosen_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { DataContext: ProgramEntry entry }) Drop(entry);
        }

        private void UpdateChosenView()
        {
            ChosenTitle.Text = $"고른 프로그램 {_chosen.Count}개";
            ChosenEmptyText.Visibility = _chosen.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ConfirmButton.IsEnabled = _chosen.Count > 0;
        }

        // ── 목록에 없는 프로그램 ─────────────────────────────────────

        // 시작 메뉴에 바로가기가 없거나, 이름이 영어라 한글로 찾히지 않는 경우의 길.
        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "프로그램 또는 바로가기 선택",
                Filter = "프로그램·바로가기 (*.exe;*.lnk)|*.exe;*.lnk|프로그램 (*.exe)|*.exe|바로가기 (*.lnk)|*.lnk",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            };
            if (dialog.ShowDialog(this) != true) return;

            string? executable = ProgramCatalog.ResolveExecutableName(dialog.FileName);
            if (executable == null)
            {
                MessageBox.Show(
                    "이 바로가기가 어떤 프로그램을 가리키는지 알아내지 못했습니다.\n프로그램(.exe) 파일을 직접 골라 주세요.",
                    "실행 파일을 찾지 못함", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (FindChosen(executable) != null) return;   // 이미 담겨 있으면 그대로 둔다

            // 목록에 같은 것이 있으면 그 줄을 담아 체크까지 함께 보이게 한다.
            var listed = (ProgramList.ItemsSource as IEnumerable<ProgramEntry>)?.FirstOrDefault(
                             p => string.Equals(p.ExecutableName, executable, StringComparison.OrdinalIgnoreCase));

            Add(listed ?? new ProgramEntry
            {
                DisplayName = Path.GetFileNameWithoutExtension(dialog.FileName),
                ExecutableName = executable,
                ExecutablePath = dialog.FileName,
                Source = ProgramSource.Picked,
            });
        }

        // ── 확인 ─────────────────────────────────────────────────────

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            if (_chosen.Count == 0) return;

            SelectedExecutables.AddRange(_chosen.Select(c => c.ExecutableName));
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
