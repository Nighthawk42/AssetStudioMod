namespace AssetStudio
{
    public static class ModelExporter
    {
        public static void ExportGltf(string path, IImported imported, ModelExportSettings settings) => GltfExporter.Export(path, imported, settings);
    }
}
