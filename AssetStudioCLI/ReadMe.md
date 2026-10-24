## AssetStudioModCLI
CLI version of AssetStudioMod.
- Supported asset types for export: `Texture2D`, `Texture2DArray`, `Sprite`, `TextAsset`, `MonoBehaviour`, `Font`, `Shader`, `MovieTexture`, `AudioClip`, `VideoClip`, `Mesh`, `Animator`.

### Usage
```
AssetStudioModCLI <input path to asset file(s)/folder> [-m, --mode <value>]
                      [-t, --asset-type <value(s)>] [-g, --group-option <value>]
                      [-f, --filename-format <value>] [-o, --output <path>]
                      [-r, --overwrite-existing] [-h, --help]
                      [--log-level <value>] [--log-output <value>]
                      [--image-format <value>] [--audio-format <value>]
                      [--l2d-group-option <value>] [--l2d-motion-mode <value>]
                      [--l2d-search-by-filename] [--l2d-force-bezier]
                      [--model-scale-factor <value>] [--model-animation <value>]
                      [--filter-by-name <text>] [--filter-by-container <text>]
                      [--filter-by-pathid <text>] [--filter-by-text <text>]
                      [--filter-with-regex] [--blockinfo-comp <value>]
                      [--block-comp <value>] [--max-export-tasks <value>]
                      [--export-asset-list <value>] [--assembly-folder <path>]
                      [--unity-version <text>] [--decompress-to-disk]
                      [--not-restore-extension] [--ignore-typetree]
                      [--load-all]

General Options:
  -m, --mode <value>            Specify working mode
                                <Value: extract | export(default) | exportRaw | dump | info | live2d |
                                splitObjects | animator>
                                Extract - Extract(Decompress) asset bundles
                                Export - Convert and export assets
                                ExportRaw - Export raw assets
                                Dump - Generate json dumps of loaded asset
                                Info - Load file(s) and show the number of available for export assets
                                Live2D - Export Live2D Cubism models
                                SplitObjects - Export all model objects (split) (glTF)
                                Animator - Export Animator assets (glTF)
                                Example: "-m info"

  -t, --asset-type <value(s)>   Specify asset type(s) to export
                                <Value(s): tex2d, tex2dArray, sprite, textAsset, monoBehaviour, font, shader
                                movieTexture, audio, video, mesh | all(default)>
                                All - Export all asset types listed in the values
                                *To specify multiple asset types, write them separated by ',' or ';' without spaces
                                Examples: "-t sprite" or "-t tex2d,sprite,audio" or "-t tex2d;sprite;font"

  -g, --group-option <value>    Specify the way in which exported assets should be grouped
                                <Value: none | type | container(default) | containerFull | fileName | sceneHierarchy>
                                None - Do not group exported assets
                                Type - Group exported assets by type name
                                Container - Group exported assets by container path
                                ContainerFull - Group exported assets by full container path (e.g. with prefab name)
                                SceneHierarchy - Group exported assets by their node path in scene hierarchy
                                FileName - Group exported assets by source file name
                                Example: "-g containerFull"

  -f, --filename-format <value> Specify the file name format for exported assets
                                <Value: assetName(default) | assetName_pathID | pathID>
                                AssetName - Asset file names will look like "assetName.extension"
                                AssetName_pathID - Asset file names will look like "assetName @pathID.extension"
                                PathID - Asset file names will look like "pathID.extension"
                                Example: "-f assetName_pathID"

  -o, --output <path>           Specify path to the output folder
                                If path isn't specified, 'ASExport' folder will be created in the program's work folder

  -r, --overwrite-existing      (Flag) If specified, Studio will overwrite existing files during asset export/dump

  -h, --help                    Display help and exit

Logger Options:
  --log-level <value>           Specify the log level
                                <Value: verbose | debug | info(default) | warning | error>
                                Example: "--log-level warning"

  --log-output <value>          Specify the log output
                                <Value: console(default) | file | both>
                                Example: "--log-output both"

Convert Options:
  --image-format <value>        Specify the format for converting image assets
                                <Value: none | jpg | png(default) | bmp | tga | webp>
                                None - Do not convert images and export them as texture data (.tex)
                                Example: "--image-format jpg"

  --audio-format <value>        Specify the format for converting FMOD audio assets
                                <Value: none | wav(default)>
                                None - Do not convert FMOD audios and export them in their own format
                                Example: "--audio-format wav"

Live2D Options:
  --l2d-group-option <value>    Specify the way in which exported models should be grouped
                                <Value: container(default) | fileName | modelName>
                                Container - Group exported models by container path
                                FileName - Group exported models by source file name
                                ModelName - Group exported models by model name
                                Example: "--l2d-group-option modelName"

  --l2d-motion-mode <value>     Specify Live2D motion export mode
                                <Value: monoBehaviour(default) | animationClip>
                                MonoBehaviour - Try to export motions from MonoBehaviour Fade motions
                                If no Fade motions are found, the AnimationClip method will be used
                                AnimationClip - Try to export motions using AnimationClip assets
                                Example: "--l2d-motion-mode animationClip"

  --l2d-search-by-filename      (Flag) If specified, Studio will search for model-related Live2D assets by file name
                                rather than by container
                                (Preferred option if all l2d assets of a single model are stored in a single file
                                or containers are obfuscated)

  --l2d-force-bezier            (Flag) If specified, Linear motion segments will be calculated as Bezier segments
                                (May help if the exported motions look jerky/not smooth enough)

Model Options:
  Models are exported as binary glTF 2.0 (.glb) with embedded textures (png, or jpg with "--image-format jpg")

  --model-scale-factor <value>  Specify the scale factor for exported glTF models
                                <Value: float number from 0 to 100 (default=1)>
                                Example: "--model-scale-factor 50"

  --model-animation <value>     Specify the animation export mode for glTF models
                                <Value: auto(default) | skip | all>
                                Auto - Search for model-related animations and export model with them
                                Skip - Don't export animations
                                All - Try to bind all loaded animations to each loaded model
                                Example: "--model-animation skip"

Filter Options:
  --filter-by-name <text>       Specify the name or regexp by which assets should be filtered
                                *To specify multiple names write them separated by ',' or ';' without spaces
                                Example: "--filter-by-name char" or "--filter-by-name char,bg"

  --filter-by-container <text>  Specify the container or regexp by which assets should be filtered
                                *To specify multiple containers write them separated by ',' or ';' without spaces
                                Example: "--filter-by-container arts" or "--filter-by-container arts,icons"

  --filter-by-pathid <text>     Specify the PathID by which assets should be filtered
                                *To specify multiple PathIDs write them separated by ',' or ';' without spaces
                                Example: "--filter-by-pathid 7238605633795851352,-2430306240205277265"

  --filter-by-text <text>       Specify the text or regexp by which assets should be filtered
                                Looks for assets that contain the specified text in their names or containers
                                *To specify multiple values write them separated by ',' or ';' without spaces
                                Example: "--filter-by-text portrait" or "--filter-by-text portrait,art"

  --filter-with-regex           (Flag) If specified, the filter options will handle the specified text
                                as a regular expression (doesn't apply to --filter-by-pathid)

Advanced Options:
  --blockinfo-comp <value>      Specify the compression type of bundle's blockInfo data
                                <Value: auto(default) | zstd | oodle | lz4 | lzma>
                                Auto - Use compression type specified in an asset bundle
                                Zstd - Try to decompress as zstd archive
                                Oodle - Try to decompress as oodle archive
                                Lz4 - Try to decompress as lz4/lz4hc archive
                                Lzma - Try to decompress as lzma archive
                                Example: "--blockinfo-comp lz4"

  --block-comp <value>          Specify the compression type of bundle's block data
                                <Value: auto(default) | zstd | oodle | lz4 | lzma>
                                Auto - Use compression type specified in an asset bundle
                                Zstd - Try to decompress as zstd archive
                                Oodle - Try to decompress as oodle archive
                                Lz4 - Try to decompress as lz4/lz4hc archive
                                Lzma - Try to decompress as lzma archive
                                Example: "--block-comp zstd"

  --max-export-tasks <value>    Specify the number of parallel tasks for asset export
                                <Value: integer number from 1 to max number of cores (default=max)>
                                Max - Number of cores in your CPU
                                Example: "--max-export-tasks 8"

  --export-asset-list <value>   Specify the format in which you want to export asset list
                                <Value: none(default) | xml>
                                None - Do not export asset list
                                Example: "--export-asset-list xml"

  --assembly-folder <path>      Specify the path to the assembly folder

  --unity-version <text>        Specify Unity version
                                Example: "--unity-version 2017.4.39f1"

  --decompress-to-disk          (Flag) If not specified, only bundles larger than 2GB will be decompressed to disk
                                instead of RAM

  --not-restore-extension       (Flag) If specified, Studio will not try to use/restore original TextAsset extension,
                                and will just export all TextAssets with the ".txt" extension

  --ignore-typetree             (Flag) If specified, Studio will not try to parse assets at load time
                                using their type tree

  --load-all                    (Flag) If specified, Studio will load assets of all types
                                (Only for Dump, Info and ExportRaw modes)
```

