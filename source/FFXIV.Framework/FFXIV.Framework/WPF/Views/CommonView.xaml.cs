using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Advanced_Combat_Tracker;
using FFXIV.Framework.Common;
using FFXIV.Framework.Globalization;

namespace FFXIV.Framework.WPF.Views
{
    /// <summary>
    /// CommonView.xaml の相互作用ロジック
    /// </summary>
    public partial class CommonView : UserControl
    {
        private Locales UILocale => Config.Instance.UILocale;

        public CommonView()
        {
            this.InitializeComponent();

            View = this;
            // HelpViewを設定する
            this.HelpView.SetLocale(this.UILocale);
            this.HelpView.ViewModel.ReloadConfigAction = null;
        }

        private CommonView view;
        public CommonView View
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

        private bool SetProperty(ref CommonView view, CommonView value)
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
                    var tabitems = CommonHelper.FindChildren<TabItem>(this.View);
                    foreach (var c in tabitems)
                    {
                        c.Foreground = ActGlobals.oFormActMain.ActColorSettings.MainWindowColors.ForeColorSetting.ToMediaColor().ToSolidColorBrush();
                        c.Background = ActGlobals.oFormActMain.ActColorSettings.MainWindowColors.BackColorSetting.ToMediaColor().ToSolidColorBrush();
                    }
                    view.Foreground = ActGlobals.oFormActMain.ActColorSettings.MainWindowColors.ForeColorSetting.ToMediaColor().ToSolidColorBrush();
                    view.Background = ActGlobals.oFormActMain.ActColorSettings.MainWindowColors.BackColorSetting.ToMediaColor().ToSolidColorBrush();

                    bOnece = true;
                }
            }
        }

        private void SendAmazonGiftCard_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (this.UILocale == Locales.JA)
            {
                Process.Start(@"https://www.amazon.co.jp/dp/BT00DHI8G4");
            }
            else
            {
                Process.Start(@"https://www.amazon.com/dp/B004LLIKVU");
            }
        }

        private void CopyAddress_Click(
            object sender,
            RoutedEventArgs e)
        {
            Clipboard.SetData(
                DataFormats.Text,
                "anoyetta@gmail.com");

            CommonSounds.Instance.PlayAsterisk();
        }
    }
}
