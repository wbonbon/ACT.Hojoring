using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.Audio;
using Discord.WebSocket;

namespace ACT.Hojoring.DiscordHelper
{
    /// <summary>
    /// Discord.Net を使用して Discord ボットの接続、チャンネル列挙、音声配信を管理するサービス。
    /// </summary>
    public class DiscordBotService : IDisposable
    {
        private DiscordSocketClient? _discordClient;
        private IAudioClient? _audioClient;
        private AudioOutStream? _audioOutStream;

        private readonly ConcurrentQueue<string> _playQueue = new ConcurrentQueue<string>();
        private Thread? _playWorker;
        private volatile bool _playWorkerRunning = false;
        private static readonly object SendBlocker = new object();

        // イベント定義 (Program.cs に状態変更やログを通知するため)
        public event Action<bool, bool>? OnStatusChanged;
        public event Action<IpcMessage>? OnChannelsUpdated;
        public event Action<string, Exception?>? OnLog;
        public event Action<string>? OnError;

        public bool IsConnected => _discordClient?.LoginState == LoginState.LoggedIn;
        public bool IsJoinedVoice => _audioClient?.ConnectionState == ConnectionState.Connected;

        public DiscordBotService()
        {
            SetupNativeDlls();
        }

        /// <summary>
        /// 実行ディレクトリの隣にある \bin\lib からネイティブDLL (libopus, libsodium) を
        /// 実行ディレクトリへコピーしてロード可能な状態にします。
        /// </summary>
        private void SetupNativeDlls()
        {
            try
            {
                var exeDir = AppContext.BaseDirectory;
                if (string.IsNullOrEmpty(exeDir)) return;

                // ネイティブDLLの検索候補パスを定義 (本番リリース用とVSデバッグ用の両方をサポート)
                var exeParent = Directory.GetParent(exeDir.TrimEnd(Path.DirectorySeparatorChar))?.FullName ?? "";
                var exeGrandParent = string.IsNullOrEmpty(exeParent) ? "" : Directory.GetParent(exeParent)?.FullName ?? "";

                var searchDirs = new[]
                {
                    Path.Combine(exeParent, "lib"),          // ..\lib (本番パッケージ用: bin\lib)
                    Path.Combine(exeGrandParent, "lib"),     // ..\..\lib (VSデバッグ用: bin\x64\Debug\lib)
                    Path.Combine(exeDir, "lib")              // .\lib (ローカル実行用)
                };

                var targets = new[]
                {
                    new { dllName = "libopus.dll", dstName = "opus.dll" },
                    new { dllName = "libopus.dll", dstName = "libopus.dll" },
                    new { dllName = "libsodium.dll", dstName = "sodium.dll" },
                    new { dllName = "libsodium.dll", dstName = "libsodium.dll" },
                    new { dllName = "libdave.dll", dstName = "dave.dll" },
                    new { dllName = "libdave.dll", dstName = "libdave.dll" }
                };

                foreach (var target in targets)
                {
                    string? srcPath = null;
                    foreach (var dir in searchDirs)
                    {
                        if (string.IsNullOrEmpty(dir)) continue;
                        var tempPath = Path.Combine(dir, target.dllName);
                        if (File.Exists(tempPath))
                        {
                            srcPath = tempPath;
                            break;
                        }
                    }

                    if (srcPath != null)
                    {
                        var dstPath = Path.Combine(exeDir, target.dstName);
                        if (!File.Exists(dstPath))
                        {
                            File.Copy(srcPath, dstPath, true);
                            Log($"Native DLL Installed: {target.dstName} (from {Path.GetFileName(Path.GetDirectoryName(srcPath))})");
                        }
                    }
                    else
                    {
                        Log($"Warning: Native DLL Source '{target.dllName}' not found in any search path.", null, true);
                    }
                }
            }
            catch (Exception ex)
            {
                Log("Failed to setup native DLLs", ex, true);
            }
        }

