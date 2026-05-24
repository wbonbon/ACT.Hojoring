using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using ACT.TTSYukkuri.Voicevox;
using FFXIV.Framework.Bridge;
using Prism.Mvvm;

namespace ACT.TTSYukkuri.Config.ViewModels
{
    /// <summary>
    /// VOICEVOX 設定用 ViewModel
    /// </summary>
    public class VoicevoxConfigViewModel : BindableBase
    {
        public VoicePalettes VoicePalette { get; set; }

        private ObservableCollection<VoicevoxSpeakerComboItem> speakers = new ObservableCollection<VoicevoxSpeakerComboItem>();

        /// <summary>
        /// ComboBox 選択用のスピーカー一覧
        /// </summary>
        public ObservableCollection<VoicevoxSpeakerComboItem> Speakers
        {
            get => this.speakers;
            set => this.SetProperty(ref this.speakers, value);
        }

        public VoicevoxConfigViewModel(VoicePalettes voicePalette = VoicePalettes.Default)
        {
            this.VoicePalette = voicePalette;

            // スピーカーリストの読み込み
            ReloadSpeakers();

            // Config の変更を検知して ApiUrl が変わったらスピーカーリストを更新する
            this.Config.PropertyChanged += this.OnConfigPropertyChanged;
        }

        public VoicevoxConfig Config
        {
            get
            {
                VoicevoxConfig config;
                switch (VoicePalette)
                {
                    case VoicePalettes.Default:
                        config = Settings.Default.VoicevoxSettings;
                        break;
                    case VoicePalettes.Ext1:
                        config = Settings.Default.VoicevoxSettingsExt1;
                        break;
                    case VoicePalettes.Ext2:
                        config = Settings.Default.VoicevoxSettingsExt2;
                        break;
                    case VoicePalettes.Ext3:
                        config = Settings.Default.VoicevoxSettingsExt3;
                        break;
                    default:
                        config = Settings.Default.VoicevoxSettings;
                        break;
                }
                return config;
            }
        }

        private void OnConfigPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(VoicevoxConfig.ApiUrl))
            {
                ReloadSpeakers();
            }
        }

        /// <summary>
        /// スピーカー一覧を再ロードする
        /// </summary>
        public async void ReloadSpeakers()
        {
            try
            {
                var apiUrl = this.Config.ApiUrl;
                if (string.IsNullOrWhiteSpace(apiUrl))
                {
                    return;
                }

                await VoicevoxSpeechController.LoadSpeakersAsync(apiUrl);

                var items = new List<VoicevoxSpeakerComboItem>();
                foreach (var speaker in VoicevoxSpeechController.Speakers)
                {
                    foreach (var style in speaker.Styles)
                    {
                        items.Add(new VoicevoxSpeakerComboItem
                        {
                            DisplayName = $"{speaker.Name} ({style.Name})",
                            Id = style.Id
                        });
                    }
                }

                // UIスレッドで安全にコレクションを更新する
                Application.Current.Dispatcher.Invoke(() =>
                {
                    this.Speakers.Clear();
                    foreach (var item in items)
                    {
                        this.Speakers.Add(item);
                    }
                });
            }
            catch (Exception)
            {
                // VOICEVOX が起動していない等で取得できない場合は無視する
            }
        }
    }

    /// <summary>
    /// UI の ComboBox で表示するスピーカー用アイテム
    /// </summary>
    public class VoicevoxSpeakerComboItem
    {
        public string DisplayName { get; set; }
        public int Id { get; set; }
    }
}
