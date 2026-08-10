using System.Windows;
using MyTools.Desktop.Utils;

namespace MyTools.Desktop.Views;

/// <summary>
/// 展示未处理异常的完整堆栈。顶部为上下文标题与错误消息，中部为可滚动的堆栈文本，
/// 底部提供复制与确定按钮。线程安全：可在任意线程调用 <see cref="Show"/>，
/// 内部自动切换到 Dispatcher 线程并选取最合适的 owner 窗口。
/// </summary>
public partial class ErrorDialog
{
    private ErrorDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 显示一个包含完整异常堆栈的错误弹窗。
    /// </summary>
    /// <param name="ex">异常对象。</param>
    /// <param name="context">产生异常的场景描述，用作弹窗标题。</param>
    public static void Show(Exception ex, string context)
    {
        var application = Application.Current;
        if (application == null)
        {
            return;
        }

        if (!application.Dispatcher.CheckAccess())
        {
            application.Dispatcher.Invoke(() => Show(ex, context));
            return;
        }

        var dialog = new ErrorDialog();
        dialog.ContextText.Text = string.IsNullOrWhiteSpace(context) ? "Error" : context;
        dialog.MessageText.Text = ex.Message;
        dialog.StackTraceText.Text = ex.ToString();

        var owner = application.Windows
            .OfType<Window>()
            .Where(window => window.IsVisible && window.WindowState != WindowState.Minimized)
            .OrderByDescending(window => window.IsActive)
            .ThenByDescending(window => window.Topmost)
            .FirstOrDefault();

        Window? temporaryOwner = null;
        if (owner == null)
        {
            temporaryOwner = TopmostMessageBox.CreateTemporaryOwner();
            owner = temporaryOwner;
            owner.Show();
        }

        try
        {
            owner.Activate();
            dialog.Owner = owner;
            dialog.ShowDialog();
        }
        finally
        {
            temporaryOwner?.Close();
        }
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(StackTraceText.Text);
            CopyButton.Content = "Copied!";
            Task.Delay(1500).ContinueWith(_ =>
            {
                Dispatcher.Invoke(() => CopyButton.Content = "Copy");
            });
        }
        catch
        {
            // 剪贴板被占用等情况，忽略
        }
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
