using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using ControlzEx.Theming;

namespace EndfieldEssenceOverlay;

public partial class App : Application
{
    private static Mutex? _mutex;

    private static string LogPath =>
        Path.Combine(
            Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory,
            "error.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        // 단일 인스턴스 보장
        _mutex = new Mutex(true, "EndfieldEssenceOverlay_SingleInstance", out bool created);
        if (!created)
        {
            MessageBox.Show("이미 실행 중입니다.", "기질 오버레이", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        // 전역 예외 핸들러
        DispatcherUnhandledException += (_, ex) =>
        {
            LogException(ex.Exception);
            MessageBox.Show($"오류가 발생했습니다.\n{ex.Exception.Message}\n\n로그: {LogPath}",
                "기질 오버레이", MessageBoxButton.OK, MessageBoxImage.Error);
            ex.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
        {
            if (ex.ExceptionObject is Exception e2)
                LogException(e2);
        };
        TaskScheduler.UnobservedTaskException += (_, ex) =>
        {
            LogException(ex.Exception);
            ex.SetObserved();
        };

        base.OnStartup(e);

        // 5색 팔레트의 액센트(#F8F546)로 다크 테마 생성
        var accent = Color.FromRgb(0xF8, 0xF5, 0x46);
        var theme = RuntimeThemeGenerator.Current.GenerateRuntimeTheme("Dark", accent);
        if (theme != null)
        {
            ThemeManager.Current.AddTheme(theme);
            ThemeManager.Current.ChangeTheme(this, theme);
        }
    }

    private static void LogException(Exception ex)
    {
        try
        {
            var entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n";
            File.AppendAllText(LogPath, entry);
        }
        catch { /* 로그 기록 실패 시 무시 */ }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
