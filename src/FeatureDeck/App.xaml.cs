using FeatureDeck.Services;
using Microsoft.UI.Xaml;

namespace FeatureDeck
{
    public partial class App : Application
    {
        private Window _window;

        public App()
        {
            InitializeComponent();
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            // 启动时确定语言：默认跟随系统，不受支持时回退英文
            AppResources.InitializeLanguage();

            _window = new MainWindow();
            _window.Activate();
        }
    }
}
