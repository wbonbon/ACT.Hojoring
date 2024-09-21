using Advanced_Combat_Tracker;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace FFXIV.Framework.Common
{
    public static class CommonHelper
    {
        public static System.Windows.Media.Color ToMediaColor(this System.Drawing.Color dColor) => System.Windows.Media.Color.FromArgb(dColor.A, dColor.R, dColor.G, dColor.B);
        public static SolidColorBrush ToSolidColorBrush(this System.Windows.Media.Color mColor) => new SolidColorBrush(mColor);

        public static IEnumerable<T> FindChildren<T>(DependencyObject depObj) where T : DependencyObject
        {
            if (depObj == null) yield break;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                var child = VisualTreeHelper.GetChild(depObj, i);
                if (child is T tChild) yield return tChild;

                foreach (var childOfChild in FindChildren<T>(child))
                {
                    yield return childOfChild;
                }
            }
        }
        public static void Change_common_ctl_color(DependencyObject View)
        {
            var textboxs = CommonHelper.FindChildren<TextBox>(View);
            foreach (var c in textboxs)
            {
                c.Foreground = CommonHelper.ToSolidColorBrush(CommonHelper.ToMediaColor(ActGlobals.oFormActMain.ActColorSettings.MainWindowColors.ForeColorSetting));
                c.Background = CommonHelper.ToSolidColorBrush(CommonHelper.ToMediaColor(ActGlobals.oFormActMain.ActColorSettings.MainWindowColors.BackColorSetting));
            }

            var buttons = CommonHelper.FindChildren<Button>(View);
            foreach (var c in buttons)
            {
                c.Foreground = CommonHelper.ToSolidColorBrush(CommonHelper.ToMediaColor(ActGlobals.oFormActMain.ActColorSettings.MainWindowColors.ForeColorSetting));
                c.Background = CommonHelper.ToSolidColorBrush(CommonHelper.ToMediaColor(ActGlobals.oFormActMain.ActColorSettings.MainWindowColors.BackColorSetting));
            }

            var labels = CommonHelper.FindChildren<Label>(View);
            foreach (var c in labels)
            {
                c.Foreground = CommonHelper.ToSolidColorBrush(CommonHelper.ToMediaColor(ActGlobals.oFormActMain.ActColorSettings.MainWindowColors.ForeColorSetting));
                c.Background = CommonHelper.ToSolidColorBrush(CommonHelper.ToMediaColor(ActGlobals.oFormActMain.ActColorSettings.MainWindowColors.BackColorSetting));
            }

            var textblocks = CommonHelper.FindChildren<TextBlock>(View);
            foreach (var c in textblocks)
            {
                c.Foreground = CommonHelper.ToSolidColorBrush(CommonHelper.ToMediaColor(ActGlobals.oFormActMain.ActColorSettings.MainWindowColors.ForeColorSetting));
                c.Background = CommonHelper.ToSolidColorBrush(CommonHelper.ToMediaColor(ActGlobals.oFormActMain.ActColorSettings.MainWindowColors.BackColorSetting));
            }
            var tabitems = CommonHelper.FindChildren<TabItem>(View);
            foreach (var c in tabitems)
            {
                
                c.Foreground = CommonHelper.ToSolidColorBrush(CommonHelper.ToMediaColor(ActGlobals.oFormActMain.ActColorSettings.MainWindowColors.ForeColorSetting));
                c.Background = CommonHelper.ToSolidColorBrush(CommonHelper.ToMediaColor(ActGlobals.oFormActMain.ActColorSettings.MainWindowColors.BackColorSetting));
            }

            var comboboxs = CommonHelper.FindChildren<ComboBox>(View);
            foreach (var c in comboboxs)
            {
                c.Foreground = CommonHelper.ToSolidColorBrush(CommonHelper.ToMediaColor(ActGlobals.oFormActMain.ActColorSettings.MainWindowColors.ForeColorSetting));
                c.Background = CommonHelper.ToSolidColorBrush(CommonHelper.ToMediaColor(ActGlobals.oFormActMain.ActColorSettings.MainWindowColors.BackColorSetting));
            }

            var togglebuttons = CommonHelper.FindChildren<ToggleButton>(View);
            foreach (var c in togglebuttons)
            {
                c.Foreground = CommonHelper.ToSolidColorBrush(CommonHelper.ToMediaColor(ActGlobals.oFormActMain.ActColorSettings.MainWindowColors.ForeColorSetting));
                c.Background = CommonHelper.ToSolidColorBrush(CommonHelper.ToMediaColor(ActGlobals.oFormActMain.ActColorSettings.MainWindowColors.BackColorSetting));
            }

            var listboxs = CommonHelper.FindChildren<ListBox>(View);
            foreach (var c in listboxs)
            {
                c.Foreground = CommonHelper.ToSolidColorBrush(CommonHelper.ToMediaColor(ActGlobals.oFormActMain.ActColorSettings.MainWindowColors.ForeColorSetting));
                c.Background = CommonHelper.ToSolidColorBrush(CommonHelper.ToMediaColor(ActGlobals.oFormActMain.ActColorSettings.MainWindowColors.BackColorSetting));
            }

            var listviews = CommonHelper.FindChildren<ListView>(View);
            foreach (var c in listviews)
            {
                c.Foreground = CommonHelper.ToSolidColorBrush(CommonHelper.ToMediaColor(ActGlobals.oFormActMain.ActColorSettings.MainWindowColors.ForeColorSetting));
                c.Background = CommonHelper.ToSolidColorBrush(CommonHelper.ToMediaColor(ActGlobals.oFormActMain.ActColorSettings.MainWindowColors.BackColorSetting));
            }
        }

        private const double DefaultMin = 0.05;
        private const double DefaultMax = 1.00;

#if DEBUG
        public static bool IsDebugMode => true;
#else
        public static bool IsDebugMode => false;
#endif

        private static readonly Random random = new Random((int)DateTime.UtcNow.Ticks);

        public static Random Random => random;

        public static TimeSpan GetRandomTimeSpan() =>
            GetRandomTimeSpan(DefaultMin, DefaultMax);

        public static TimeSpan GetRandomTimeSpan(
            double maxSecounds = DefaultMax) =>
            GetRandomTimeSpan(DefaultMin, maxSecounds);

        public static TimeSpan GetRandomTimeSpan(
            double minSecounds = DefaultMin,
            double maxSecounds = DefaultMax) =>
            TimeSpan.FromMilliseconds(random.Next(
                (int)(minSecounds * 1000),
                (int)(maxSecounds * 1000)));

        public static T Clone<T>(this T source) where T : class
        {
            return typeof(T).InvokeMember(
              "MemberwiseClone",
              System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.InvokeMethod,
              null, source, null) as T;
        }

        public static dynamic Clone(this object source)
        {
            var t = source.GetType();
            return t.InvokeMember(
              "MemberwiseClone",
              System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.InvokeMethod,
              null, source, null);
        }

        public static void Walk<T>(this IEnumerable<T> e, Action<T> action)
        {
            foreach (var item in e)
            {
                action(item);
            }
        }

        public static async Task InvokeTasks(
            IEnumerable<Action> tasks)
        {
            var f = true;

            foreach (var task in tasks)
            {
                if (!f)
                {
                    await Task.Yield();
                }

                task.Invoke();
                f = false;
            }
        }
    }
}
