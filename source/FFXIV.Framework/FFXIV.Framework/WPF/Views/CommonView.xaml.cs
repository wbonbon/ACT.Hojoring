using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
            if (!bOnece)
            {
                Func<DependencyObject, IEnumerable<TabItem>> f = null;
                f = d =>
                {
                    if (d is TabItem) { return new[] { (TabItem)d }; }
                    return Enumerable.Range(0, VisualTreeHelper.GetChildrenCount(d)).Select(i => f(VisualTreeHelper.GetChild(d, i))).SelectMany(a => a);
                };

                var list = f(this.view);

                foreach (TabItem t in list)
                {
                    t.Foreground = FFXIV.Framework.Common.CommonHelper.ToSolidColorBrush(FFXIV.Framework.Common.CommonHelper.ToMediaColor(Advanced_Combat_Tracker.ActGlobals.oFormActMain.ActColorSettings.MainWindowColors.ForeColorSetting));
                    t.Background = FFXIV.Framework.Common.CommonHelper.ToSolidColorBrush(FFXIV.Framework.Common.CommonHelper.ToMediaColor(Advanced_Combat_Tracker.ActGlobals.oFormActMain.ActColorSettings.MainWindowColors.BackColorSetting));
                }

                view.Foreground = FFXIV.Framework.Common.CommonHelper.ToSolidColorBrush(FFXIV.Framework.Common.CommonHelper.ToMediaColor(Advanced_Combat_Tracker.ActGlobals.oFormActMain.ActColorSettings.MainWindowColors.ForeColorSetting));
                view.Background = FFXIV.Framework.Common.CommonHelper.ToSolidColorBrush(FFXIV.Framework.Common.CommonHelper.ToMediaColor(Advanced_Combat_Tracker.ActGlobals.oFormActMain.ActColorSettings.MainWindowColors.BackColorSetting));
                bOnece = true;
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
