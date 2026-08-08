using System.Windows;
using DanmuFree.App.Services;
using DanmuFree.Core.Login;
namespace DanmuFree.App.Views;

public partial class LoginDialog : Window
{
    private readonly LoginService _login = new(new());
    private CancellationTokenSource? _cts;
    public string? Cookie { get; private set; }

    public LoginDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => _ = RunAsync();
    }

    private async Task RunAsync()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        try
        {
            RefreshBtn.Visibility = Visibility.Collapsed;
            StatusText.Text = "正在获取二维码…";
            var info = await _login.GenerateAsync(ct);
            QrImage.Source = QrImageRenderer.Render(info.Url);
            StatusText.Text = "请用手机 B站 app 扫码";
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(2000, ct);
                var st = await _login.PollAsync(info.QrcodeKey, ct);
                if (st.State == QrState.Success)
                {
                    var buvid = await _login.GetBuvid3Async(ct);
                    Cookie = string.IsNullOrEmpty(buvid) ? st.Cookie! : st.Cookie! + "; buvid3=" + buvid;
                    StatusText.Text = "登录成功！";
                    try { await Task.Delay(600, ct); } catch { }
                    Close();
                    return;
                }
                StatusText.Text = st.State switch
                {
                    QrState.Scanned => "已扫码，请在手机确认",
                    QrState.Expired => "二维码过期",
                    _ => "请用手机 B站 app 扫码",
                };
                if (st.State == QrState.Expired) { RefreshBtn.Visibility = Visibility.Visible; return; }
            }
        }
        catch (OperationCanceledException) { }
        catch { StatusText.Text = "网络错误，请刷新重试"; RefreshBtn.Visibility = Visibility.Visible; }
    }

    private void OnRefresh(object sender, RoutedEventArgs e) => _ = RunAsync();
    protected override void OnClosed(EventArgs e) { _cts?.Cancel(); base.OnClosed(e); }
}
