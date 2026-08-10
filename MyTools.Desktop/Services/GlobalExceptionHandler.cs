using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using MyTools.Common.DependencyInjection;
using MyTools.Desktop.Views;

namespace MyTools.Desktop.Services;

/// <summary>
/// 全局异常处理：统一捕获 Dispatcher / AppDomain / Task 未观察异常，
/// 记录日志并弹出 <see cref="ErrorDialog"/> 显示完整堆栈。
/// 也提供 <see cref="Report"/> 供业务代码主动上报（如 fire-and-forget 的 async catch）。
/// </summary>
public sealed class GlobalExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> logger;
    private int isShowingDialog;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    {
        this.logger = logger;
    }

    /// <summary>
    /// 注册全局异常钩子。应在应用启动时调用一次。
    /// </summary>
    public void Register()
    {
        var app = System.Windows.Application.Current;
        if (app != null)
        {
            app.DispatcherUnhandledException += OnDispatcherUnhandledException;
        }

        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    /// <summary>
    /// 主动上报一个异常：记录日志并弹出 ErrorDialog。
    /// 可在 async catch 块中调用（这些异常不会被全局钩子自动捕获）。
    /// </summary>
    public void Report(Exception ex, string context)
    {
        logger.LogError(ex, "{Context}", context);
        ShowDialog(ex, context);
    }

    private void OnDispatcherUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        logger.LogError(e.Exception, "Unhandled dispatcher exception");
        e.Handled = true;
        ShowDialog(e.Exception, "Unhandled Exception");
    }

    private void OnDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            logger.LogError(ex, "Unhandled domain exception (terminating={IsTerminating})", e.IsTerminating);
            ShowDialog(ex, "Fatal Exception");
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        logger.LogError(e.Exception, "Unobserved task exception");
        e.SetObserved();
        ShowDialog(e.Exception, "Unobserved Task Exception");
    }

    private void ShowDialog(Exception ex, string context)
    {
        // 防重入：如果正在显示弹窗，后续异常只记日志不弹窗，避免弹窗叠加。
        if (Interlocked.CompareExchange(ref isShowingDialog, 1, 0) != 0)
        {
            return;
        }

        var app = System.Windows.Application.Current;
        if (app == null)
        {
            isShowingDialog = 0;
            return;
        }

        try
        {
            if (app.Dispatcher.CheckAccess())
            {
                ErrorDialog.Show(ex, context);
            }
            else
            {
                app.Dispatcher.Invoke(() => ErrorDialog.Show(ex, context));
            }
        }
        catch (Exception dialogEx)
        {
            logger.LogError(dialogEx, "Failed to show ErrorDialog for original exception.");
        }
        finally
        {
            isShowingDialog = 0;
        }
    }

    /// <summary>
    /// 便捷入口：供无法注入实例的静态上下文使用（如 fire-and-forget 的 async void 方法）。
    /// </summary>
    public static void ReportStatic(Exception ex, string context)
    {
        try
        {
            var handler = ServiceLocator.GetRequiredService<GlobalExceptionHandler>();
            handler.Report(ex, context);
        }
        catch
        {
            // ServiceLocator 不可用时忽略，避免二次异常
        }
    }
}
