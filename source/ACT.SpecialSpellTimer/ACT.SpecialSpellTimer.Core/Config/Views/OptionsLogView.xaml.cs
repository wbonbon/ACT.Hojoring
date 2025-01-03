using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Input;
using ACT.SpecialSpellTimer.resources;
using Advanced_Combat_Tracker;
using FFXIV.Framework.Common;
using FFXIV.Framework.Globalization;
using Prism.Commands;

namespace ACT.SpecialSpellTimer.Config.Views
{
    /// <summary>
    /// OptionsLogView.xaml の相互作用ロジック
    /// </summary>
    public partial class OptionsLogView :
        UserControl,
        ILocalizable
    {
        public OptionsLogView()
        {
            this.InitializeComponent();
            this.SetLocale(Settings.Default.UILocale);
            this.LoadConfigViewResources();
        }

        public void SetLocale(Locales locale) => this.ReloadLocaleDictionary(locale);

        public Settings Config => Settings.Default;

        public FFXIV.Framework.Config FrameworkConfig => FFXIV.Framework.Config.Instance;

        private System.Windows.Forms.FolderBrowserDialog dialog = new System.Windows.Forms.FolderBrowserDialog()
        {
            Description = "ログの保存先を選択してください。",
            ShowNewFolderButton = true,
        };

        private ICommand browseLogDirectoryCommand;

        public ICommand BrowseLogDirectoryCommand =>
            this.browseLogDirectoryCommand ?? (this.browseLogDirectoryCommand = new DelegateCommand(() =>
            {
                this.dialog.SelectedPath = this.Config.SaveLogDirectory;
                if (this.dialog.ShowDialog(ActGlobals.oFormActMain) ==
                    System.Windows.Forms.DialogResult.OK)
                {
                    this.Config.SaveLogDirectory = this.dialog.SelectedPath;
                }
            }));

        private ICommand openLogCommand;

        public ICommand OpenLogCommand =>
            this.openLogCommand ?? (this.openLogCommand = new DelegateCommand(async () =>
            {
                var file = ParsedLogWorker.Instance.OutputFile;
                if (File.Exists(file))
                {
                    await Task.Run(() => ParsedLogWorker.Instance.Flush(true));
                    Process.Start(file);
                }
            }));

        private DelegateCommand flushLogCommand;

        public DelegateCommand FlushLogCommand =>
            this.flushLogCommand ?? (this.flushLogCommand = new DelegateCommand(this.ExecuteFlushLogCommand));

        private async void ExecuteFlushLogCommand()
        {
            await Task.Run(() =>
            {
                ParsedLogWorker.Instance.Flush(true);
                CommonSounds.Instance.PlayAsterisk();
            });
        }
    }
}
