using System.Windows;
using System.Windows.Controls;
using ACT.TTSYukkuri.Config.ViewModels;
using ACT.TTSYukkuri.resources;
using FFXIV.Framework.Bridge;
using FFXIV.Framework.Globalization;

namespace ACT.TTSYukkuri.Config.Views
{
    /// <summary>
    /// VoicevoxConfigView.xaml の相互作用ロジック
    /// </summary>
    public partial class VoicevoxConfigView : UserControl, ILocalizable
    {
        public VoicevoxConfigView(VoicePalettes voicePalette = VoicePalettes.Default)
        {
            InitializeComponent();
            this.DataContext = new VoicevoxConfigViewModel(voicePalette);

            this.SetLocale(Settings.Default.UILocale);
        }

        public VoicevoxConfigViewModel ViewModel => this.DataContext as VoicevoxConfigViewModel;

        public void SetLocale(Locales locale) => this.ReloadLocaleDictionary(locale);

        private void OnReloadClick(object sender, RoutedEventArgs e)
        {
            this.ViewModel?.ReloadSpeakers();
        }
    }
}
