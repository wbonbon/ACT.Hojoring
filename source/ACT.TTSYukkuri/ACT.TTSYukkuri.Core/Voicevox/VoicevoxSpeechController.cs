using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using ACT.TTSYukkuri.Config;
using FFXIV.Framework.Bridge;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using FFXIV.Framework.Common;

namespace ACT.TTSYukkuri.Voicevox
{
    /// <summary>
    /// VOICEVOX (Voicebox) 音声合成エンジンコントローラー
    /// </summary>
    public class VoicevoxSpeechController :
        ISpeechController
    {
        #region Logger

        private static readonly Logger Logger = AppLog.DefaultLogger;

        #endregion Logger

        private static readonly HttpClient HttpClient = new HttpClient();

        /// <summary>
        /// キャッシュされているスピーカー一覧
        /// </summary>
        public static List<VoicevoxSpeaker> Speakers { get; private set; } = new List<VoicevoxSpeaker>();

        /// <summary>
        /// 初期化する
        /// </summary>
        public void Initialize()
        {
            // 初期起動時にデフォルトアドレスからスピーカー一覧を取得してみる
            Task.Run(async () =>
            {
                try
                {
                    await LoadSpeakersAsync(Settings.Default.VoicevoxSettings.ApiUrl);
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, "VOICEVOX スピーカー一覧の初期取得に失敗しました。");
                }
            });
        }

        /// <summary>
        /// 解放する
        /// </summary>
        public void Free()
        {
        }

        /// <summary>
        /// VOICEVOX サーバーからスピーカー一覧を読み込む
        /// </summary>
        /// <param name="apiUrl">APIサーバーのアドレス</param>
        public static async Task LoadSpeakersAsync(string apiUrl)
        {
            if (string.IsNullOrWhiteSpace(apiUrl))
            {
                return;
            }

            var url = apiUrl.TrimEnd('/') + "/speakers";
            var response = await HttpClient.GetAsync(url).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var list = JsonConvert.DeserializeObject<List<VoicevoxSpeaker>>(json);
            if (list != null)
            {
                Speakers = list;
            }
        }

        /// <summary>
        /// テキストを読み上げる
        /// </summary>
        public void Speak(
            string text,
            PlayDevices playDevice = PlayDevices.Both,
            bool isSync = false,
            float? volume = null)
            => Speak(text, playDevice, VoicePalettes.Default, isSync, volume);

        /// <summary>
        /// テキストを読み上げる
        /// </summary>
        public void Speak(
            string text,
            PlayDevices playDevice = PlayDevices.Both,
            VoicePalettes voicePalette = VoicePalettes.Default,
            bool isSync = false,
            float? volume = null)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            VoicevoxConfig config;
            switch (voicePalette)
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

            // 現在の条件をハッシュ化してWAVEファイル名を作る
            var wave = this.GetCacheFileName(
                "Voicevox",
                text.Replace(Environment.NewLine, "+"),
                config.ToString(),
                false);

            this.CreateWaveWrapper(wave, () =>
            {
                this.CreateWave(
                    text,
                    wave,
                    config);
            });

            // 再生する
            SoundPlayerWrapper.Play(wave, playDevice, isSync, volume);
        }

        /// <summary>
        /// VOICEVOX API を呼び出して WAV 音声ファイルを生成する
        /// </summary>
        private void CreateWave(
            string textToSpeak,
            string wavePath,
            VoicevoxConfig config)
        {
            try
            {
                var baseUri = config.ApiUrl.TrimEnd('/');
                var speakerId = config.SpeakerId;

                // 1. audio_query を作成 (POST)
                var queryUrl = $"{baseUri}/audio_query?text={Uri.EscapeDataString(textToSpeak)}&speaker={speakerId}";
                var queryResponse = HttpClient.PostAsync(queryUrl, new StringContent("", Encoding.UTF8)).GetAwaiter().GetResult();
                queryResponse.EnsureSuccessStatusCode();

                var queryJson = queryResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                // パラメータを config に合わせて調整する
                var jObject = JObject.Parse(queryJson);
                jObject["volumeScale"] = config.VolumeScale;
                jObject["speedScale"] = config.SpeedScale;
                jObject["pitchScale"] = config.PitchScale;
                jObject["intonationScale"] = config.IntonationScale;

                var modifiedQueryJson = jObject.ToString(Formatting.None);

                // 2. synthesis を実行 (POST)
                var synthesisUrl = $"{baseUri}/synthesis?speaker={speakerId}";
                var content = new StringContent(modifiedQueryJson, Encoding.UTF8, "application/json");

                var synthesisResponse = HttpClient.PostAsync(synthesisUrl, content).GetAwaiter().GetResult();
                synthesisResponse.EnsureSuccessStatusCode();

                var audioBytes = synthesisResponse.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();

                // ファイルに書き出す
                using (var fs = new FileStream(wavePath, FileMode.Create, FileAccess.Write))
                {
                    fs.Write(audioBytes, 0, audioBytes.Length);
                    fs.Flush();
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"VOICEVOX での音声合成に失敗しました。テキスト: {textToSpeak}");
                throw;
            }
        }
    }

    /// <summary>
    /// VOICEVOX スピーカー情報
    /// </summary>
    public class VoicevoxSpeaker
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("speaker_uuid")]
        public string SpeakerUuid { get; set; }

        [JsonProperty("styles")]
        public List<VoicevoxStyle> Styles { get; set; }

        [JsonProperty("version")]
        public string Version { get; set; }
    }

    /// <summary>
    /// VOICEVOX スタイル情報
    /// </summary>
    public class VoicevoxStyle
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("id")]
        public int Id { get; set; }
    }
}
