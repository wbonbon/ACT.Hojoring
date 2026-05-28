using System;
using System.IO;
using NAudio.Wave;

namespace ACT.Hojoring.DiscordHelper
{
    /// <summary>
    /// 音声ファイルを読み込み、Discord推奨のオーディオフォーマット（48kHz, 16bit, 2ch ステレオ）に
    /// リサンプルして Discord のオーディオストリームへ送信するクラス。
    /// </summary>
    public static class AudioPipeline
    {
        /// <summary>
        /// Discord の規定オーディオフォーマット (48kHz, 16bit, 2ch 固定)
        /// </summary>
        private static readonly WaveFormat DiscordOutputFormat = new WaveFormat(48000, 16, 2);

        /// <summary>
        /// 指定された音声ファイルを読み込み、Discordフォーマットにリサンプルして送信します。
        /// </summary>
        /// <param name="filePath">音声ファイルの絶対パス</param>
        /// <param name="outputStream">Discord.Net の AudioOutStream</param>
        public static void SendAudio(string filePath, Stream outputStream)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("音声ファイルが存在しません。", filePath);
            }

            // NAudio を使用して音声ファイルをデコードし、48kHzステレオPCMにリサンプル
            using (var audio = new AudioFileReader(filePath))
            using (var resampler = new MediaFoundationResampler(audio, DiscordOutputFormat))
            {
                // リサンプラーの品質を設定 (60は一般的な高音質設定)
                resampler.ResamplerQuality = 60;

                // 1フレーム(20ms)あたりのバイト数
                // 48000Hz * 2 bytes/sample * 2 channels / 50 fps = 3840 bytes
                var blockSize = DiscordOutputFormat.AverageBytesPerSecond / 50;
                var buffer = new byte[blockSize];
                var byteCount = 0;

                // Discord.Net の AudioOutStream.Write は内部でバッファ制限による自動リアルタイムペース制御（ブロッキングバックプレッシャー）を行います。
                // そのため、手動のスリープやDelayを追加せずに、ストリームにそのまま同期書き込みします。
                while ((byteCount = resampler.Read(buffer, 0, blockSize)) > 0)
                {
                    if (byteCount < blockSize)
                    {
                        // ブロックサイズに足りない場合は、無音(0)でパディングする
                        Array.Clear(buffer, byteCount, blockSize - byteCount);
                    }

                    // Discordの音声ストリームへ同期で書き込む
                    outputStream.Write(buffer, 0, buffer.Length);
                }
            }
        }
    }
}
