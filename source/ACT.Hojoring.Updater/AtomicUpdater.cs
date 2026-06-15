using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using NLog;
using NLog.Config;
using NLog.Targets;

namespace ACT.Hojoring
{
    /// <summary>
    /// .new ファイルを安全かつ原子的に置換するための専用クラスです。
    /// </summary>
    public static class AtomicUpdater
    {
        #region Logger

        private static Logger logger;

        public static Logger AppLogger
        {
            get
            {
                if (logger == null)
                {
                    ConfigureLogging();
                    logger = LogManager.GetLogger("AtomicUpdater");
                }
                return logger;
            }
        }

        public static void Log(string message, Exception ex = null)
        {
            if (ex != null)
            {
                AppLogger.Error(ex, message);
            }
            else
            {
                AppLogger.Info(message);
            }
        }

        private static string GetLogDirectory()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "anoyetta", "ACT", "logs");
        }

        private static void ConfigureLogging()
        {
            try
            {
                var config = LogManager.Configuration ?? new LoggingConfiguration();
                var logDir = GetLogDirectory();

                if (!Directory.Exists(logDir))
                {
                    Directory.CreateDirectory(logDir);
                }

                var logPathWithDate = Path.Combine(logDir, "AtomicUpdater.${shortdate}.log");

                var fileTarget = new FileTarget("AtomicUpdaterFile")
                {
                    FileName = logPathWithDate,
                    Layout = "${longdate} [${uppercase:${level:padding=-5}}] ${message} ${exception:format=tostring}",
                    Encoding = Encoding.UTF8,
                    KeepFileOpen = false,
                    AutoFlush = true
                };

                config.AddTarget(fileTarget);
                var rule = new LoggingRule("AtomicUpdater", LogLevel.Info, fileTarget);
                config.LoggingRules.Add(rule);

                LogManager.Configuration = config;
                LogManager.ReconfigExistingLoggers();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AtomicUpdater] Failed to configure logger: {ex.Message}");
            }
        }

        #endregion Logger

        public const string NewFileSuffix = ".new";
        public const string OldFileSuffix = ".old";
        private static bool executed = false;

        private static readonly string[] DeprecatedDlls = new[]
        {
            // FFXIV.Framework
            "Sharlayan.dll",
            "Newtonsoft.Json.dll", "Newtonsoft.Json.Bson.dll",
            "System.Reactive.dll", "System.Reactive.Linq.dll",
            "Microsoft.CodeAnalysis.dll", "Microsoft.CodeAnalysis.CSharp.dll", "Microsoft.CodeAnalysis.CSharp.Scripting.dll", "Microsoft.CodeAnalysis.Scripting.dll",
            "System.Runtime.CompilerServices.Unsafe.dll", "System.Memory.dll", "System.Buffers.dll", "System.Numerics.Vectors.dll",
            "System.Collections.Immutable.dll", "System.Threading.Tasks.Extensions.dll", "System.ValueTuple.dll", "System.Reflection.Metadata.dll",
            "System.Text.Encoding.CodePages.dll", "System.Text.Encodings.Web.dll", "Microsoft.Bcl.AsyncInterfaces.dll",
            "websocket-sharp.dll", "WindowsInput.dll",
            "Grpc.Core.dll", "Grpc.Core.Api.dll", "Grpc.Net.Client.dll", "Grpc.Net.Common.dll",
            "TagLibSharp.dll", "Prism.dll", "Prism.Wpf.dll", "CommonServiceLocator.dll",

            // ACT.SpecialSpellTimer
            "RazorLight.dll",
            "Microsoft.AspNetCore.Html.Abstractions.dll", "Microsoft.AspNetCore.Http.Abstractions.dll", "Microsoft.AspNetCore.Http.Features.dll",
            "Microsoft.AspNetCore.Mvc.Razor.Extensions.dll", "Microsoft.AspNetCore.Razor.dll", "Microsoft.AspNetCore.Razor.Language.dll", "Microsoft.AspNetCore.Razor.Runtime.dll",
            "Microsoft.CodeAnalysis.Razor.dll",
            "Microsoft.Extensions.Caching.Abstractions.dll", "Microsoft.Extensions.Caching.Memory.dll", "Microsoft.Extensions.Configuration.Abstractions.dll",
            "Microsoft.Extensions.DependencyInjection.dll", "Microsoft.Extensions.DependencyInjection.Abstractions.dll", "Microsoft.Extensions.FileProviders.Abstractions.dll",
            "Microsoft.Extensions.FileProviders.Physical.dll", "Microsoft.Extensions.FileSystemGlobbing.dll", "Microsoft.Extensions.Hosting.Abstractions.dll",
            "Microsoft.Extensions.Logging.Abstractions.dll", "Microsoft.Extensions.Options.dll", "Microsoft.Extensions.Primitives.dll",
            "Microsoft.IO.RecyclableMemoryStream.dll",
            "SixLabors.Fonts.dll", "SixLabors.ImageSharp.dll",
            "NPOI.Core.dll", "NPOI.OOXML.dll", "NPOI.OpenXml4Net.dll", "NPOI.OpenXmlFormats.dll",
            "ICSharpCode.SharpZipLib.dll", "Enums.NET.dll", "BouncyCastle.Cryptography.dll", "MathNet.Numerics.dll",
            "AvalonEdit.dll", "Markdig.Signed.dll", "Hjson.dll",

            // ACT.TTSYukkuri
            "RucheHome.Voiceroid.dll", "RucheHomeLib.dll", "VoiceTextWebAPI.Client.dll",

            // ACT.UltraScouter
            "Extended.Wpf.Toolkit.dll", "Xceed.Wpf.Toolkit.dll",
            "FontAwesome.WPF.dll", "NLog.dll",
            "MahApps.Metro.IconPacks.dll", "MahApps.Metro.IconPacks.Core.dll",
            "MahApps.Metro.IconPacks.Material.dll", "MahApps.Metro.IconPacks.MaterialLight.dll",
            "MahApps.Metro.IconPacks.BootstrapIcons.dll", "MahApps.Metro.IconPacks.BoxIcons.dll",
            "MahApps.Metro.IconPacks.Codicons.dll", "MahApps.Metro.IconPacks.Coolicons.dll",
            "MahApps.Metro.IconPacks.Entypo.dll", "MahApps.Metro.IconPacks.EvaIcons.dll",
            "MahApps.Metro.IconPacks.FeatherIcons.dll", "MahApps.Metro.IconPacks.FileIcons.dll",
            "MahApps.Metro.IconPacks.Fontaudio.dll", "MahApps.Metro.IconPacks.FontAwesome.dll",
            "MahApps.Metro.IconPacks.Fontisto.dll", "MahApps.Metro.IconPacks.ForkAwesome.dll",
            "MahApps.Metro.IconPacks.Ionicons.dll", "MahApps.Metro.IconPacks.JamIcons.dll",
            "MahApps.Metro.IconPacks.MaterialDesign.dll", "MahApps.Metro.IconPacks.MaterialLight.dll",
            "MahApps.Metro.IconPacks.Microns.dll", "MahApps.Metro.IconPacks.Modern.dll",
            "MahApps.Metro.IconPacks.Octicons.dll", "MahApps.Metro.IconPacks.PicolIcons.dll",
            "MahApps.Metro.IconPacks.PixelartIcons.dll", "MahApps.Metro.IconPacks.RadixIcons.dll",
            "MahApps.Metro.IconPacks.RemixIcon.dll", "MahApps.Metro.IconPacks.RPGAwesome.dll",
            "MahApps.Metro.IconPacks.SimpleIcons.dll", "MahApps.Metro.IconPacks.Typicons.dll",
            "MahApps.Metro.IconPacks.Unicons.dll", "MahApps.Metro.IconPacks.VaadinIcons.dll",
            "MahApps.Metro.IconPacks.WeatherIcons.dll", "MahApps.Metro.IconPacks.Zondicons.dll"
        };

        /// <summary>
        /// 現在のプラグインディレクトリから .new ファイルを探し、可能な限り即時置換します。
        /// 置換できなかったファイルは自動的に ACT 終了後のバッチ処理に回されます。
        /// </summary>
        public static void Apply()
        {
            if (executed) return;

            string targetDir = GetTargetDirectory();
            if (string.IsNullOrEmpty(targetDir)) return;

            try
            {
                DeleteOldFiles(targetDir);

                var newFiles = Directory.GetFiles(targetDir, "*" + NewFileSuffix, SearchOption.AllDirectories);
                if (newFiles.Length == 0) return;

                Log($"[AtomicUpdater] .new files detected. Starting replacement in {targetDir}");

                var lockedFiles = new List<string>();

                foreach (var newFile in newFiles)
                {
                    string targetPath = newFile.Substring(0, newFile.Length - NewFileSuffix.Length);
                    string fileName = Path.GetFileName(targetPath);

                    if (IsFileLocked(targetPath))
                    {
                        Log($"[AtomicUpdater] File is locked, scheduled for external update: {fileName}");
                        lockedFiles.Add(newFile);
                        continue;
                    }

                    try
                    {
                        if (File.Exists(targetPath))
                        {
                            File.SetAttributes(targetPath, FileAttributes.Normal);
                            File.Delete(targetPath);
                        }
                        File.Move(newFile, targetPath);
                        Log($"[AtomicUpdater] Successfully replaced: {fileName}");
                    }
                    catch
                    {
                        Log($"[AtomicUpdater] Failed immediate replace, adding to batch: {fileName}");
                        lockedFiles.Add(newFile);
                    }
                }

                if (lockedFiles.Count > 0)
                {
                    ScheduleExternalUpdate(lockedFiles);
                }
            }
            catch (Exception ex)
            {
                Log("[AtomicUpdater] Critical error during Apply.", ex);
            }
            finally
            {
                executed = true;
            }
        }

        /// <summary>
        /// 即時置換を試みず、現在存在するすべての .new ファイルを ACT 終了後に置換するように予約します。
        /// </summary>
        public static void RequestExternalUpdate()
        {
            string targetDir = GetTargetDirectory();
            if (string.IsNullOrEmpty(targetDir)) return;

            var newFiles = Directory.GetFiles(targetDir, "*" + NewFileSuffix, SearchOption.AllDirectories);
            if (newFiles.Length == 0) return;

            Log($"[AtomicUpdater] External update requested. {newFiles.Length} files scheduled.");
            ScheduleExternalUpdate(newFiles);
        }

        private static string GetTargetDirectory()
        {
            try
            {
                var assembly = Assembly.GetAssembly(typeof(ACT.Hojoring.Common.Hojoring));
                if (assembly != null)
                {
                    return Path.GetDirectoryName(assembly.Location);
                }
            }
            catch (Exception ex)
            {
                Log("[AtomicUpdater] Failed to detect assembly location.", ex);
            }
            return null;
        }

        private static bool IsFileLocked(string filePath)
        {
            if (!File.Exists(filePath)) return false;
            try
            {
                using (var stream = File.Open(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    stream.Close();
                }
            }
            catch { return true; }
            return false;
        }

        private static void ScheduleExternalUpdate(IEnumerable<string> newFiles)
        {
            string targetDir = GetTargetDirectory();
            if (string.IsNullOrEmpty(targetDir)) return;

            try
            {
                var batchPath = Path.Combine(Path.GetTempPath(), $"hojoring_atomic_swap_{Guid.NewGuid():N}.bat");
                var logFile = Path.Combine(GetLogDirectory(), $"AtomicUpdater.{DateTime.Now:yyyy-MM-dd}.log");

                using (var sw = new StreamWriter(batchPath, false, Encoding.GetEncoding(932)))
                {
                    sw.WriteLine("@echo off");
                    sw.WriteLine("echo ==================================================");
                    sw.WriteLine("echo  ACT.Hojoring Atomic Updater (Debug Mode)");
                    sw.WriteLine("echo ==================================================");
                    sw.WriteLine($"echo %date% %time% [INFO ] [Batch] Start external update session. >> \"{logFile}\"");

                    sw.WriteLine("echo ACTの終了を待機しています（3秒）...");
                    sw.WriteLine("timeout /t 3 /nobreak > nul");

                    // 不要となったDLLのクリーンアップを追加
                    sw.WriteLine("echo 不要なアセンブリをクリーンアップしています...");
                    foreach (var dll in DeprecatedDlls)
                    {
                        string path1 = Path.Combine(targetDir, dll);
                        string path2 = Path.Combine(targetDir, "bin", dll);

                        sw.WriteLine($"if exist \"{path1}\" attrib -r \"{path1}\" > nul");
                        sw.WriteLine($"if exist \"{path1}\" del /f /q \"{path1}\" > nul 2>&1");
                        sw.WriteLine($"if exist \"{path2}\" attrib -r \"{path2}\" > nul");
                        sw.WriteLine($"if exist \"{path2}\" del /f /q \"{path2}\" > nul 2>&1");
                    }

                    foreach (var newFile in newFiles)
                    {
                        string targetPath = newFile.Substring(0, newFile.Length - NewFileSuffix.Length);
                        string fileName = Path.GetFileName(targetPath);
                        string retryLabel = "retry_" + Guid.NewGuid().ToString("N").Substring(0, 8);

                        sw.WriteLine($":{retryLabel}");
                        sw.WriteLine($"echo 更新を試行中: {fileName}");
                        sw.WriteLine($"if exist \"{targetPath}\" attrib -r \"{targetPath}\" > nul");
                        sw.WriteLine($"del /f /q \"{targetPath}\" > nul 2>&1");
                        sw.WriteLine($"move /y \"{newFile}\" \"{targetPath}\" > nul 2>&1");

                        sw.WriteLine($"if exist \"{newFile}\" (");
                        sw.WriteLine($"  echo   - 失敗: ファイルがロックされています。1秒後に再試行します。");
                        sw.WriteLine($"  echo %date% %time% [WARN ] [Batch] Retrying {fileName}... >> \"{logFile}\"");
                        sw.WriteLine("  timeout /t 1 > nul");
                        sw.WriteLine($"  goto :{retryLabel}");
                        sw.WriteLine($") else (");
                        sw.WriteLine($"  echo   - 完了: {fileName}");
                        sw.WriteLine($"  echo %date% %time% [INFO ] [Batch] Successfully replaced {fileName}. >> \"{logFile}\"");
                        sw.WriteLine($")");
                    }

                    sw.WriteLine($"echo %date% %time% [INFO ] [Batch] External update session completed. >> \"{logFile}\"");
                    sw.WriteLine("echo すべての更新が完了しました。");
                    sw.WriteLine("pause");
                    sw.WriteLine($"del \"{batchPath}\"");
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"{batchPath}\"",
                    CreateNoWindow = false, // ウィンドウを表示
                    UseShellExecute = true,  // シェル実行を使用
                    WindowStyle = ProcessWindowStyle.Normal // 通常のウィンドウ
                });

                Log($"[AtomicUpdater] Debug batch script created: {Path.GetFileName(batchPath)}");
            }
            catch (Exception ex)
            {
                Log("[AtomicUpdater] Failed to schedule external update.", ex);
            }
        }

        public static void DeleteOldFiles(string targetDir)
        {
            try
            {
                if (!Directory.Exists(targetDir)) return;
                var oldFiles = Directory.GetFiles(targetDir, "*" + OldFileSuffix, SearchOption.AllDirectories);
                foreach (var oldFile in oldFiles)
                {
                    try { File.SetAttributes(oldFile, FileAttributes.Normal); File.Delete(oldFile); } catch { }
                }
            }
            catch { }
        }
    }
}