### MCP server

`AssetStudioModCLI mcp` starts a [Model Context Protocol](https://modelcontextprotocol.io) server over stdio,
so AI agents (Claude Code, Claude Desktop, etc.) can inspect and extract Unity assets.

Claude Code:
```
claude mcp add assetstudio -- "C:\path\to\AssetStudioModCLI.exe" mcp
```
Claude Desktop / other clients (`mcpServers` config):
```json
{
  "mcpServers": {
    "assetstudio": {
      "command": "C:\path\to\AssetStudioModCLI.exe",
      "args": ["mcp"]
    }
  }
}
```
For portable builds use `"command": "dotnet"` and `"args": ["C:\path\to\AssetStudioModCLI.dll", "mcp"]`.

Tools:
| Tool | Description |
|---|---|
| `inspect_assets` | Unity version and number of exportable assets per type |
| `list_assets` | Paged asset list (name, type, container, source file, size, path_id) with name/container filters |
| `export_assets` | Export/convert assets (images, wav, obj, text, json, ...), raw export, or json dump |
| `export_models` | Export Animator models or split scene objects as glTF (.glb) |
| `export_live2d` | Export Live2D Cubism models |
| `extract_bundles` | Decompress bundles into their contained files |
| `preview_asset` | Return a texture/sprite as an inline PNG, or a text asset's content |
| `run_cli` | Run the CLI with raw arguments (for options not covered above) |

Notes:
- Each call reloads the input files; pass `asset_types` to keep this fast on large games. Calls run one at a time.
- Path IDs are only unique per file. Pass `source_file` (from `list_assets`) together with `path_ids` / `path_id`.
