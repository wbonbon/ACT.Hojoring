using System;
using Prism.Mvvm;

namespace ACT.TTSYukkuri.Config
{
    /// <summary>
    /// VOICEVOX (Voicebox) 設定
    /// </summary>
    [Serializable]
    public class VoicevoxConfig :
        BindableBase
    {
        private string apiUrl = "http://localhost:50021/";
        private int speakerId = 2; // デフォルト：四国めたん（あまあま）
        private double volumeScale = 1.0;
        private double speedScale = 1.0;
        private double pitchScale = 0.0;
        private double intonationScale = 1.0;

        public void SetDefault()
        {
            var defaultConfig = new VoicevoxConfig();
            this.ApiUrl = defaultConfig.ApiUrl;
            this.SpeakerId = defaultConfig.SpeakerId;
            this.VolumeScale = defaultConfig.VolumeScale;
            this.SpeedScale = defaultConfig.SpeedScale;
            this.PitchScale = defaultConfig.PitchScale;
            this.IntonationScale = defaultConfig.IntonationScale;
        }

        /// <summary>
        /// APIサーバーのアドレス
        /// </summary>
        public string ApiUrl
        {
            get => this.apiUrl;
            set => this.SetProperty(ref this.apiUrl, value);
        }

        /// <summary>
        /// スピーカー (スタイル) ID
        /// </summary>
        public int SpeakerId
        {
            get => this.speakerId;
            set => this.SetProperty(ref this.speakerId, value);
        }

        /// <summary>
        /// 音量スケール (0.0 〜 2.0)
        /// </summary>
        public double VolumeScale
        {
            get => this.volumeScale;
            set => this.SetProperty(ref this.volumeScale, value);
        }

        /// <summary>
        /// 話速スケール (0.5 〜 2.0)
        /// </summary>
        public double SpeedScale
        {
            get => this.speedScale;
            set => this.SetProperty(ref this.speedScale, value);
        }

        /// <summary>
        /// 音高スケール (-0.15 〜 0.15)
        /// </summary>
        public double PitchScale
        {
            get => this.pitchScale;
            set => this.SetProperty(ref this.pitchScale, value);
        }

        /// <summary>
        /// 抑揚スケール (0.0 〜 2.0)
        /// </summary>
        public double IntonationScale
        {
            get => this.intonationScale;
            set => this.SetProperty(ref this.intonationScale, value);
        }

        public override string ToString() =>
            $"{nameof(this.SpeakerId)}:{this.SpeakerId}," +
            $"{nameof(this.VolumeScale)}:{this.VolumeScale}," +
            $"{nameof(this.SpeedScale)}:{this.SpeedScale}," +
            $"{nameof(this.PitchScale)}:{this.PitchScale}," +
            $"{nameof(this.IntonationScale)}:{this.IntonationScale}";
    }
}
