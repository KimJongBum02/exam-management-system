using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ProfessorUI.Service
{
    // 배포용 시험 묶음을 만드는 일을 맡는다.
    //   무엇을 묶을지 고르는 것(화면·목록)   → ExamReadyViewModel
    //   어떻게 묶을지(7za 실행)              → ZipService → FileControl.dll
    //   언제·어떤 이름으로 묶을지(여기)      → 회차 번호, 같은 내용 판정, 준비 상태 기록
    //
    // 화면을 띄우지 않는다. 판단 결과만 돌려주고, 물어보는 것은 화면이 한다.
    public class ExamReadyService
    {
        public static ExamReadyService Instance { get; } = new ExamReadyService();

        // 배포용 묶음이 만들어지는 곳. 교수가 바로 확인할 수 있도록 바탕화면에 둔다.
        public static string PackageFolder { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "시험 파일");

        // 이번 실행에서 암호화·압축을 마친 묶음들. 같은 내용을 다시 압축하려 하면 경고하는 데 쓴다.
        // 파일로 남기지 않는다 — 암호는 메모리에만 있어서, 앱을 다시 켜면 이전 묶음은 배포에 쓸 수 없고
        // 어차피 새로 만들어야 한다.
        private readonly List<PackagedRecord> _packaged = new();

        // 이번 실행에서 마지막으로 만든 묶음과, 그 묶음을 학생에게 보냈는지.
        // 보내기 전에 다시 압축하면 같은 번호를 새 내용으로 바꾸고, 보낸 뒤에는 다음 번호를 쓴다.
        // SendFileState.IsFileDistributed 는 [새 시험 준비]에서 다시 false 가 되므로 따로 기억한다.
        private string? _currentOutput;
        private bool _currentDistributed;

        private ExamReadyService()
        {
            SendFileState.StateChanged += () =>
            {
                if (SendFileState.IsFileDistributed && _currentOutput != null &&
                    string.Equals(SendFileState.PackagePath, _currentOutput, StringComparison.OrdinalIgnoreCase))
                    _currentDistributed = true;
            };
        }

        // 이미 만든 묶음 한 건 (같은 내용을 다시 압축하려 할 때 알려 준다)
        public sealed record PackagedRecord(string Fingerprint, string OutputPath, DateTime PackagedAt);

        // 압축을 시작하기 전 판단 결과.
        // Previous 가 있으면 같은 내용으로 이미 만든 묶음이 남아 있다는 뜻이다.
        public sealed record PackagePlan(string Fingerprint, PackagedRecord? Previous);

        // 압축 결과. Ok 가 false 면 Error 에 이유가 담긴다.
        public sealed record PackageResult(bool Ok, string OutputPath, string Error);

        // 아직 배포하지 않은 묶음이 있으면 그 파일 이름, 없으면 null.
        // [모두 지우기]가 이 묶음을 함께 지울지 판단하는 데 쓴다.
        public string? UndistributedPackageName
            => _currentOutput != null && !_currentDistributed && File.Exists(_currentOutput)
               ? Path.GetFileName(_currentOutput) : null;

        // 고른 항목을 훑어 같은 내용을 이미 압축했는지 본다.
        // 폴더가 크면 시간이 걸려 백그라운드에서 돈다. 읽지 못하면 예외가 그대로 올라간다.
        public async Task<PackagePlan> PlanAsync(IReadOnlyList<string> items)
        {
            string fingerprint = await Task.Run(() => ComputeFingerprint(items));

            // 묶음 파일을 지웠으면 다시 만들어야 하므로 알리지 않는다.
            PackagedRecord? previous = _packaged.LastOrDefault(
                r => r.Fingerprint == fingerprint && File.Exists(r.OutputPath));

            return new PackagePlan(fingerprint, previous);
        }

        // 묶음을 만든다. 성공하면 배포 단계가 읽을 준비 상태(SendFileState)까지 채운다.
        // fingerprint 는 PlanAsync 가 돌려준 값을 그대로 넘긴다 — 파일을 두 번 훑지 않기 위함이다.
        public async Task<PackageResult> CreateAsync(IReadOnlyList<string> items, string fingerprint)
        {
            string output;
            try
            {
                Directory.CreateDirectory(PackageFolder);
                output = ChooseOutputPath(PackageFolder);
            }
            catch (Exception ex)
            {
                return new PackageResult(false, string.Empty, $"시험 파일 폴더를 준비하지 못했습니다: {ex.Message}");
            }

            string? password;
            try
            {
                // 선택 파일들을 스테이징 폴더로 묶어 7za로 압축+암호화 (백그라운드 실행)
                password = await Task.Run(() => ZipService.Package(items, output));
            }
            catch (Exception ex)
            {
                // 배포가 진행 중이면 이전 아카이브가 잠겨 바꿔 놓지 못하는 등으로 실패할 수 있다.
                // 이때 이전 아카이브와 배포 정보는 그대로 남는다(ZipService).
                return new PackageResult(false, output,
                    $"압축/암호화 실패: {ex.Message}\n배포가 진행 중이면 끝난 뒤 다시 시도해 주세요.");
            }

            if (password == null)
                return new PackageResult(false, output, "압축/암호화 실패. 7za.exe와 입력을 확인하세요.");

            // 번호가 옮겨 갔으면(예: 더 작은 번호가 비어서) 보낸 적 없는 옛 묶음은 지운다.
            // 남겨 두면 폴더에 배포하지 않을 파일이 쌓이고, 다음 번호 계산도 어긋난다.
            if (_currentOutput != null && !_currentDistributed &&
                !string.Equals(_currentOutput, output, StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(_currentOutput); } catch (Exception) { }
                _packaged.RemoveAll(r => string.Equals(r.OutputPath, _currentOutput, StringComparison.OrdinalIgnoreCase));
            }

            // 배포 단계가 읽도록 공용 저장소에 보관.
            // 예: 0917_T1 — 학생 PC 에서는 이 이름이 그대로 시험 폴더 이름이 된다.
            SendFileState.ExamId = Path.GetFileNameWithoutExtension(output);
            SendFileState.PackagePath = output;
            SendFileState.Password = password;
            SendFileState.IsFilePrepared = true;

            _currentOutput = output;
            _currentDistributed = false;

            // 같은 자리의 이전 기록은 이 묶음으로 바뀌었으므로 버린다.
            _packaged.RemoveAll(r => string.Equals(r.OutputPath, output, StringComparison.OrdinalIgnoreCase));
            _packaged.Add(new PackagedRecord(fingerprint, output, DateTime.Now));

            return new PackageResult(true, output, string.Empty);
        }

        // 아직 배포하지 않은 묶음을 지우고 준비 상태를 되돌린다.
        // 남겨 두면 잘못 고른 파일이 그대로 배포될 수 있다. 지우지 못하면 이유를 돌려준다.
        public string? DeleteUndistributedPackage()
        {
            if (_currentOutput == null) return null;

            try
            {
                File.Delete(_currentOutput);
            }
            catch (Exception ex)
            {
                return $"시험 압축 파일을 지우지 못했습니다: {ex.Message}";
            }

            _packaged.RemoveAll(r => string.Equals(r.OutputPath, _currentOutput, StringComparison.OrdinalIgnoreCase));
            _currentOutput = null;

            SendFileState.ExamId = null;
            SendFileState.PackagePath = null;
            SendFileState.Password = null;
            SendFileState.IsFilePrepared = false;
            return null;
        }

        // 묶음 이름을 정한다. 하루에 시험을 두 번(오전·오후) 보므로 날짜 뒤에 회차를 붙인다: 0917_T1, 0917_T2 …
        //
        // 폴더에서 비어 있는 가장 작은 번호를 쓴다. 교수가 폴더에서 묶음을 지우면 그 번호가 다시 쓰인다.
        // 아직 배포하지 않은 묶음은 자기 번호를 비운 것으로 친다 — 그대로 다시 압축하면 같은 번호가 유지되고
        // (빠뜨린 파일을 더한 경우), 더 작은 번호가 비어 있으면 그쪽으로 옮겨 간다.
        // 이미 배포한 묶음의 번호는 학생 PC 의 시험 폴더 이름이므로 다시 쓰지 않는다.
        private string ChooseOutputPath(string packageDir)
        {
            string prefix = DateTime.Now.ToString("MMdd") + "_T";
            var pattern = new Regex("^" + Regex.Escape(prefix) + @"(\d+)\.7z$", RegexOptions.IgnoreCase);

            var used = Directory.GetFiles(packageDir)
                .Select(file => pattern.Match(Path.GetFileName(file)))
                .Where(match => match.Success)
                .Select(match => int.Parse(match.Groups[1].Value))
                .ToHashSet();

            // 아직 보내지 않은 내 묶음은 덮어쓸 것이므로 빈 번호로 본다.
            if (_currentOutput != null && !_currentDistributed)
            {
                var mine = pattern.Match(Path.GetFileName(_currentOutput));
                if (mine.Success) used.Remove(int.Parse(mine.Groups[1].Value));
            }

            int number = 1;
            while (used.Contains(number)) number++;
            return Path.Combine(packageDir, $"{prefix}{number}.7z");
        }

        // 선택한 항목의 내용을 한 줄로 요약한다. 두 번의 선택이 같은 내용인지 비교하는 데 쓴다.
        // 파일마다 경로·크기·수정 시각을 담는다 — 원본을 고쳐 저장하면 수정 시각이 바뀌어 새 내용으로 본다.
        // 해시를 내지 않는 이유: 시험 폴더가 수백 MB여도 선택할 때마다 기다리게 할 수는 없다.
        private static string ComputeFingerprint(IEnumerable<string> items)
        {
            var lines = new List<string>();
            foreach (string item in items)
            {
                if (Directory.Exists(item))
                {
                    lines.Add(item + "\\");
                    foreach (string file in Directory.GetFiles(item, "*", SearchOption.AllDirectories))
                        lines.Add(Describe(file));
                }
                else
                {
                    lines.Add(Describe(item));
                }
            }

            lines.Sort(StringComparer.OrdinalIgnoreCase);
            return string.Join("\n", lines).ToUpperInvariant();

            static string Describe(string file)
            {
                var info = new FileInfo(file);
                return $"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
            }
        }
    }
}
