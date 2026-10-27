using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using XieBox.Contracts;
using XieBox.Platform.UI;

namespace XieBox.NetworkDiagnostics;

public sealed record NetworkCheck(string Name, string Result, string Detail, int? StatusCode = null);
public sealed record NetworkConclusion(string Title, string Explanation, string Advice);
public static class NetworkEngine
{
    public static NetworkConclusion Explain(IReadOnlyList<NetworkCheck> rows, bool complete)
    {
        if (!complete) return new("检查尚未完成", "目前的信息不足以判断原因。", "请重新检查；如果反复中断，可以导出报告联系我们。");
        var target = rows.FirstOrDefault(r => r.Name == "HTTPS 连接");
        var reference = rows.FirstOrDefault(r => r.Name == "对照网站");
        if (target?.Result == "已收到响应")
        {
            if (target.StatusCode >= 400) return new("网站可以连接，但返回了异常结果", "电脑已收到网站回复，网站可能限制访问或暂时无法处理请求。", "先用浏览器打开该网站，确认是否需要登录。若仍无法访问，请稍后重试或导出报告联系我们。");
            return new("网络连接正常", "电脑已成功连接互联网，测试网站访问正常。", "无需处理。如有某个网站打不开，可更换为该网址进行检查。");
        }
        if (!rows.Any(r => r.Result == "已连接")) return new("未检测到已连接的网络", "电脑当前没有报告可用的网络连接。", "请确认 Wi-Fi 已连接，或网线已插好，然后再次检查。若仍无连接，请打开网络设置查看状态。");
        if (reference?.Result == "已收到响应") return new("测试网站未连通，其他网站可以连接", "问题可能与测试网站、网址或针对该网站的访问限制有关，尚不能确定具体原因。", "请检查网址是否正确，并用浏览器重试。仍无法访问时，导出报告联系我们。");
        if (rows.Any(r => r.Name == "DNS 解析" && r.Result != "成功")) return new("暂时无法查到测试网站的地址", "网站地址查询未成功，可能是网址、网络连接或地址查询服务出现问题。", "请先确认网址，再用手机连接同一个 Wi-Fi 测试。手机也无法上网时，请检查路由器或联系网络服务商；只有电脑异常时，可导出报告联系我们。");
        return new("网络已连接，但网站访问未成功", "已查到网站地址，但本次网页连接未完成；还不能确定是网络、网站还是电脑设置导致。", "请用手机连接同一个 Wi-Fi 测试，并换一个网站重试。不要反复重置网络；可导出报告联系我们继续排查。");
    }
    public static bool WebsiteSucceeded(NetworkCheck? check) => check?.Result == "已收到响应" && check.StatusCode is >= 200 and < 400;
    public static NetworkConclusion ExplainNetwork(IReadOnlyList<NetworkCheck> rows, bool complete)
    {
        if (!complete) return Explain(rows, false);
        if (rows.Any(r => (r.Name == "HTTPS 连接" || r.Name == "对照网站") && WebsiteSucceeded(r)))
            return new("网络连接正常", "电脑已成功连接互联网。", "无需处理。若某个网页打不开，可使用右上角的“测试网站链接”。");
        return Explain(rows, true);
    }
    public static Uri WebsiteUri(string input)
    {
        input = input.Trim();
        if (!input.Contains("://")) input = "https://" + input;
        if (!Uri.TryCreate(input, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https") || string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo))
            throw new ArgumentException("请输入有效的 http 或 https 网站链接。");
        return uri;
    }
    public static string ValidateHost(string host)
    {
        host = host.Trim();
        if (host.Length == 0 || host.Length > 253 || Uri.CheckHostName(host) == UriHostNameType.Unknown || host.Contains('/') || host.Contains(':')) throw new ArgumentException("请输入域名或 IPv4 地址，不包含协议、端口和路径。");
        return host;
    }
    public static IReadOnlyList<NetworkCheck> Local()
    {
        var output = new List<NetworkCheck>();
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback))
        {
            var properties = adapter.GetIPProperties();
            var addresses = string.Join("、", properties.UnicastAddresses.Select(a => a.Address.ToString()));
            var dns = string.Join("、", properties.DnsAddresses);
            output.Add(new(adapter.Name, adapter.OperationalStatus == OperationalStatus.Up ? "已连接" : "未连接", $"地址：{addresses}\nDNS：{dns}"));
        }
        if (output.Count == 0) output.Add(new("网络适配器", "未发现", "请检查网络设备是否启用。"));
        return output;
    }
    public static async Task<IReadOnlyList<NetworkCheck>> Diagnose(string host, IProgress<NetworkCheck>? progress, CancellationToken token)
    {
        var website = WebsiteUri(host); host = website.Host; var result = new List<NetworkCheck>();
        void Add(NetworkCheck row) { result.Add(row); progress?.Report(row); }
        foreach (var item in Local()) { token.ThrowIfCancellationRequested(); Add(item); }
        var gateways = NetworkInterface.GetAllNetworkInterfaces().Where(a => a.OperationalStatus == OperationalStatus.Up).SelectMany(a => a.GetIPProperties().GatewayAddresses).Select(a => a.Address).Where(a => !a.Equals(IPAddress.Any) && !a.Equals(IPAddress.IPv6Any)).Distinct().Take(4);
        foreach (var gateway in gateways)
        {
            try
            {
                using var ping = new Ping(); var reply = await ping.SendPingAsync(gateway, TimeSpan.FromSeconds(2), new byte[16], null, token);
                Add(new("网关 " + gateway, reply.Status == IPStatus.Success ? "可达" : "未响应", reply.Status == IPStatus.Success ? $"往返 {reply.RoundtripTime} ms" : "网关可能禁止 Ping，此结果不能单独判断断网。"));
            }
            catch (PingException) { Add(new("网关 " + gateway, "无法测试", "设备或防火墙可能禁止 ICMP。")); }
        }
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(5));
            var addresses = await Dns.GetHostAddressesAsync(host, timeout.Token);
            Add(new("DNS 解析", "成功", host + " → " + string.Join("、", addresses.Select(a => a.ToString()))));
        }
        catch (Exception e) when (e is System.Net.Sockets.SocketException or OperationCanceledException)
        { token.ThrowIfCancellationRequested(); Add(new("DNS 解析", "未完成", "解析失败或超时：" + e.Message)); }
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
            using var request = new HttpRequestMessage(HttpMethod.Get, website);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            Add(new("HTTPS 连接", "已收到响应", $"{host} 返回 HTTP {(int)response.StatusCode}。仅代表该站点当前可达。", (int)response.StatusCode));
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException)
        { token.ThrowIfCancellationRequested(); Add(new("HTTPS 连接", "未完成", e.ToString())); }
        var reference = host.Equals("www.microsoft.com", StringComparison.OrdinalIgnoreCase) ? "www.baidu.com" : "www.microsoft.com";
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://" + reference + "/");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            Add(new("对照网站", "已收到响应", $"{reference}：HTTP {(int)response.StatusCode}", (int)response.StatusCode));
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException)
        { token.ThrowIfCancellationRequested(); Add(new("对照网站", "未完成", reference + "：" + e.Message)); }
        return result;
    }
    [DllImport("dnsapi.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DnsFlushResolverCache();
    public static bool FlushDns() => DnsFlushResolverCache();
}
public sealed class NetworkModel(IPlatformContext context) : ToolModel, IReportable
{
    public string Host { get; set; } = "";
    public bool WebsiteMode { get; private set; }
    public string WebsiteResult { get; private set; } = "";
    public string WebsiteBrushKey { get; private set; } = "PendingBrush";
    internal void UpdateWebsiteResult()
    {
        var target = Entries.FirstOrDefault(r => r.Name == "HTTPS 连接");
        WebsiteResult = !Complete ? (IsBusy ? "正在测试网站…" : "网站测试未完成") : NetworkEngine.WebsiteSucceeded(target) ? "网站响应正常" : target?.StatusCode is int code ? $"网站访问异常（HTTP {code}）" : "网站未能连接";
        WebsiteBrushKey = !Complete ? "PendingBrush" : NetworkEngine.WebsiteSucceeded(target) ? "SuccessBrush" : "ErrorBrush";
        Notify(nameof(WebsiteResult)); Notify(nameof(WebsiteBrushKey));
    }
    public IReadOnlyList<NetworkCheck> Entries { get; private set; } = [];
    public string ConclusionTitle { get; private set; } = "";
    public string Explanation { get; private set; } = "";
    public string Advice { get; private set; } = "";
    public DateTimeOffset? CheckedAt { get; private set; }
    public string CheckedHost { get; private set; } = "";
    public bool Complete { get; private set; }
    public bool CanExport => !IsBusy && CheckedAt is not null;
    public string ReportTitle => "网络检查";
    public bool CanViewDetails => Complete && !IsBusy;
    private string? _report;
    private void Present(NetworkConclusion conclusion)
    {
        ConclusionTitle = conclusion.Title; Explanation = conclusion.Explanation; Advice = conclusion.Advice;
        Notify(nameof(ConclusionTitle)); Notify(nameof(Explanation)); Notify(nameof(Advice)); Notify(nameof(CanExport)); Notify(nameof(CanViewDetails));
    }
    public string BuildReport()
    {
        if (CheckedAt is null) throw new InvalidOperationException("请先进行一次检查。");
        return _report ??= ReportBuilder.Build(ReportTitle, CheckedAt.Value, $"检查类型：{(WebsiteMode ? "指定网站测试" : "网络连接检查")}；测试地址：{CheckedHost}；检查状态：{(Complete ? "完成" : "未完成")}", $"{ConclusionTitle}\n{Explanation}\n{(WebsiteMode ? WebsiteResult : "")}\n建议：{Advice}", ["技术记录", .. Entries.Select(r => $"{r.Name}：{r.Result}\n{r.Detail}")]);
    }
    public void UseSnapshot(IReadOnlyList<NetworkCheck> entries, DateTimeOffset time)
    {
        if (IsBusy) return;
        Entries = entries; CheckedAt = time; CheckedHost = "https://www.baidu.com/"; Complete = true; WebsiteMode = false; _report = null;
        Present(NetworkEngine.ExplainNetwork(entries, true)); ResultsLayout = true;
        Notify(nameof(Entries)); Notify(nameof(CheckedHost)); Notify(nameof(WebsiteMode));
        Message("显示本次体检的网络检查结果");
    }
    public Task DiagnoseAsync() => RunDiagnostics(false);
    public Task TestWebsiteAsync() => RunDiagnostics(true);
    private Task RunDiagnostics(bool websiteMode) => Run(async token =>
    {
        if (!(await context.CheckCapabilityAsync("scan", token)).Allowed) { Message("暂时无法开始检查。"); return; }
        var host = websiteMode ? NetworkEngine.WebsiteUri(Host).AbsoluteUri : "https://www.baidu.com/";
        WebsiteMode = websiteMode; Notify(nameof(WebsiteMode));
        CheckedHost = host; CheckedAt = DateTimeOffset.Now; Complete = false; Entries = []; _report = null;
        Notify(nameof(CheckedHost)); UpdateWebsiteResult();
        ResultsLayout = true; Notify(nameof(Entries)); Message("正在检查…");
        Present(new("正在检查网络连接", "正在测试电脑连接和网站访问情况。", "检查通常需要几秒到半分钟，可以随时停止。"));
        // The worker owns the list; copy it only after completion or cancellation.
        var rows = new List<NetworkCheck>();
        try
        {
            Entries = await Task.Run(() => NetworkEngine.Diagnose(host, new InlineProgress(row => { rows.Add(row); var snapshot = rows.ToArray(); System.Windows.Application.Current.Dispatcher.Invoke(() => { Entries = snapshot; Notify(nameof(Entries)); }); }), token), token);
            Complete = true;
        }
        catch (OperationCanceledException) { Entries = rows.ToArray(); }
        catch (Exception e) when (e is NetworkInformationException or System.Net.Sockets.SocketException)
        { rows.Add(new("检查异常", "未完成", e.ToString())); Entries = rows.ToArray(); }
        var conclusion = NetworkEngine.ExplainNetwork(Entries, Complete);
        if (WebsiteMode && Complete)
        {
            var target = Entries.FirstOrDefault(r => r.Name == "HTTPS 连接");
            conclusion = conclusion with { Advice = NetworkEngine.WebsiteSucceeded(target)
                ? "网站已响应本次请求。如果浏览器仍无法打开，请导出报告并联系我们。"
                : target?.StatusCode is >= 400 ? $"网站返回错误（HTTP {target.StatusCode}）。可稍后重试，或导出报告联系我们。"
                : "请检查网站链接是否正确。如果仍无法访问，请导出报告并联系我们。" };
        }
        Present(conclusion); Notify(nameof(Entries)); UpdateWebsiteResult();
        Message(Complete ? "检查完成" : "检查未完成，可重新检查或导出报告。");
    });
    private sealed class InlineProgress(Action<NetworkCheck> add) : IProgress<NetworkCheck>
    { public void Report(NetworkCheck value) => add(value); }
}
public static class NetworkPage
{
    public static ToolPage Create(IPlatformContext context)
    {
        var model = new NetworkModel(context);
        var options = new StackPanel();

        var page = new ToolPage(model, "网络检查", "检查电脑、路由器与互联网的连接情况。", options, "开始检查", model.DiagnoseAsync);
        var summary = new StackPanel { Margin = new Thickness(24, 48, 24, 32), MaxWidth = 680, HorizontalAlignment = HorizontalAlignment.Stretch };
        var title = ToolPage.Text("", 26, true); title.TextAlignment = TextAlignment.Center; title.SetBinding(TextBlock.TextProperty, new Binding("ConclusionTitle")); summary.Children.Add(title);
        var explanation = ToolPage.Text(""); explanation.TextAlignment = TextAlignment.Center; explanation.Margin = new Thickness(0, 12, 0, 22); explanation.SetBinding(TextBlock.TextProperty, new Binding("Explanation")); summary.Children.Add(explanation);
        summary.Children.Add(new NetworkMap(model) { Margin = new Thickness(0, 12, 0, 26) });
        var details = ToolPage.Button("查看详细信息", () => CreateDetails(model, Window.GetWindow(page)).ShowDialog());
        details.SetResourceReference(FrameworkElement.StyleProperty, "FlatButton");
        details.HorizontalAlignment = HorizontalAlignment.Center; details.Margin = new Thickness(0, -12, 0, 20);
        details.SetBinding(UIElement.VisibilityProperty, new Binding(nameof(model.CanViewDetails)) { Converter = new BooleanToVisibilityConverter() });
        summary.Children.Add(details);
        var websiteContent = new StackPanel();
        var websiteTitle = ToolPage.Text("", 15, true); websiteTitle.SetBinding(TextBlock.TextProperty, new Binding("WebsiteResult")); websiteTitle.SetBinding(ThemeResources.ForegroundKeyProperty, new Binding("WebsiteBrushKey")); websiteContent.Children.Add(websiteTitle);
        var websiteAddress = ToolPage.Text(""); websiteAddress.Margin = new Thickness(0, 8, 0, 0); websiteAddress.SetBinding(TextBlock.TextProperty, new Binding("CheckedHost")); websiteContent.Children.Add(websiteAddress);
        var websiteCard = new Border { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Padding = new Thickness(24, 18, 24, 18), Margin = new Thickness(0, 0, 0, 12), Child = websiteContent };
        websiteCard.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        websiteCard.SetBinding(ThemeResources.BorderKeyProperty, new Binding("WebsiteBrushKey"));
        websiteCard.SetBinding(UIElement.VisibilityProperty, new Binding("WebsiteMode") { Converter = new BooleanToVisibilityConverter() });
        summary.Children.Add(websiteCard);
        var adviceContent = new StackPanel();
        adviceContent.Children.Add(ToolPage.Text("处理建议", 14, true));
        var advice = ToolPage.Text(""); advice.Margin = new Thickness(0, 8, 0, 0); advice.SetBinding(TextBlock.TextProperty, new Binding("Advice")); adviceContent.Children.Add(advice);
        var adviceCard = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(24, 18, 24, 18), Child = adviceContent };
        adviceCard.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush"); adviceCard.SetResourceReference(Border.BorderBrushProperty, "LineBrush"); summary.Children.Add(adviceCard);
        page.SetResultsContent(new ScrollViewer { Content = summary, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalContentAlignment = HorizontalAlignment.Stretch });
        void TestWebsite()
        {
            var input = new TextBox { Text = model.Host, Margin = new Thickness(0, 8, 0, 16) };
            var body = new StackPanel { Margin = new Thickness(24) };
            body.Children.Add(ToolPage.Text("网站链接")); body.Children.Add(input);
            var error = ToolPage.Text(""); body.Children.Add(error);
            var dialog = new DialogWindow { Owner = Window.GetWindow(page), Title = "测试网站链接", Width = 520, Content = body }.FitContent();
            var submit = ToolPage.Button("开始测试", () =>
            {
                try { NetworkEngine.WebsiteUri(input.Text); }
                catch (ArgumentException ex) { error.Text = ex.Message; return; }
                model.Host = input.Text.Trim();
                dialog.Close();
                _ = model.TestWebsiteAsync();
            }, true);
            submit.DataContext = model; submit.HorizontalAlignment = HorizontalAlignment.Right; body.Children.Add(submit);
            dialog.Loaded += (_, _) => input.Focus();
            dialog.ShowDialog();
        }
        page.Actions.Children.Add(ToolPage.Button("测试网站链接", TestWebsite));
        page.Actions.Children.Add(ToolPage.Button("再次检查", async () => await model.DiagnoseAsync(), true));
        var export = ToolPage.Button("导出排查报告", () =>
        {
            ReportExporter.Save(Window.GetWindow(page), model, model.Message);
        });
        model.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(model.IsIdle)) { model.Notify(nameof(model.CanExport)); model.Notify(nameof(model.CanViewDetails)); model.UpdateWebsiteResult(); } };
        export.SetBinding(UIElement.IsEnabledProperty, new Binding("CanExport")); page.FooterActions.Children.Add(export);
        page.FooterActions.Children.Add(ToolPage.Button("联系我们", () => ToolPage.Notice(Window.GetWindow(page), "联系我们", "请先导出排查报告，并附上遇到的问题和发生时间。\n\n邮箱：1416849251@qq.com")));
        page.FooterActions.Children.Add(ToolPage.Button("网络设置", () => { try { Process.Start(new ProcessStartInfo("ms-settings:network-status") { UseShellExecute = true }); } catch (System.ComponentModel.Win32Exception) { model.Message("无法打开网络设置，请从 Windows 设置中打开“网络和 Internet”。"); } }));
        return page;
    }
    public static DialogWindow CreateDetails(NetworkModel model, Window? owner = null)
    {
        var snapshot = model.Entries.ToArray();
        var body = new DockPanel { Margin = new Thickness(24) };
        var caption = ToolPage.Text($"检查时间：{model.CheckedAt:yyyy-MM-dd HH:mm:ss}　·　{(model.WebsiteMode ? "网站测试" : "网络连接检查")}\n测试地址：{model.CheckedHost}", 12);
        caption.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); caption.Margin = new Thickness(0, 0, 0, 16); DockPanel.SetDock(caption, Dock.Top); body.Children.Add(caption);
        var table = new DataGrid { ItemsSource = snapshot, AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, CanUserDeleteRows = false, HeadersVisibility = DataGridHeadersVisibility.Column };
        foreach (var (label, field, width) in new[] { ("检查项目", "Name", 1.2), ("结果", "Result", 0.8), ("详细信息", "Detail", 3d) })
        {
            var style = new Style(typeof(TextBlock)); style.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
            table.Columns.Add(new DataGridTextColumn { Header = label, Binding = new Binding(field), Width = new DataGridLength(width, DataGridLengthUnitType.Star), ElementStyle = style });
        }
        body.Children.Add(table);
        return new DialogWindow { Owner = owner, Title = "网络检查 · 详细信息", Width = Math.Min(880, SystemParameters.WorkArea.Width - 40), Height = Math.Min(Math.Clamp(180 + snapshot.Length * 70, 300, 600), SystemParameters.WorkArea.Height - 60), Content = body };
    }
}


