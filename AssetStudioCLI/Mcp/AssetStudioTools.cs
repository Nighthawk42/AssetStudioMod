using AssetStudio;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AssetStudioCLI.Mcp
{
    [McpServerToolType]
    public static class AssetStudioTools
    {
        private const string InputPathDesc = "Path to a Unity asset file, bundle, or game data folder. Multiple paths can be separated with ';'.";
        private const string AssetTypesDesc = "Asset types to include, e.g. [\"audio\", \"tex2d\"]. Values: tex2d, tex2dArray, sprite, textAsset, monoBehaviour, font, shader, movieTexture, audio, video, mesh, all. Defaults to all.";
        private const string NameFilterDesc = "Only include assets whose name contains any of these strings (case-insensitive), or matches the regex when use_regex is true.";
        private const string ContainerFilterDesc = "Only include assets whose container path contains any of these strings, or matches the regex when use_regex is true.";
        private const string UnityVersionDesc = "Override the Unity version (e.g. \"2021.3.10f1\") for files with stripped version info.";

        private static readonly HashSet<string> PreviewTextExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".txt", ".json", ".xml", ".shader", ".csv", ".yaml", ".yml", ".lua", ".md", ".html", ".js", ".cs"
        };

        [McpServerTool(Name = "inspect_assets", ReadOnly = true), Description("Load Unity files and summarize them: Unity version, number of exportable assets per type, and Live2D model count. Run this first to see what a game contains.")]
        public static async Task<object> InspectAssets(
            [Description(InputPathDesc)] string input_path,
            [Description(AssetTypesDesc)] string[] asset_types = null,
            [Description(UnityVersionDesc)] string unity_version = null,
            CancellationToken ct = default)
        {
            var args = new ArgsBuilder(input_path).Mode("info").Types(asset_types).Add("--unity-version", unity_version);
            var result = await McpJobRunner.RunAsync(args.Build(), () => new
            {
                unity_version = Studio.parsedAssetsList.FirstOrDefault()?.Asset.version.ToString(),
                loaded_files = Studio.assetsManager.AssetsFileList.Count,
                total_assets = Studio.parsedAssetsList.Count,
                assets_by_type = Studio.parsedAssetsList
                    .GroupBy(x => x.TypeString)
                    .OrderByDescending(x => x.Count())
                    .ToDictionary(x => x.Key, x => x.Count()),
                live2d_models = Studio.l2dModelDict.Count,
            }, runWorkMode: false, ct);
            return Response(result);
        }

        [McpServerTool(Name = "list_assets", ReadOnly = true), Description("List exportable assets with name, type, container, source file, size and path_id. Supports filtering and paging. Use path_id values with export_assets or preview_asset.")]
        public static async Task<object> ListAssets(
            [Description(InputPathDesc)] string input_path,
            [Description(AssetTypesDesc)] string[] asset_types = null,
            [Description(NameFilterDesc)] string[] name_filter = null,
            [Description(ContainerFilterDesc)] string[] container_filter = null,
            [Description("Treat name_filter/container_filter as a regular expression (first value only).")] bool use_regex = false,
            [Description("Number of assets to skip.")] int offset = 0,
            [Description("Maximum number of assets to return (1-1000).")] int limit = 100,
            [Description(UnityVersionDesc)] string unity_version = null,
            CancellationToken ct = default)
        {
            limit = Math.Clamp(limit, 1, 1000);
            offset = Math.Max(offset, 0);
            var args = new ArgsBuilder(input_path).Mode("info").Types(asset_types)
                .Filters(name_filter, container_filter, use_regex)
                .Add("--unity-version", unity_version);
            var result = await McpJobRunner.RunAsync(args.Build(), () => new
            {
                total = Studio.parsedAssetsList.Count,
                offset,
                assets = Studio.parsedAssetsList.Skip(offset).Take(limit).Select(x => new
                {
                    name = x.Text,
                    type = x.TypeString,
                    container = x.Container,
                    source_file = x.SourceFile.fileName,
                    size = x.FullSize,
                    // as string: 64-bit ids lose precision as JSON numbers in many clients
                    path_id = x.m_PathID.ToString(),
                }).ToList(),
            }, runWorkMode: false, ct);
            return Response(result);
        }

        [McpServerTool(Name = "export_assets"), Description("Export assets to a folder. mode=export converts assets (textures/sprites to images, audio to wav, meshes to obj, text, MonoBehaviour to json, fonts, shaders, video); mode=exportRaw writes raw asset data; mode=dump writes json dumps. Returns the number of files written and a sample of their paths.")]
        public static async Task<object> ExportAssets(
            [Description(InputPathDesc)] string input_path,
            [Description("Folder to write exported files to. Created if missing.")] string output_folder,
            [Description(AssetTypesDesc)] string[] asset_types = null,
            [Description("export (default), exportRaw, or dump.")] string mode = "export",
            [Description(NameFilterDesc)] string[] name_filter = null,
            [Description(ContainerFilterDesc)] string[] container_filter = null,
            [Description("Only export assets with these exact path_id values.")] string[] path_ids = null,
            [Description("Only export assets from this source file (source_file from list_assets). Use together with path_ids, since path IDs are only unique per file.")] string source_file = null,
            [Description("Treat name_filter/container_filter as a regular expression (first value only).")] bool use_regex = false,
            [Description("How to group exported files into folders: none, type, container (default), containerFull, fileName, sceneHierarchy.")] string group_by = null,
            [Description("File name format: assetName (default), assetName_pathID, pathID.")] string filename_format = null,
            [Description("Image format for textures/sprites: png (default), jpg, bmp, tga, webp, none.")] string image_format = null,
            [Description("Audio format: wav (default) or none (export raw audio data).")] string audio_format = null,
            [Description("Overwrite existing files instead of adding a suffix.")] bool overwrite = false,
            [Description(UnityVersionDesc)] string unity_version = null,
            CancellationToken ct = default)
        {
            var modeArg = mode?.ToLowerInvariant() switch
            {
                null or "" or "export" => "export",
                "exportraw" or "raw" => "exportRaw",
                "dump" => "dump",
                _ => null,
            };
            if (modeArg == null)
                return Error($"Unsupported mode \"{mode}\". Use export, exportRaw, or dump.");

            var exactIds = ParsePathIds(path_ids, out var idError);
            if (idError != null)
                return Error(idError);

            var args = new ArgsBuilder(input_path).Mode(modeArg).Types(asset_types)
                .Filters(name_filter, container_filter, use_regex, exactIds)
                .Output(output_folder)
                .Add("-g", group_by).Add("-f", filename_format)
                .Add("--image-format", image_format).Add("--audio-format", audio_format)
                .Flag("-r", overwrite)
                .Add("--unity-version", unity_version);
            return await RunExport(args, output_folder, ExactIdFilter(exactIds, source_file), ct);
        }

        [McpServerTool(Name = "export_models"), Description("Export 3D models as binary glTF 2.0 (.glb) with embedded textures, skeletons, blend shapes and animations. mode=animator exports Animator assets (rigged models with optional animations); mode=splitObjects exports every scene model object separately.")]
        public static async Task<object> ExportModels(
            [Description(InputPathDesc)] string input_path,
            [Description("Folder to write exported files to. Created if missing.")] string output_folder,
            [Description("animator (default) or splitObjects.")] string mode = "animator",
            [Description("Animation handling: auto (default, export model-related animations), skip, all.")] string animation = null,
            [Description("Model scale factor (default 1).")] float? scale_factor = null,
            [Description("Texture format embedded in the model: png (default) or jpg.")] string image_format = null,
            [Description("Overwrite existing files instead of adding a suffix.")] bool overwrite = false,
            [Description(UnityVersionDesc)] string unity_version = null,
            CancellationToken ct = default)
        {
            var modeArg = mode?.ToLowerInvariant() switch
            {
                null or "" or "animator" => "animator",
                "splitobjects" or "split" => "splitObjects",
                _ => null,
            };
            if (modeArg == null)
                return Error($"Unsupported mode \"{mode}\". Use animator or splitObjects.");

            var args = new ArgsBuilder(input_path).Mode(modeArg).Output(output_folder)
                .Add("--model-animation", animation)
                .Add("--model-scale-factor", scale_factor?.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Add("--image-format", image_format)
                .Flag("-r", overwrite)
                .Add("--unity-version", unity_version);
            return await RunExport(args, output_folder, null, ct);
        }

        [McpServerTool(Name = "export_live2d"), Description("Export Live2D Cubism models (moc3, textures, motions, physics) found in the Unity files.")]
        public static async Task<object> ExportLive2D(
            [Description(InputPathDesc)] string input_path,
            [Description("Folder to write exported files to. Created if missing.")] string output_folder,
            [Description("Model grouping: container (default), fileName, modelName.")] string group_by = null,
            [Description("Motion export mode: monoBehaviour (default) or animationClip.")] string motion_mode = null,
            [Description("Overwrite existing files instead of adding a suffix.")] bool overwrite = false,
            [Description(UnityVersionDesc)] string unity_version = null,
            CancellationToken ct = default)
        {
            var args = new ArgsBuilder(input_path).Mode("live2d").Output(output_folder)
                .Add("--l2d-group-option", group_by)
                .Add("--l2d-motion-mode", motion_mode)
                .Flag("-r", overwrite)
                .Add("--unity-version", unity_version);
            return await RunExport(args, output_folder, null, ct);
        }

        [McpServerTool(Name = "extract_bundles"), Description("Decompress asset bundles / web data files into their raw contained files, without converting assets.")]
        public static async Task<object> ExtractBundles(
            [Description(InputPathDesc)] string input_path,
            [Description("Folder to write extracted files to. Created if missing.")] string output_folder,
            CancellationToken ct = default)
        {
            var args = new ArgsBuilder(input_path).Mode("extract").Output(output_folder);
            return await RunExport(args, output_folder, null, ct);
        }

        [McpServerTool(Name = "preview_asset", ReadOnly = true), Description("Preview a single asset by path_id: textures and sprites are returned as a PNG image, text-based assets (TextAsset, MonoBehaviour json, Shader) as text. Other types are exported to a temp folder and their path is returned.")]
        public static async Task<CallToolResult> PreviewAsset(
            [Description(InputPathDesc)] string input_path,
            [Description("The asset's path_id, from list_assets.")] string path_id,
            [Description("The asset's source_file from list_assets. Path IDs are only unique within one file, so pass this to avoid matching several assets.")] string source_file = null,
            [Description("Maximum characters of text to return for text assets.")] int max_text_length = 20000,
            [Description(UnityVersionDesc)] string unity_version = null,
            CancellationToken ct = default)
        {
            if (!long.TryParse(path_id, out var id))
                return ErrorResult($"Invalid path_id \"{path_id}\".");

            var tempDir = Path.Combine(Path.GetTempPath(), "AssetStudioMCP", "preview", Guid.NewGuid().ToString("N"));
            var args = new ArgsBuilder(input_path).Mode("export")
                .Add("--filter-by-pathid", path_id)
                .Output(tempDir)
                .Add("-g", "none").Add("--image-format", "png")
                .Add("--unity-version", unity_version);
            var exactIds = new HashSet<long> { id };
            var result = await McpJobRunner.RunAsync(args.Build(), ExactIdFilter(exactIds, source_file), ct: ct);
            if (!result.Success)
                return ErrorResult($"{result.Error}\n\n{result.Log}");

            var files = Directory.Exists(tempDir)
                ? Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories)
                : Array.Empty<string>();
            if (files.Length == 0)
                return ErrorResult($"No exportable asset with path_id {path_id} was found.\n\n{result.Log}");

            var content = new List<ContentBlock>();
            foreach (var file in files)
            {
                var ext = Path.GetExtension(file);
                if (ext.Equals(".png", StringComparison.OrdinalIgnoreCase))
                {
                    content.Add(ImageContentBlock.FromBytes(await File.ReadAllBytesAsync(file, ct), "image/png"));
                }
                else if (PreviewTextExtensions.Contains(ext))
                {
                    var text = await File.ReadAllTextAsync(file, ct);
                    if (text.Length > max_text_length)
                        text = text.Substring(0, Math.Max(max_text_length, 0)) + "\n...(truncated)";
                    content.Add(new TextContentBlock { Text = $"{Path.GetFileName(file)}:\n{text}" });
                }
                else
                {
                    content.Add(new TextContentBlock { Text = $"Exported to {file} ({new FileInfo(file).Length} bytes); no inline preview for {ext} files." });
                }
            }
            return new CallToolResult { Content = content };
        }

        [McpServerTool(Name = "run_cli"), Description("Advanced: run AssetStudioModCLI with raw command-line arguments (first argument is the input path). Use for options not covered by the other tools, e.g. [\"C:/game/data\", \"-m\", \"export\", \"-t\", \"audio\", \"-o\", \"C:/out\", \"--block-comp\", \"lz4\"]. Returns the CLI log.")]
        public static async Task<object> RunCli(
            [Description("CLI arguments, one per array element.")] string[] args,
            CancellationToken ct = default)
        {
            if (args == null || args.Length == 0)
                return Error("args must contain at least the input path.");
            var result = await McpJobRunner.RunAsync(args, ct: ct);
            return Response(result);
        }

        private static async Task<object> RunExport(ArgsBuilder args, string outputFolder, Func<object> afterFilter, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(outputFolder))
                return Error("output_folder is required.");
            var fullOutput = Path.GetFullPath(outputFolder);
            var startUtc = DateTime.UtcNow.AddSeconds(-1);
            var result = await McpJobRunner.RunAsync(args.Build(), afterFilter, ct: ct);
            var files = McpJobRunner.CollectNewFiles(fullOutput, startUtc, 50, out var total);
            return new
            {
                success = result.Success,
                error = result.Error,
                output_folder = fullOutput,
                files_written = total,
                sample_files = files,
                log = LogFor(result),
            };
        }

        // CLI's --filter-by-pathid is a substring match; narrow it to exact ids
        private static Func<object> ExactIdFilter(HashSet<long> ids, string sourceFile = null)
        {
            var hasIds = ids != null && ids.Count > 0;
            var hasFile = !string.IsNullOrWhiteSpace(sourceFile);
            if (!hasIds && !hasFile)
                return null;
            return () =>
            {
                Studio.parsedAssetsList = Studio.parsedAssetsList.FindAll(x =>
                    (!hasIds || ids.Contains(x.m_PathID)) &&
                    (!hasFile || string.Equals(x.SourceFile.fileName, sourceFile, StringComparison.OrdinalIgnoreCase)));
                return null;
            };
        }

        private static HashSet<long> ParsePathIds(string[] pathIds, out string error)
        {
            error = null;
            if (pathIds == null || pathIds.Length == 0)
                return null;
            var ids = new HashSet<long>();
            foreach (var value in pathIds)
            {
                if (!long.TryParse(value, out var id))
                {
                    error = $"Invalid path_id \"{value}\".";
                    return null;
                }
                ids.Add(id);
            }
            return ids;
        }

        private static object Response(JobResult result) => new
        {
            success = result.Success,
            error = result.Error,
            result = result.Data,
            log = LogFor(result),
        };

        // Successful runs only need the tail (summary lines); failures get the full captured log
        private static string LogFor(JobResult result)
        {
            const int successLogLength = 1500;
            if (!result.Success || result.Log == null || result.Log.Length <= successLogLength)
                return result.Log;
            return "..." + result.Log.Substring(result.Log.Length - successLogLength);
        }

        private static object Error(string message) => new { success = false, error = message };

        private static CallToolResult ErrorResult(string message) => new CallToolResult
        {
            IsError = true,
            Content = new List<ContentBlock> { new TextContentBlock { Text = message } },
        };

        private sealed class ArgsBuilder
        {
            private readonly List<string> args = new List<string>();

            public ArgsBuilder(string inputPath)
            {
                args.Add(inputPath ?? string.Empty);
            }

            public ArgsBuilder Add(string option, string value)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    args.Add(option);
                    args.Add(value);
                }
                return this;
            }

            public ArgsBuilder Flag(string flag, bool enabled)
            {
                if (enabled)
                    args.Add(flag);
                return this;
            }

            public ArgsBuilder Mode(string mode) => Add("-m", mode);

            public ArgsBuilder Output(string folder) => Add("-o", folder);

            public ArgsBuilder Types(string[] types) => Add("-t", Join(types));

            public ArgsBuilder Filters(string[] names, string[] containers, bool regex, HashSet<long> pathIds = null)
            {
                Add("--filter-by-name", Join(names));
                Add("--filter-by-container", Join(containers));
                if (pathIds != null && pathIds.Count > 0)
                    Add("--filter-by-pathid", string.Join(",", pathIds));
                return Flag("--filter-with-regex", regex && (names?.Length > 0 || containers?.Length > 0));
            }

            public string[] Build() => args.ToArray();

            private static string Join(string[] values)
            {
                var list = values?.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
                return list == null || list.Length == 0 ? null : string.Join(",", list);
            }
        }
    }
}
