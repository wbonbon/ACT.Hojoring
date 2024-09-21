using System.Windows;
using System.Windows.Controls;
using ACT.TTSYukkuri.resources;
using Advanced_Combat_Tracker;
using FFXIV.Framework.Common;
using FFXIV.Framework.Globalization;

namespace ACT.TTSYukkuri.Config.Views
{
    /// <summary>
    /// ConfigBaseView.xaml の相互作用ロジック
    /// </summary>
    public partial class ConfigBaseView : UserControl, ILocalizable
    {
        public static ConfigBaseView Instance { get; private set; }

        public ConfigBaseView()
        {
            Instance = this;
            this.InitializeComponent();
            View = this;
            this.SetLocale(Settings.Default.UILocale);
        }

        private ConfigBaseView view;
        public ConfigBaseView View
        {
            get => this.view;
            set
            {
                var old = this.view;

                if (this.SetProperty(ref this.view, value))
                {
                    if (old != null)
                    {
                        old.Loaded -= this.View_Loaded;
                    }

                    this.view.Loaded += this.View_Loaded;
                }
            }
        }
        private bool SetProperty(ref ConfigBaseView view, ConfigBaseView value)
        {
            if (this.view == value) { return false; }
            this.view = value;
            return true;
        }

        bool bOnece = false;
        private void View_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            if (ActGlobals.oFormActMain.ActColorSettings.InvertColors)
            {
                if (!bOnece)
                {
                    //CommonHelper.Change_common_ctl_color(this.View);
                    var tabitems = CommonHelper.FindChildren<TabItem>(this.View);
                    foreach (var c in tabitems)
                    {
                        c.Foreground = CommonHelper.ToSolidColorBrush(CommonHelper.ToMediaColor(ActGlobals.oFormActMain.ActColorSettings.MainWindowColors.ForeColorSetting));
                        c.Background = CommonHelper.ToSolidColorBrush(CommonHelper.ToMediaColor(ActGlobals.oFormActMain.ActColorSettings.MainWindowColors.BackColorSetting));
                    }
                    view.Foreground = CommonHelper.ToSolidColorBrush(CommonHelper.ToMediaColor(ActGlobals.oFormActMain.ActColorSettings.MainWindowColors.ForeColorSetting));
                    view.Background = CommonHelper.ToSolidColorBrush(CommonHelper.ToMediaColor(ActGlobals.oFormActMain.ActColorSettings.MainWindowColors.BackColorSetting));
                    bOnece = true;
                }
            }
        }
        public void SetLocale(Locales locale) => this.ReloadLocaleDictionary(locale);

        public void SetActivationStatus(
            bool isAllow)
            => this.DenyMessageLabel.Visibility = isAllow ?
                System.Windows.Visibility.Collapsed :
                System.Windows.Visibility.Visible;
    }
}