        /// <summary>
        /// Discordボットに接続します。
        /// </summary>
        public async Task ConnectAsync(string token)
        {
            if (_discordClient == null)
            {
                var config = new DiscordSocketConfig
                {
                    GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages | GatewayIntents.GuildVoiceStates,
                    LogLevel = LogSeverity.Debug,
                    EnableVoiceDaveEncryption = false
                };
                _discordClient = new DiscordSocketClient(config);
                _discordClient.Log += message =>
                {
                    // DAVEプロトコル移行期の非クリティカルな警告ログ（Malformed Frame / Unknown OpCode）をフィルタリングしてコンソールの不要な混乱を防ぐ
                    if (message.Message != null && 
                        (message.Message.Contains("Malformed Frame") || 
                         message.Message.Contains("Unknown OpCode (15)")))
                    {
                        return Task.CompletedTask;
                    }

                    var isError = message.Severity == LogSeverity.Error || message.Severity == LogSeverity.Critical;
                    Log($"[Discord.Net] {message.Source}: {message.Message}", message.Exception, isError);
                    return Task.CompletedTask;
                };
                _discordClient.Ready += DiscordClientOnReady;
                _discordClient.LoggedOut += DiscordClientOnLoggedOut;
            }

            try
            {
                Log("Connecting to Discord...");
                await _discordClient.LoginAsync(TokenType.Bot, token);
                await _discordClient.StartAsync();
            }
            catch (Exception ex)
            {
                Log("Connection Error", ex, true);
                OnError?.Invoke($"Connection Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Discordボットから切断します。
        /// </summary>
        public async Task DisconnectAsync()
        {
            Log("Disconnecting from Discord...");
            ClearQueue();
            await LeaveVoiceAsync();

            if (_discordClient != null)
            {
                await _discordClient.StopAsync();
                await _discordClient.LogoutAsync();
                _discordClient.Dispose();
                _discordClient = null;
            }
            
            NotifyStatus();
        }

        /// <summary>
        /// ボイスチャンネルに参加します。
        /// </summary>
        public async Task JoinVoiceAsync(string channelId)
        {
            if (_discordClient == null) return;

            if (!ulong.TryParse(channelId, out var id))
            {
                OnError?.Invoke($"Invalid channel ID: {channelId}");
                return;
            }

            var channel = _discordClient.GetChannel(id) as SocketVoiceChannel;
            if (channel == null)
            {
                OnError?.Invoke($"Voice channel not found: {channelId}");
                return;
            }

            try
            {
                Log($"Joining Voice Channel: [{channel.Guild.Name}] {channel.Name}");
                _audioClient = await channel.ConnectAsync();
                Log($"Joined Voice Channel: {channel.Name}");

                _audioOutStream = _audioClient.CreatePCMStream(AudioApplication.Voice, bufferMillis: 200);

                lock (SendBlocker)
                {
                    ClearQueue();

                    if (_playWorker == null || !_playWorker.IsAlive)
                    {
                        _playWorkerRunning = true;
                        _playWorker = new Thread(PlayThread)
                        {
                            IsBackground = true,
                            Priority = ThreadPriority.BelowNormal
                        };
                        _playWorker.Start();
                    }
                }

                NotifyStatus();
            }
            catch (Exception ex)
            {
                Log("Join Voice Channel Error", ex, true);
                OnError?.Invoke($"Join Voice Error: {ex.Message}");
            }
        }

        /// <summary>
        /// ボイスチャンネルから退出します。
        /// </summary>
        public async Task LeaveVoiceAsync()
        {
            _playWorkerRunning = false;
            
            lock (SendBlocker)
            {
                if (_playWorker != null)
                {
                    _playWorker.Join(TimeSpan.FromMilliseconds(500));
                    _playWorker = null;
                }
                ClearQueue();

                if (_audioOutStream != null)
                {
                    _audioOutStream.Dispose();
                    _audioOutStream = null;
                }
            }

            if (_audioClient != null)
            {
                await _audioClient.StopAsync();
                _audioClient.Dispose();
                _audioClient = null;
            }

            Log("Left Voice Channel");
            NotifyStatus();
        }

        /// <summary>
        /// テキストメッセージを送信します。
        /// </summary>
        public async Task SendMessageAsync(string channelId, string text, bool tts)
        {
            if (_discordClient == null) return;

            if (!ulong.TryParse(channelId, out var id))
            {
                OnError?.Invoke($"Invalid channel ID: {channelId}");
                return;
            }

            var channel = _discordClient.GetChannel(id) as SocketTextChannel;
            if (channel != null)
            {
                await channel.SendMessageAsync(text, isTTS: tts);
            }
            else
            {
                OnError?.Invoke($"Text channel not found: {channelId}");
            }
        }

        /// <summary>
        /// 音声ファイルの再生をキューに登録します。
        /// </summary>
        public void PlayAudio(string filePath)
        {
            if (!File.Exists(filePath))
            {
                Log($"Play Audio Error: File not found. {filePath}", null, true);
                return;
            }

            _playQueue.Enqueue(filePath);
        }

        /// <summary>
        /// 再生キューをクリアします。
        /// </summary>
        public void ClearQueue()
        {
            while (_playQueue.TryDequeue(out _)) ;
        }

        private void PlayThread()
        {
            while (_playWorkerRunning)
            {
                if (_playQueue.IsEmpty)
                {
                    Thread.Sleep(50);
                    continue;
                }

                while (_playQueue.TryDequeue(out var filePath))
                {
                    if (!_playWorkerRunning) return;

                    PlayCore(filePath);
                    Thread.Sleep(TimeSpan.FromSeconds(0.05)); // 再生終了後の余韻ディレイ
                }

                Thread.Yield();
            }
        }

        private void PlayCore(string filePath)
        {
            lock (SendBlocker)
            {
                if (!IsJoinedVoice || _audioClient == null || _audioOutStream == null) return;

                Log($"Play Sound: {Path.GetFileName(filePath)}");

                try
                {
                    _audioClient.SetSpeakingAsync(true).GetAwaiter().GetResult();
                    // NAudioリサンプラー経由でストリームに流し込む
                    AudioPipeline.SendAudio(filePath, _audioOutStream);
                    
                    // バッファを完全にフラッシュする
                    _audioOutStream.Flush();
                }
                catch (Exception ex)
                {
                    Log("Play Sound Error", ex, true);
                }
                finally
                {
                    try
                    {
                        _audioClient.SetSpeakingAsync(false).GetAwaiter().GetResult();
                    }
                    catch (Exception ex)
                    {
                        Log("SetSpeakingAsync(false) Error", ex, false);
                    }
                }
            }
        }

        private async Task DiscordClientOnReady()
        {
            if (_discordClient == null) return;

            Log("Connected to DISCORD. Client is Ready!");
            await _discordClient.SetGameAsync("ACT.Hojoring (Helper)");

            // 接続ステータス通知
            NotifyStatus();

            // チャンネルリストを列挙・通知
            EnumerateChannels();
        }

        private Task DiscordClientOnLoggedOut()
        {
            Log("Disconnected from DISCORD. Bye!");
            NotifyStatus();
            return Task.CompletedTask;
        }

        private void NotifyStatus()
        {
            OnStatusChanged?.Invoke(IsConnected, IsJoinedVoice);
        }

        /// <summary>
        /// サーバーおよびチャンネル一覧を列挙して通知イベントを発行します。
        /// </summary>
        public void EnumerateChannels()
        {
            if (_discordClient == null || !IsConnected) return;

            var msg = new IpcMessage
            {
                type = "channels_updated",
                guilds = _discordClient.Guilds.OrderBy(x => x.Id).Select(x => x.Name).ToList(),
                textChannels = new System.Collections.Generic.List<DiscordChannelInfo>(),
                voiceChannels = new System.Collections.Generic.List<DiscordChannelInfo>()
            };

            foreach (var guild in _discordClient.Guilds)
            {
                foreach (var ch in guild.TextChannels.OrderBy(x => x.Position))
                {
                    msg.textChannels.Add(new DiscordChannelInfo
                    {
                        id = ch.Id.ToString(),
                        name = ch.Name,
                        serverName = guild.Name
                    });
                }

                foreach (var ch in guild.VoiceChannels.OrderBy(x => x.Position))
                {
                    msg.voiceChannels.Add(new DiscordChannelInfo
                    {
                        id = ch.Id.ToString(),
                        name = ch.Name,
                        serverName = guild.Name
                    });
                }
            }

            OnChannelsUpdated?.Invoke(msg);
        }

        private void Log(string message, Exception? ex = null, bool err = false)
        {
            var text = message;
            if (ex != null)
            {
                text += $"{Environment.NewLine}{ex}";
            }

            if (err)
            {
                Console.Error.WriteLine($"[ERROR] {text}");
                OnError?.Invoke(message);
            }
            else
            {
                Console.WriteLine($"[LOG] {text}");
                OnLog?.Invoke(message, ex);
            }
        }

        public void Dispose()
        {
            DisconnectAsync().GetAwaiter().GetResult();
        }
    }
}
