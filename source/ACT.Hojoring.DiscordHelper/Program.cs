using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ACT.Hojoring.DiscordHelper
{
    internal class Program
    {
        private const string PipeName = "ACTHojoringDiscord";
        private static DiscordBotService? _botService;
        private static NamedPipeServerStream? _pipeServer;
        private static StreamWriter? _pipeWriter;
        private static readonly object WriteLock = new object();
        private static CancellationTokenSource _cts = new CancellationTokenSource();

        private static async Task Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine("=== ACT.Hojoring.DiscordHelper Started ===");

            // 1. 親プロセスのIDを引数から解析して監視を開始
            int parentPid = 0;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--parent-pid" && i + 1 < args.Length)
                {
                    int.TryParse(args[i + 1], out parentPid);
                }
            }

            if (parentPid > 0)
            {
                StartParentProcessMonitor(parentPid);
            }
            else
            {
                Console.WriteLine("Warning: No parent process PID specified. Orphan protection is disabled.");
            }

            // 2. Discordボットサービスの初期化
            _botService = new DiscordBotService();
            _botService.OnLog += BotServiceOnLog;
            _botService.OnError += BotServiceOnError;
            _botService.OnStatusChanged += BotServiceOnStatusChanged;
            _botService.OnChannelsUpdated += BotServiceOnChannelsUpdated;

            // 3. Named Pipe サーバーと通信ループの開始
            try
            {
                await RunPipeServerAsync(_cts.Token);
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("Server loop canceled.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Fatal Error in Pipe Server: {ex.Message}");
            }
            finally
            {
                Cleanup();
            }
        }

        /// <summary>
        /// Named Pipe サーバーを起動し、クライアントからの接続とコマンドを処理します。
        /// </summary>
        private static async Task RunPipeServerAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                Console.WriteLine($"Waiting for ACT Plugin connection on Named Pipe: {PipeName}...");

                _pipeServer = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await _pipeServer.WaitForConnectionAsync(cancellationToken);
                Console.WriteLine("ACT Plugin connected.");

                using (var reader = new StreamReader(_pipeServer, new UTF8Encoding(false)))
                using (var writer = new StreamWriter(_pipeServer, new UTF8Encoding(false)) { AutoFlush = true })
                {
                    lock (WriteLock)
                    {
                        _pipeWriter = writer;
                    }

                    // 接続直後のステータスを即座に返す
                    if (_botService != null)
                    {
                        SendIpcMessage(new IpcMessage
                        {
                            type = "status_changed",
                            connected = _botService.IsConnected,
                            joinedVoice = _botService.IsJoinedVoice
                        });
                    }

                    // メッセージ読み込みループ
                    while (!cancellationToken.IsCancellationRequested && _pipeServer.IsConnected)
                    {
                        var line = await reader.ReadLineAsync(cancellationToken);
                        if (line == null)
                        {
                            // クライアントが切断された場合
                            Console.WriteLine("ACT Plugin disconnected.");
                            break;
                        }

                        if (string.IsNullOrWhiteSpace(line)) continue;

                        try
                        {
                            var message = JsonSerializer.Deserialize<IpcMessage>(line);
                            if (message != null)
                            {
                                await ProcessCommandAsync(message);
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Failed to parse or process command: {ex.Message}");
                            BotServiceOnError($"IPC Command Processing Error: {ex.Message}");
                        }
                    }
                }

                // クライアントが切断された場合、一度Discordボットをグレースフル切断して
                // プロセスをクリーンに終了します（ACTプラグイン側でプロセスの再起動を伴う再接続が行われます）
                Console.WriteLine("Named Pipe client disconnected. Exiting helper application.");
                break; 
            }
        }

        /// <summary>
        /// プラグインからのコマンドメッセージを処理します。
        /// </summary>
        private static async Task ProcessCommandAsync(IpcMessage msg)
        {
            if (_botService == null) return;

            switch (msg.type)
            {
                case "connect":
                    if (!string.IsNullOrEmpty(msg.token))
                    {
                        await _botService.ConnectAsync(msg.token);
                    }
                    break;

                case "disconnect":
                    await _botService.DisconnectAsync();
                    break;

                case "get_channels":
                    _botService.EnumerateChannels();
                    break;

                case "join_voice":
                    if (!string.IsNullOrEmpty(msg.channelId))
                    {
                        await _botService.JoinVoiceAsync(msg.channelId);
                    }
                    break;

                case "leave_voice":
                    await _botService.LeaveVoiceAsync();
                    break;

                case "send_message":
                    if (!string.IsNullOrEmpty(msg.channelId) && !string.IsNullOrEmpty(msg.text))
                    {
                        await _botService.SendMessageAsync(msg.channelId, msg.text, msg.tts ?? false);
                    }
                    break;

                case "play_audio":
                    if (!string.IsNullOrEmpty(msg.filePath))
                    {
                        _botService.PlayAudio(msg.filePath);
                    }
                    break;

                case "clear_queue":
                    _botService.ClearQueue();
                    break;

                default:
                    Console.WriteLine($"Unknown command type: {msg.type}");
                    break;
            }
        }

        /// <summary>
        /// プラグインへ Named Pipe 経由で JSON メッセージを送信します。
        /// </summary>
        private static void SendIpcMessage(IpcMessage msg)
        {
            // 書き込み処理を完全にバックグラウンドスレッドに逃がし、メッセージループをブロックさせないようにする
            _ = Task.Run(() =>
            {
                lock (WriteLock)
                {
                    if (_pipeWriter == null || _pipeServer == null || !_pipeServer.IsConnected) return;

                    try
                    {
                        var json = JsonSerializer.Serialize(msg);
                        _pipeWriter.WriteLine(json);
                        _pipeWriter.Flush();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error writing to Named Pipe: {ex.Message}");
                    }
                }
            });
        }

        #region Bot Service Event Handlers

        private static void BotServiceOnLog(string message, Exception? ex)
        {
            var text = message;
            if (ex != null)
            {
                text += $"{Environment.NewLine}{ex}";
            }

            SendIpcMessage(new IpcMessage
            {
                type = "log",
                message = text
            });
        }

        private static void BotServiceOnError(string errorMessage)
        {
            SendIpcMessage(new IpcMessage
            {
                type = "error",
                message = errorMessage
            });
        }

        private static void BotServiceOnStatusChanged(bool connected, bool joinedVoice)
        {
            Console.WriteLine($"[STATUS] Connected: {connected}, JoinedVoice: {joinedVoice}");

            SendIpcMessage(new IpcMessage
            {
                type = "status_changed",
                connected = connected,
                joinedVoice = joinedVoice
            });
        }

        private static void BotServiceOnChannelsUpdated(IpcMessage msg)
        {
            Console.WriteLine("[CHANNELS] Updated channel list.");
            SendIpcMessage(msg);
        }

        #endregion

        /// <summary>
        /// 親プロセスの生存を監視し、親が消滅したら自動的に自分をシャットダウンします。
        /// </summary>
        private static void StartParentProcessMonitor(int parentPid)
        {
            Console.WriteLine($"Orphan protection enabled. Monitoring parent process PID: {parentPid}");
            
            var monitorThread = new Thread(() =>
            {
                try
                {
                    var parentProcess = Process.GetProcessById(parentPid);
                    
                    while (!_cts.Token.IsCancellationRequested)
                    {
                        if (parentProcess.HasExited)
                        {
                            Console.WriteLine("Parent process exited. Initiating automatic shutdown.");
                            _cts.Cancel();
                            break;
                        }
                        Thread.Sleep(2000); // 2秒ごとにチェック
                    }
                }
                catch (Exception)
                {
                    Console.WriteLine("Parent process already exited or could not be found. Initiating automatic shutdown.");
                    _cts.Cancel();
                }
            })
            {
                IsBackground = true,
                Name = "ParentProcessMonitor"
            };

            monitorThread.Start();
        }

        /// <summary>
        /// 終了時のクリーンアップ処理
        /// </summary>
        private static void Cleanup()
        {
            Console.WriteLine("Cleaning up resources...");
            
            if (_botService != null)
            {
                _botService.Dispose();
                _botService = null;
            }

            lock (WriteLock)
            {
                if (_pipeWriter != null)
                {
                    _pipeWriter.Dispose();
                    _pipeWriter = null;
                }
            }

            if (_pipeServer != null)
            {
                _pipeServer.Dispose();
                _pipeServer = null;
            }

            Console.WriteLine("ACT.Hojoring.DiscordHelper shutdown complete.");
        }
    }
}
