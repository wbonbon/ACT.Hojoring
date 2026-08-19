using ACT.TTSYukkuri.Config;
using ACT.TTSYukkuri.SAPI5;
using Amazon.Polly;
using Amazon.Polly.Model;
using Amazon.Runtime;
using Amazon.Runtime.CredentialManagement;
using FFXIV.Framework.Bridge;
using System;
using System.IO;
using System.Linq;

namespace ACT.TTSYukkuri.Polly
{
    public class PollySpeechController :
        ISpeechController
    {
        /// <summary>
        /// 初期化する
        /// </summary>
        public void Initialize()
        {
        }

        public void Free()
        {
        }

        /// <summary>
        /// テキストを読み上げる
        /// </summary>
        /// <param name="text">読み上げるテキスト</param>
        public void Speak(
            string text,
            PlayDevices playDevice = PlayDevices.Both,
            bool isSync = false,
            float? volume = null)
            => Speak(text, playDevice, VoicePalettes.Default, isSync, volume);

        /// <summary>
        /// テキストを読み上げる
        /// </summary>
        /// <param name="text">読み上げるテキスト</param>
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
            PollyConfigs config;
            switch (voicePalette)
            {
                case VoicePalettes.Default:
                    config = Settings.Default.PollySettings;
                    break;
                case VoicePalettes.Ext1:
                    config = Settings.Default.PollySettingsExt1;
                    break;
                case VoicePalettes.Ext2:
                    config = Settings.Default.PollySettingsExt2;
                    break;
                case VoicePalettes.Ext3:
                    config = Settings.Default.PollySettingsExt3;
                    break;
                default:
                    config = Settings.Default.PollySettingsExt1;
                    break;
            }

            // 現在の条件をハッシュ化してWAVEファイル名を作る
            var wave = this.GetCacheFileName(
                Settings.Default.TTS,
                text.Replace(Environment.NewLine, "+"),
                config.ToString(),
                true);

            this.CreateWaveWrapper(wave, () =>
            {
                this.CreateWave(
                    text,
                    wave);
            });

            // 再生する
            SoundPlayerWrapper.Play(wave, playDevice, isSync, volume);
        }

        /// <summary>
        /// WAVEファイルを生成する
        /// </summary>
        /// <param name="textToSpeak">
        /// Text to Speak</param>
        /// <param name="wave">
        /// WAVEファイルのパス</param>
        private void CreateWave(
            string textToSpeak,
            string wave)
        {
            var config = Settings.Default.PollySettings;
            var endpoint = config.Endpoint;
            var chain = new CredentialProfileStoreChain();

            var hash = (config.Region + config.AccessKey + config.SecretKey).GetHashCode().ToString("X4");
            var profileName = $"polly_profile_{hash}";

            AWSCredentials awsCredentials;
            if (!chain.TryGetAWSCredentials(
                profileName,
                out awsCredentials))
            {
                var options = new CredentialProfileOptions
                {
                    AccessKey = config.AccessKey,
                    SecretKey = config.SecretKey,
                };

                var profile = new CredentialProfile(profileName, options);
                profile.Region = endpoint;

                chain.RegisterProfile(profile);

                chain.TryGetAWSCredentials(
                    profileName,
                    out awsCredentials);
            }

            if (awsCredentials == null)
            {
                return;
            }

            using (var pc = new AmazonPollyClient(
                awsCredentials,
                endpoint))
            {
                var req = new SynthesizeSpeechRequest();
                req.OutputFormat = OutputFormat.Mp3;
                req.VoiceId = config.Voice;

                var selectedVoice = Settings.Default.PollyVoices.FirstOrDefault(x => x.Value == config.Voice);
                if (selectedVoice != null && !string.IsNullOrEmpty(selectedVoice.SupportedEngines))
                {
                    if (selectedVoice.SupportedEngines.Contains("generative"))
                    {
                        req.Engine = Engine.FindValue("generative");
                    }
                    else if (selectedVoice.SupportedEngines.Contains("long-form"))
                    {
                        req.Engine = Engine.FindValue("long-form");
                    }
                    else if (selectedVoice.SupportedEngines.Contains("neural"))
                    {
                        req.Engine = Engine.Neural;
                    }
                    else
                    {
                        req.Engine = Engine.Standard;
                    }
                }
                else
                {
                    req.Engine = Engine.Standard;
                }

                // Amazon Polly Engine limitations for SSML:
                var ssml = string.Empty;
                if (req.Engine == Engine.FindValue("generative"))
                {
                    // Generative does not support <prosody> tag
                    ssml = $@"<speak>{textToSpeak}</speak>";
                }
                else if (req.Engine == Engine.Neural || req.Engine == Engine.FindValue("long-form"))
                {
                    // Neural and LongForm do not support 'pitch' attribute in <prosody>
                    ssml = $@"<speak><prosody volume=""{config.Volume.ToXML()}"" rate=""{config.Rate.ToXML()}"">{textToSpeak}</prosody></speak>";
                }
                else
                {
                    ssml = $@"<speak><prosody volume=""{config.Volume.ToXML()}"" rate=""{config.Rate.ToXML()}"" pitch=""{config.Pitch.ToXML()}"">{textToSpeak}</prosody></speak>";
                }

                req.TextType = TextType.Ssml;
                req.Text = ssml;

                var res = pc.SynthesizeSpeech(req);

                using (var fs = new FileStream(wave, FileMode.Create, FileAccess.Write))
                {
                    res.AudioStream.CopyTo(fs);
                    fs.Flush();
                    fs.Close();
                }
            }
        }
    }
}
