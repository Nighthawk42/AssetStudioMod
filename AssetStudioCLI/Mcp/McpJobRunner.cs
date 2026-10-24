using AssetStudio;
using AssetStudioCLI.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AssetStudioCLI.Mcp
{
    internal sealed class JobResult
    {
        public bool Success { get; init; }
        public string Error { get; init; }
        public string Log { get; init; }
        public object Data { get; init; }
    }

    // Runs the regular CLI pipeline in-process for MCP tool calls.
    // Studio/CLIOptions are static, so jobs are serialized.
    internal static class McpJobRunner
    {
        private const int MaxLogLength = 16000;
        private static readonly SemaphoreSlim JobLock = new SemaphoreSlim(1, 1);
        private static readonly Regex AnsiRegex = new Regex(@"\e\[[0-9;]*m(?:\e\[K)?", RegexOptions.Compiled);

        /// <param name="args">CLI arguments, exactly as for AssetStudioModCLI</param>
        /// <param name="afterFilter">Optional hook invoked after assets are parsed and filtered (non-extract modes)</param>
        /// <param name="runWorkMode">Whether to run the export/info step for the parsed work mode</param>
        public static async Task<JobResult> RunAsync(string[] args, Func<object> afterFilter = null, bool runWorkMode = true, CancellationToken ct = default)
        {
            await JobLock.WaitAsync(ct);
            try
            {
                return await Task.Run(() => Run(args, afterFilter, runWorkMode), ct);
            }
            finally
            {
                JobLock.Release();
            }
        }

        private static JobResult Run(string[] args, Func<object> afterFilter, bool runWorkMode)
        {
            var capture = new StringWriter();
            var syncCapture = TextWriter.Synchronized(capture);
            var prevOut = Console.Out;
            var prevLogger = Logger.Default;
            Console.SetOut(syncCapture);
            Logger.Default = new McpLogger(syncCapture);
            object data = null;
            string error = null;
            try
            {
                Studio.Reset();
                CLIOptions.InitOptions();
                CLIOptions.ParseArgs(args);
                if (!CLIOptions.isParsed)
                {
                    error = "Invalid arguments. See log for details.";
                    return Result();
                }
                Logger.Default = new McpLogger(syncCapture, CLIOptions.o_logLevel.Value);
                Studio.ApplyOptions();
                // Progress output is console noise for MCP clients
                Progress.Default = new Progress<int>();
                Progress.SetInstance(1, new Progress<int>());

                if (CLIOptions.o_workMode.Value == WorkMode.Extract)
                {
                    Studio.ExtractBundles();
                    return Result();
                }
                if (!Studio.LoadAssets())
                {
                    error = "No Unity files could be loaded from the input path.";
                    return Result();
                }
                Studio.ParseAssets();
                if (CLIOptions.filterBy != FilterBy.None)
                {
                    Studio.Filter();
                }
                if (afterFilter != null)
                {
                    data = afterFilter();
                }
                if (!runWorkMode)
                {
                    return Result();
                }
                if (CLIOptions.o_exportAssetList.Value != ExportListType.None)
                {
                    Studio.ExportAssetList();
                }
                switch (CLIOptions.o_workMode.Value)
                {
                    case WorkMode.Info:
                        Studio.ShowExportableAssetsInfo();
                        break;
                    case WorkMode.Live2D:
                        Studio.ExportLive2D();
                        break;
                    case WorkMode.SplitObjects:
                        Studio.ExportSplitObjects();
                        break;
                    case WorkMode.Animator:
                        Studio.ExportAnimator();
                        break;
                    default:
                        Studio.ExportAssets();
                        break;
                }
                return Result();
            }
            catch (Exception ex)
            {
                error = ex.Message;
                syncCapture.WriteLine(ex.ToString());
                return Result();
            }
            finally
            {
                try
                {
                    Studio.Clear();
                }
                catch
                {
                    // ignored
                }
                Console.SetOut(prevOut);
                Logger.Default = prevLogger;
            }

            JobResult Result() => new JobResult
            {
                Success = error == null,
                Error = error,
                Log = TrimLog(AnsiRegex.Replace(capture.ToString(), "")),
                Data = data,
            };
        }

        private static string TrimLog(string log)
        {
            log = log.Trim();
            if (log.Length <= MaxLogLength)
                return log;
            // Keep the tail: errors and totals are reported last
            return "...(truncated)...\n" + log.Substring(log.Length - MaxLogLength);
        }

        public static List<string> CollectNewFiles(string folder, DateTime sinceUtc, int max, out int total)
        {
            total = 0;
            var files = new List<string>();
            if (!Directory.Exists(folder))
                return files;
            foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            {
                if (File.GetLastWriteTimeUtc(file) < sinceUtc)
                    continue;
                total++;
                if (files.Count < max)
                    files.Add(file);
            }
            return files;
        }

        private sealed class McpLogger : ILogger
        {
            private readonly TextWriter writer;
            private readonly LoggerEvent minLevel;

            public McpLogger(TextWriter writer, LoggerEvent minLevel = LoggerEvent.Info)
            {
                this.writer = writer;
                this.minLevel = minLevel;
            }

            public void Log(LoggerEvent loggerEvent, string message, bool ignoreLevel = false)
            {
                if ((loggerEvent < minLevel && !ignoreLevel) || string.IsNullOrEmpty(message))
                    return;
                writer.WriteLine($"[{loggerEvent}] {message.TrimEnd()}");
            }
        }
    }
}
