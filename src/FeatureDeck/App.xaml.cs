using FeatureDeck.Services;
using Microsoft.UI.Xaml;
using System;
using System.IO;
using System.Threading.Tasks;

namespace FeatureDeck
{
    public partial class App : Application
    {
        private Window _window;

        public App()
        {
            // 必须在任何资源加载之前设置语言：
            // unpackaged 应用 PrimaryLanguageOverride 不持久化，须每次启动重设，
            // 且要早于 InitializeComponent（XBF 资源）与 MainWindow 的 x:Uid 解析
            try
            {
                AppResources.InitializeLanguage();
            }
            catch (Exception ex)
            {
                LogCrash("InitLanguage", ex);
            }

            InitializeComponent();

            // 全局异常兜底：写崩溃日志便于诊断，避免静默闪退
            UnhandledException += OnUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                LogCrash("AppDomain", e.ExceptionObject as Exception);
            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                LogCrash("Task", e.Exception);
                e.SetObserved();
            };
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            _window = new MainWindow();
            _window.Activate();
        }

        private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            LogCrash("Xaml", e.Exception);
            // 不标记 Handled，让系统继续收集崩溃信息（保持既有行为）
        }

        internal static void LogCrash(string source, Exception ex)
        {
            if (ex == null) return;
            try
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "FeatureDeck");
                Directory.CreateDirectory(dir);
                File.AppendAllText(
                    Path.Combine(dir, "crash.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{source}] {ex}\n\n");
            }
            catch
            {
                // 日志失败不影响程序
            }
        }
    }
}
