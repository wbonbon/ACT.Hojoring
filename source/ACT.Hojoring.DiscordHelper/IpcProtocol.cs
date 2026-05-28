using System.Collections.Generic;

namespace ACT.Hojoring.DiscordHelper
{
    /// <summary>
    /// プラグインとヘルパーアプリ間で Named Pipe を通じて送受信される JSON メッセージのデータ構造。
    /// 単一のコンテナクラスに統合することで、シリアライズ/デシリアライズのロジックを極めてシンプルに保ちます。
    /// </summary>
    public class IpcMessage
    {
        // 共通フィールド
        /// <summary>
        /// メッセージの種別。
        /// コマンド: "connect", "disconnect", "get_channels", "join_voice", "leave_voice", "play_audio", "send_message"
        /// イベント: "status_changed", "channels_updated", "log", "error"
        /// </summary>
        public string type { get; set; } = string.Empty;

        // プラグイン -> ヘルパー (コマンド用フィールド)
        public string? token { get; set; }
        public string? channelId { get; set; }
        public string? text { get; set; }
        public bool? tts { get; set; }
        public string? filePath { get; set; }

        // ヘルパー -> プラグイン (イベント/レスポンス用フィールド)
        public bool? connected { get; set; }
        public bool? joinedVoice { get; set; }
        public string? message { get; set; }

        // チャンネル情報受信用フィールド
        public List<string>? guilds { get; set; }
        public List<DiscordChannelInfo>? textChannels { get; set; }
        public List<DiscordChannelInfo>? voiceChannels { get; set; }
    }

    /// <summary>
    /// Discordのチャンネル情報を表現するクラス。
    /// </summary>
    public class DiscordChannelInfo
    {
        public string id { get; set; } = string.Empty;
        public string name { get; set; } = string.Empty;
        public string serverName { get; set; } = string.Empty;
    }
}
