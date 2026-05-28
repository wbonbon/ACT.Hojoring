using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ACT.TTSYukkuri.Config;
using FFXIV.Framework.Bridge;
using FFXIV.Framework.Common;
using Newtonsoft.Json;
using NLog;
using Prism.Mvvm;

namespace ACT.TTSYukkuri.Discord.Models
{
    /// <summary>
    /// Discord.Net を直接使用する代わりに、別プロセスの .NET 8 ヘルパーアプリへ Named Pipe IPC 経由で
    /// コマンドを中継するクライアント実装。既存の IDiscordClientModel および WPF バインディングと完全互換。
    /// </summary>
    public class DiscordNetModel :
        BindableBase,
        IDiscordClientModel
    {
        #region Singleton

        private static DiscordNetModel instance;

        public static DiscordNetModel Instance =>
            instance ?? (instance = new DiscordNetModel());

        private DiscordNetModel()
        {
        }

        #endregion Singleton

        #region Logger

        private Logger AppLogger => AppLog.DefaultLogger;

        private readonly StringBuilder log = new StringBuilder();

        public string Log => this.log.ToString();

        private void AppendLogLine(
            string message,
            Exception ex = null,
            bool err = false)
        {
            // UIに出力する
            var text = $"[{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff")}] {message}";
            if (ex != null)
            {
                text += Environment.NewLine + ex.ToString();
            }

            this.log.AppendLine(text);
            WPFHelper.BeginInvoke(() => this.RaisePropertyChanged(nameof(this.Log)));

            // NLogに出力する
            var logText = $"[DISCORD] {message}";
            if (ex == null && !err)
            {
                this.AppLogger.Trace(logText);
            }
            else
            {
                this.AppLogger.Error(ex, logText);
            }
        }

        #endregion Logger

        #region IPC & Helper Process Fields

        private Process helperProcess;
        private NamedPipeClientStream pipeClient;
        private StreamWriter pipeWriter;
        private CancellationTokenSource cts;
        private readonly object sendLock = new object();
        private bool isConnecting = false;

        #endregion

        #region IDiscordClientModel Properties

        private bool connected;

        public bool IsConnected
        {
            get => this.connected;
            set => this.SetProperty(ref this.connected, value);
        }

        private bool joinedVoiceChannel;

        public bool IsJoinedVoiceChannel
        {
            get => this.joinedVoiceChannel;
            set => this.SetProperty(ref this.joinedVoiceChannel, value);
        }

        private string previousAvailableTextChannelID;
        private string previousAvailableVoiceChannelID;

        private DiscordChannelContainer selectedTextChannel;

        public DiscordChannelContainer SelectedTextChannel
        {
            get => this.selectedTextChannel;
            set
            {
                this.selectedTextChannel = value;

                var id = (value?.ID ?? 0).ToString();
                if (id == "0" || id == "-1")
                {
                    this.previousAvailableTextChannelID = this.Config.DefaultTextChannelID;
                }

                this.Config.DefaultTextChannelID = id;
                this.RaisePropertyChanged();
            }
        }

        private DiscordChannelContainer selectedVoiceChannel;

        public DiscordChannelContainer SelectedVoiceChannel
        {
            get => this.selectedVoiceChannel;
            set
            {
                this.selectedVoiceChannel = value;

                var id = (value?.ID ?? 0).ToString();
                if (id == "0")
                {
                    this.previousAvailableVoiceChannelID = this.Config.DefaultVoiceChannelID;
                }

                this.Config.DefaultVoiceChannelID = id;
                this.RaisePropertyChanged();
            }
        }

        // guilds データを保持するための内部リスト
        private readonly List<string> guilds = new List<string>();

        public string[] AvailableGuilds => this.guilds
            .OrderBy(x => x)
            .ToArray();

        public string AvailableGuildsText => string.Join(
            Environment.NewLine,
            this.guilds);

        private readonly ObservableCollection<DiscordChannelContainer> channels = new ObservableCollection<DiscordChannelContainer>();
        private readonly ObservableCollection<DiscordChannelContainer> textChannels = new ObservableCollection<DiscordChannelContainer>();
        private readonly ObservableCollection<DiscordChannelContainer> voiceChannels = new ObservableCollection<DiscordChannelContainer>();

        public ObservableCollection<DiscordChannelContainer> Channels => this.channels;
        public ObservableCollection<DiscordChannelContainer> TextChannels => this.textChannels;
        public ObservableCollection<DiscordChannelContainer> VoiceChannels => this.voiceChannels;

        #endregion IDiscordClientModel Properties

        #region IDiscordClientModel Methods

        public void Initialize()
        {
            // Bridgeにデリゲートを登録する
            DiscordBridge.Instance.SendMessageDelegate = this.SendMessage;
            DiscordBridge.Instance.SendSpeakingDelegate = this.Play;
        }

        public void Dispose()
        {
            // Bridgeのデリゲートを解除する
            DiscordBridge.Instance.SendMessageDelegate = null;
            DiscordBridge.Instance.SendSpeakingDelegate = null;

            this.Disconnect();
        }

        public void ClearQueue()
        {
            this.SendIpcMessage(new IpcMessage { type = "clear_queue" });
        }

        public void Connect(bool isInitialize = false)
        {
            if (this.isConnecting || this.IsConnected) return;

            this.isConnecting = true;
            this.AppendLogLine("Initializing Discord Helper connection...");

            // UIスレッドをブロックしないよう、完全にバックグラウンドで実行
            _ = Task.Run(() =>
            {
                try
                {
                    // 1. ヘルパープロセスの起動確認と開始
                    this.StartHelperProcess();

                    // 2. Named Pipe への接続
                    this.ConnectPipe();

                    // 3. ボットの接続コマンドをヘルパーに送信
                    if (!string.IsNullOrEmpty(this.Config.Token))
                    {
                        this.SendIpcMessage(new IpcMessage
                        {
                            type = "connect",
                            token = this.Config.Token
                        });
                    }
                    else
                    {
                        this.AppendLogLine("Discord Bot Token is empty. Please set your token in settings.");
                    }
                }
                catch (Exception ex)
                {
                    this.AppendLogLine("Failed to connect to Discord Helper.", ex, true);
                    this.Disconnect();
                }
                finally
                {
                    this.isConnecting = false;
                }
            });
        }

        public void Disconnect()
        {
            this.AppendLogLine("Disconnecting from Discord...");

            // ヘルパーに同期的に切断コマンドを送る
            this.SendIpcMessageSync(new IpcMessage { type = "disconnect" });

            // Pipe とプロセスの破棄
            this.CleanupIpc();
            this.StopHelperProcess();

            WPFHelper.BeginInvoke(() =>
            {
                this.IsConnected = false;
                this.IsJoinedVoiceChannel = false;
                this.guilds.Clear();
                this.channels.Clear();
                this.textChannels.Clear();
                this.voiceChannels.Clear();
            });
        }

        public void JoinVoiceChannel()
        {
            if (!this.IsConnected) return;

            var id = this.SelectedVoiceChannel?.ID?.ToString();
            if (string.IsNullOrEmpty(id) || id == "0") return;

            this.SendIpcMessage(new IpcMessage
            {
                type = "join_voice",
                channelId = id
            });
        }

        public void LeaveVoiceChannel()
        {
            if (!this.IsConnected) return;

            this.SendIpcMessage(new IpcMessage
            {
                type = "leave_voice"
            });
        }

        public void Play(string audioFile)
        {
            if (!File.Exists(audioFile))
            {
                this.AppendLogLine($"Play Sound Error. File not found. {audioFile}");
                return;
            }

            // 音声再生コマンドを送信 (WAVファイルのストリーミング・再生キュー処理はヘルパーが行う)
            this.SendIpcMessage(new IpcMessage
            {
                type = "play_audio",
                filePath = audioFile
            });
        }

        public void SendMessage(string message, bool tts = false)
        {
            var id = this.SelectedTextChannel?.ID?.ToString();
            if (string.IsNullOrEmpty(id) || id == "0" || id == "-1") return;

            this.SendIpcMessage(new IpcMessage
            {
                type = "send_message",
                channelId = id,
                text = message,
                tts = tts
            });
        }

        #endregion IDiscordClientModel Methods

        #region Helper Process Control

        private void StartHelperProcess()
        {
            if (this.helperProcess != null && !this.helperProcess.HasExited)
            {
                return;
            }

            var helperPath = Path.Combine(
                PluginCore.Instance.PluginDirectory,
                "bin",
                "discord",
                "ACT.Hojoring.DiscordHelper.exe");

            if (!File.Exists(helperPath))
            {
                throw new FileNotFoundException("Discord Helper executable not found.", helperPath);
            }

            this.AppendLogLine("Starting Discord Helper process...");

            var psi = new ProcessStartInfo
            {
                FileName = helperPath,
                Arguments = $"--parent-pid {Process.GetCurrentProcess().Id}",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            this.helperProcess = new Process { StartInfo = psi };
            this.helperProcess.EnableRaisingEvents = true;
            this.helperProcess.Exited += this.HelperProcess_Exited;

            if (this.helperProcess.Start())
            {
                // バックグラウンドでコンソール出力を読み取り
                _ = Task.Run(() => this.ReadProcessOutputAsync(this.helperProcess.StandardOutput));
                _ = Task.Run(() => this.ReadProcessOutputAsync(this.helperProcess.StandardError));
            }
            else
            {
                throw new InvalidOperationException("Failed to start Discord Helper process.");
            }
        }

        private void StopHelperProcess()
        {
            var process = Interlocked.Exchange(ref this.helperProcess, null);
            if (process == null) return;

            try
            {
                process.Exited -= this.HelperProcess_Exited;
                if (!process.HasExited)
                {
                    this.AppendLogLine("Waiting for Discord Helper process to exit gracefully...");
                    if (!process.WaitForExit(1000))
                    {
                        this.AppendLogLine("Discord Helper did not exit in time. Killing process...");
                        process.Kill();
                    }
                }
            }
            catch (Exception ex)
            {
                this.AppLogger.Error(ex, "Failed to stop helper process.");
            }
            finally
            {
                process.Dispose();
            }
        }

        private void HelperProcess_Exited(object sender, EventArgs e)
        {
            this.AppendLogLine("Discord Helper process exited.");
            
            // 意図しない切断の場合、状態をリセット
            if (this.IsConnected || this.IsJoinedVoiceChannel)
            {
                this.Disconnect();
            }
        }

        private async Task ReadProcessOutputAsync(StreamReader reader)
        {
            try
            {
                while (!reader.EndOfStream)
                {
                    var line = await reader.ReadLineAsync();
                    if (line != null)
                    {
                        // 標準出力のログはデバッグ追跡用
                        this.AppLogger.Trace($"[Helper Console] {line}");
                    }
                }
            }
            catch (Exception)
            {
                // プロセス終了時などにストリーム切断で例外が発生する可能性があるため無視
            }
        }

        #endregion Helper Process Control

        #region Named Pipe Communication

        private void ConnectPipe()
        {
            this.CleanupIpc();

            this.pipeClient = new NamedPipeClientStream(
                ".",
                "ACTHojoringDiscord",
                PipeDirection.InOut,
                PipeOptions.Asynchronous);

            this.AppendLogLine("Connecting to Named Pipe...");
            
            // 最大5秒間、ヘルパーがNamed Pipeサーバーを立ち上げるのを待つ
            this.pipeClient.Connect(5000);

            this.pipeWriter = new StreamWriter(this.pipeClient, new UTF8Encoding(false)) { AutoFlush = true };
            this.cts = new CancellationTokenSource();

            // 受信ループを非同期で開始
            _ = Task.Run(() => this.ReceiveLoopAsync(this.cts.Token));
            this.AppendLogLine("Named Pipe connection established.");
        }

        private void CleanupIpc()
        {
            if (this.cts != null)
            {
                this.cts.Cancel();
                this.cts.Dispose();
                this.cts = null;
            }

            lock (this.sendLock)
            {
                if (this.pipeWriter != null)
                {
                    this.pipeWriter.Dispose();
                    this.pipeWriter = null;
                }
            }

            if (this.pipeClient != null)
            {
                this.pipeClient.Dispose();
                this.pipeClient = null;
            }
        }

        /// <summary>
        /// ヘルパープロセスへ JSON メッセージを同期的に送信します（スレッドセーフ）。
        /// </summary>
        private void SendIpcMessageSync(IpcMessage msg)
        {
            lock (this.sendLock)
            {
                if (this.pipeWriter == null || this.pipeClient == null || !this.pipeClient.IsConnected)
                {
                    return;
                }

                try
                {
                    var json = JsonConvert.SerializeObject(msg);
                    this.pipeWriter.WriteLine(json);
                    this.pipeWriter.Flush();
                }
                catch (Exception ex)
                {
                    // 切断処理中またはパイプ破棄時の例外は正常系のため無視する
                    if (ex is OperationCanceledException || ex is ObjectDisposedException || ex.InnerException is ObjectDisposedException)
                    {
                        return;
                    }
                    this.AppendLogLine("Error sending sync message to helper.", ex, true);
                }
            }
        }

        /// <summary>
        /// ヘルパープロセスへ JSON メッセージを送信します（スレッドセーフ）。
        /// </summary>
        private void SendIpcMessage(IpcMessage msg)
        {
            // 書き込み処理を完全にバックグラウンドスレッドに逃がし、読み取りループをブロックさせないようにする
            _ = Task.Run(() =>
            {
                lock (this.sendLock)
                {
                    if (this.pipeWriter == null || this.pipeClient == null || !this.pipeClient.IsConnected)
                    {
                        return;
                    }

                    try
                    {
                        var json = JsonConvert.SerializeObject(msg);
                        this.pipeWriter.WriteLine(json);
                        this.pipeWriter.Flush();
                    }
                    catch (Exception ex)
                    {
                        // 切断処理中またはパイプ破棄時の例外は正常系のため無視する
                        if (ex is OperationCanceledException || ex is ObjectDisposedException || ex.InnerException is ObjectDisposedException)
                        {
                            return;
                        }
                        this.AppendLogLine("Error sending message to helper.", ex, true);
                    }
                }
            });
        }

        /// <summary>
        /// ヘルパーからの Named Pipe メッセージを非同期で待ち受けるループ。
        /// </summary>
        private async Task ReceiveLoopAsync(CancellationToken token)
        {
            try
            {
                using (var reader = new StreamReader(this.pipeClient, new UTF8Encoding(false)))
                {
                    while (!token.IsCancellationRequested && this.pipeClient.IsConnected)
                    {
                        var line = await reader.ReadLineAsync();
                        if (line == null)
                        {
                            this.AppendLogLine("Named Pipe connection closed by Helper.");
                            break;
                        }

                        if (string.IsNullOrWhiteSpace(line)) continue;

                        try
                        {
                            var msg = JsonConvert.DeserializeObject<IpcMessage>(line);
                            if (msg != null)
                            {
                                this.ProcessIpcMessage(msg);
                            }
                        }
                        catch (Exception ex)
                        {
                            this.AppLogger.Error(ex, $"Failed to deserialize IPC message: {line}");
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 正常終了
            }
            catch (Exception ex)
            {
                this.AppendLogLine("Named Pipe receive loop encountered an error.", ex, true);
            }
            finally
            {
                this.Disconnect();
            }
        }

        /// <summary>
        /// ヘルパーから受信したメッセージを解釈し、プロパティの更新やUIバインディングの更新を処理します。
        /// </summary>
        private void ProcessIpcMessage(IpcMessage msg)
        {
            switch (msg.type)
            {
                case "status_changed":
                    WPFHelper.BeginInvoke(() =>
                    {
                        this.IsConnected = msg.connected ?? false;
                        this.IsJoinedVoiceChannel = msg.joinedVoice ?? false;
                    });
                    break;

                case "channels_updated":
                    WPFHelper.BeginInvoke(() =>
                    {
                        this.channels.Clear();
                        this.textChannels.Clear();
                        this.voiceChannels.Clear();

                        // 無効化用のダミーテキストチャンネルを最初に追加
                        this.textChannels.Add(new DiscordChannelContainer()
                        {
                            ID = "-1",
                            ServerName = "DISABLED",
                            Type = ChannelType.Text
                        });

                        // テキストチャンネル of 構築
                        if (msg.textChannels != null)
                        {
                            foreach (var tc in msg.textChannels)
                            {
                                this.textChannels.Add(new DiscordChannelContainer()
                               {
                                   ID = tc.id,
                                   Name = tc.name,
                                   ServerName = tc.serverName,
                                   Type = ChannelType.Text
                               });
                            }
                        }

                        // ボイスチャンネル of 構築
                        if (msg.voiceChannels != null)
                        {
                            foreach (var vc in msg.voiceChannels)
                            {
                                this.voiceChannels.Add(new DiscordChannelContainer()
                               {
                                   ID = vc.id,
                                   Name = vc.name,
                                   ServerName = vc.serverName,
                                   Type = ChannelType.Voice
                               });
                            }
                        }

                        this.channels.AddRange(this.textChannels);
                        this.channels.AddRange(this.voiceChannels);

                        // サーバー(Guilds)一覧 of 生成
                        this.guilds.Clear();
                        if (msg.guilds != null)
                        {
                            this.guilds.AddRange(msg.guilds);
                        }

                        this.RaisePropertyChanged(nameof(this.AvailableGuilds));
                        this.RaisePropertyChanged(nameof(this.AvailableGuildsText));

                        // 前回選択されていたチャンネルをIDから復元する
                        var textChID = this.Config.DefaultTextChannelID != "0" ?
                            this.Config.DefaultTextChannelID :
                            this.previousAvailableTextChannelID;

                        var voiceChID = this.Config.DefaultVoiceChannelID != "0" ?
                            this.Config.DefaultVoiceChannelID :
                            this.previousAvailableVoiceChannelID;

                        this.SelectedTextChannel = this.textChannels.FirstOrDefault(x =>
                            x.ID.ToString() == textChID);

                        this.SelectedVoiceChannel = this.voiceChannels.FirstOrDefault(x =>
                            x.ID.ToString() == voiceChID);
                    });
                    break;

                case "log":
                    this.AppendLogLine(msg.message ?? string.Empty);
                    break;

                case "error":
                    this.AppendLogLine(msg.message ?? string.Empty, null, true);
                    break;
            }
        }

        #endregion Named Pipe Communication

        private DiscordSettings Config => Settings.Default.DiscordSettings;
    }

    #region IPC Data Class Definitions

    /// <summary>
    /// Named Pipe 通信で送受信される JSON メッセージデータ構造のプラグイン側定義。
    /// ヘルパー側の定義と完全一致させます。
    /// </summary>
    public class IpcMessage
    {
        public string type { get; set; } = string.Empty;

        // プラグイン -> ヘルパー (コマンド用)
        public string token { get; set; }
        public string channelId { get; set; }
        public string text { get; set; }
        public bool? tts { get; set; }
        public string filePath { get; set; }

        // ヘルパー -> プラグイン (イベント/レスポンス用)
        public bool? connected { get; set; }
        public bool? joinedVoice { get; set; }
        public string message { get; set; }

        // チャンネルリスト更新用
        public List<string> guilds { get; set; }
        public List<DiscordChannelInfo> textChannels { get; set; }
        public List<DiscordChannelInfo> voiceChannels { get; set; }
    }

    public class DiscordChannelInfo
    {
        public string id { get; set; } = string.Empty;
        public string name { get; set; } = string.Empty;
        public string serverName { get; set; } = string.Empty;
    }

    #endregion
}
