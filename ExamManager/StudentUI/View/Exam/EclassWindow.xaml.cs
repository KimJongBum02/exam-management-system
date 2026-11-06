using System.Windows;
using ExamManager.Shared;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using StudentUI.Service;

namespace StudentUI.View.Exam
{
    // 시험 중에 유일하게 들어갈 수 있는 사이트(학교 LMS)를 보여 주는 창.
    //
    // 시험 중에는 방화벽이 교수 PC 말고 전부 막는데, FirewallPolicyService 가 이 사이트의 주소 하나만
    // 빼 둔다. 그런데 DNS 서버도 막히므로 브라우저가 이름을 주소로 바꿀 수 없다. 그래서 크로미움에
    // --host-resolver-rules 로 '이 이름은 이 주소' 를 박아 DNS 를 아예 건너뛴다.
    //
    // 덤으로 다른 브라우저는 이름을 못 풀어서, 열어 둔 주소를 실제로 쓸 수 있는 것이 이 창뿐이 된다.
    public partial class EclassWindow : Window
    {
        private static string Home => $"https://{FirewallPolicyService.AllowedSiteHost}/";

        public EclassWindow()
        {
            InitializeComponent();

            string? ip = FirewallPolicyService.AllowedSiteIp;
            if (ip == null)
                ShowFailure($"{FirewallPolicyService.AllowedSiteHost} 의 주소를 확인하지 못했습니다. 시험 시작 시점에 이름을 풀지 못했습니다.");

            // InPrivate: 로그인 쿠키를 디스크에 남기지 않는다. 강의실 PC 라서 다음 학생이 앞 학생
            // eclass 계정으로 들어가 있으면 안 된다. (그래서 창을 닫을 때 지울 것도 없다)
            // UserDataFolder 를 정하지 않으면 exe 옆에 만들려다 Program Files 에서 실패한다.
            //
            // 주소를 못 풀었어도 그대로 띄운다. 어차피 들어가지 못하고 위 안내가 덮지만,
            // 컨트롤을 초기화하지 않은 채 두면 WPF 가 알아서 기본값으로 초기화를 시도한다.
            Browser.CreationProperties = new CoreWebView2CreationProperties
            {
                UserDataFolder = AppRecord.PathOf("WebView2"),
                IsInPrivateModeEnabled = true,
                AdditionalBrowserArguments = ip == null
                    ? null
                    : $"--host-resolver-rules=\"MAP {FirewallPolicyService.AllowedSiteHost} {ip}\"",
            };

            // 차단이 풀리면(답안 제출 성공) 열어 둔 주소도 사라진다. 깨진 페이지를 들고 있지 않고 닫는다.
            ExamMonitorService.Instance.NetworkStateChanged += OnNetworkStateChanged;
            Closed += (_, _) => ExamMonitorService.Instance.NetworkStateChanged -= OnNetworkStateChanged;

            _ = OpenAsync();
        }

        private async Task OpenAsync()
        {
            try
            {
                await Browser.EnsureCoreWebView2Async();
            }
            catch (Exception ex)
            {
                // WebView2 런타임이 없는 PC 에서 여기로 온다. 조용히 빈 창을 남기면 학생이 이유를 모른다.
                ShowFailure($"브라우저 구성 요소를 띄우지 못했습니다. ({ex.Message})");
                return;
            }

            Browser.CoreWebView2.NavigationStarting += OnNavigationStarting;
            Browser.CoreWebView2.NewWindowRequested += OnNewWindowRequested;

            // 이 사이트는 CDN 에 있는 CSS·JS 를 불러 쓰는데, 그 주소는 방화벽이 막는다. 문제는
            // 막히는 모양이 '거절' 이 아니라 '무응답' 이라, 브라우저가 이름 하나에 11초씩 매달린다.
            // 그 태그들이 화면과 파서를 세우는 자리(head 의 stylesheet, async 없는 script)에 있어서
            // 로그인 화면 하나가 수십 초 뒤에 뜬다. 어차피 받아 올 수 없는 자원이니 기다리지 않고
            // 여기서 바로 거절한다. 덤으로 이클래스 밖으로 나가는 요청이 아예 없어진다.
            Browser.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            Browser.CoreWebView2.WebResourceRequested += OnWebResourceRequested;

            Browser.CoreWebView2.Navigate(Home);
        }

        // 허용한 사이트 밖으로 나가려 하면 첫 화면으로 되돌린다.
        private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
        {
            if (IsAllowed(e.Uri)) return;

            e.Cancel = true;

            // 취소되는 중에 바로 다음 이동을 걸면 그 이동이 묻힌다. 한 박자 뒤로 미룬다.
            Dispatcher.BeginInvoke(new Action(() => Browser.CoreWebView2?.Navigate(Home)));
        }

        // target="_blank" 링크는 여기로 온다. 그냥 두면 WebView2 가 이 필터가 붙지 않은 새 창을 만들어,
        // 그 창으로는 어디든 갈 수 있게 된다.
        private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            e.Handled = true;
            Browser.CoreWebView2.Navigate(IsAllowed(e.Uri) ? e.Uri : Home);
        }

        private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
        {
            // data:·blob: 처럼 네트워크를 쓰지 않는 주소는 호스트가 없어 아래 비교에 걸린다. 그대로 둔다.
            if (!Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out Uri? uri)) return;
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return;
            if (IsAllowed(e.Request.Uri)) return;

            e.Response = Browser.CoreWebView2.Environment.CreateWebResourceResponse(null, 403, "Blocked", "");
        }

        // 주소를 문자열로 견주면 'eclass.yuhan.ac.kr.남의사이트.com' 이 통과한다. 호스트만 떼어 견준다.
        private static bool IsAllowed(string uri) =>
            Uri.TryCreate(uri, UriKind.Absolute, out Uri? parsed) &&
            parsed.Host.Equals(FirewallPolicyService.AllowedSiteHost, StringComparison.OrdinalIgnoreCase);

        private void OnNetworkStateChanged() => UiThread.Post(() =>
        {
            if (ExamMonitorService.Instance.NetworkState != NetworkBlockState.Blocked) Close();
        });

        private void ShowFailure(string reason)
        {
            FailReasonText.Text = reason;
            FailPanel.Visibility = Visibility.Visible;
        }
    }
}
