using SharpGLTF.Memory;
using SharpGLTF.Schema2;
using SharpGLTF.Transforms;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;
using NVector4 = System.Numerics.Vector4;
using NQuaternion = System.Numerics.Quaternion;
using NMatrix4x4 = System.Numerics.Matrix4x4;
using GNode = SharpGLTF.Schema2.Node;
using GMaterial = SharpGLTF.Schema2.Material;
using GAnimation = SharpGLTF.Schema2.Animation;

namespace AssetStudio
{
    public sealed class ModelExportSettings
    {
        public bool ExportAllNodes { get; set; } = true;
        public bool ExportSkins { get; set; } = true;
        public bool ExportAnimations { get; set; } = true;
        public bool ExportBlendShape { get; set; } = true;
        public float ScaleFactor { get; set; } = 1.0f;

        public static ModelExportSettings FromBase64(string base64String)
        {
            var settingsData = Convert.FromBase64String(base64String);
            return JsonSerializer.Deserialize<ModelExportSettings>(settingsData);
        }

        public string ToBase64()
        {
            return Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(this));
        }
    }

    // Writes IImported models as binary glTF 2.0 (.glb).
    // IImported data is already right-handed (X mirrored from Unity), which matches glTF's coordinate system.
    public sealed class GltfExporter
    {
        private readonly IImported imported;
        private readonly ModelExportSettings settings;
        private readonly ModelRoot model = ModelRoot.CreateModel();
        private readonly Dictionary<ImportedFrame, GNode> frameToNode = new Dictionary<ImportedFrame, GNode>();
        private readonly Dictionary<string, GMaterial> materials = new Dictionary<string, GMaterial>();
        private readonly Dictionary<string, Image> images = new Dictionary<string, Image>();
        // mesh path -> (node, morph channel names) for blend shape animation
        private readonly Dictionary<string, (GNode node, List<string> channels)> morphNodes = new Dictionary<string, (GNode, List<string>)>();
        private HashSet<string> exportFramePaths;
        private GMaterial defaultMaterial;

        private GltfExporter(IImported imported, ModelExportSettings settings)
        {
            this.imported = imported;
            this.settings = settings ?? new ModelExportSettings();
        }

        public static void Export(string path, IImported imported, ModelExportSettings settings)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var exporter = new GltfExporter(imported, settings);
            exporter.Build();
            exporter.model.SaveGLB(path, new WriteSettings { MergeBuffers = true });
        }

        private void Build()
        {
            var scene = model.UseScene("Scene");
            if (imported.RootFrame == null)
                return;

            if (!settings.ExportAllNodes)
            {
                exportFramePaths = SearchHierarchy();
            }

            GNode rootNode;
            if (Math.Abs(settings.ScaleFactor - 1f) > float.Epsilon && settings.ScaleFactor > 0)
            {
                // Scaling a wrapper node scales meshes, skins and animations consistently
                var scaleNode = scene.CreateNode("ScaleFactor");
                scaleNode.LocalTransform = new AffineTransform(new NVector3(settings.ScaleFactor), NQuaternion.Identity, NVector3.Zero);
                rootNode = CreateNodeTree(scaleNode.CreateNode(imported.RootFrame.Name), imported.RootFrame);
            }
            else
            {
                rootNode = CreateNodeTree(scene.CreateNode(imported.RootFrame.Name), imported.RootFrame);
            }

            if (imported.MeshList?.Count > 0)
            {
                foreach (var (frame, node) in frameToNode.ToList())
                {
                    var mesh = ImportedHelpers.FindMesh(frame.Path, imported.MeshList);
                    if (mesh != null)
                    {
                        ExportMesh(node, mesh, rootNode);
                    }
                }
            }

            if (settings.ExportAnimations && imported.AnimationList?.Count > 0)
            {
                foreach (var animation in imported.AnimationList)
                {
                    ExportAnimation(animation);
                }
            }
        }

        private GNode CreateNodeTree(GNode node, ImportedFrame frame)
        {
            frameToNode[frame] = node;
            node.LocalTransform = new AffineTransform(ToNumerics(frame.LocalScale), ToNumerics(frame.LocalRotation), ToNumerics(frame.LocalPosition));
            for (var i = 0; i < frame.Count; i++)
            {
                var child = frame[i];
                if (exportFramePaths != null && !exportFramePaths.Contains(child.Path))
                    continue;
                CreateNodeTree(node.CreateNode(child.Name), child);
            }
            return node;
        }

        #region Mesh
        private void ExportMesh(GNode node, ImportedMesh iMesh, GNode skeletonRoot)
        {
            var vertices = iMesh.VertexList;
            if (vertices == null || vertices.Count == 0 || iMesh.SubmeshList == null || iMesh.SubmeshList.Count == 0)
                return;
            var vertexCount = vertices.Count;
            var mesh = model.CreateMesh(node.Name);

            var attributes = new Dictionary<string, Accessor>
            {
                ["POSITION"] = CreateVertexAccessor(vertices.Select(v => ToNumerics(v.Vertex)).ToArray(), DimensionType.VEC3)
            };
            if (iMesh.hasNormal)
            {
                attributes["NORMAL"] = CreateVertexAccessor(vertices.Select(v => SafeNormalize(ToNumerics(v.Normal))).ToArray(), DimensionType.VEC3);
            }
            if (iMesh.hasTangent && iMesh.hasNormal)
            {
                attributes["TANGENT"] = CreateVertexAccessor(vertices.Select(v =>
                {
                    var t = SafeNormalize(new NVector3(v.Tangent.X, v.Tangent.Y, v.Tangent.Z), NVector3.UnitX);
                    // Mirroring X flips the handedness of the tangent frame
                    return new NVector4(t, v.Tangent.W < 0 ? 1f : -1f);
                }).ToArray(), DimensionType.VEC4);
            }
            var texCoordIndex = 0;
            for (var uv = 0; uv < iMesh.hasUV.Length; uv++)
            {
                if (!iMesh.hasUV[uv])
                    continue;
                var index = uv;
                // glTF texture coordinates start at the top-left corner
                attributes[$"TEXCOORD_{texCoordIndex++}"] = CreateVertexAccessor(vertices.Select(v => v.UV[index] != null ? new NVector2(v.UV[index][0], 1f - v.UV[index][1]) : NVector2.Zero).ToArray(), DimensionType.VEC2);
            }
            if (iMesh.hasColor)
            {
                attributes["COLOR_0"] = CreateVertexAccessor(vertices.Select(v => new NVector4(v.Color.R, v.Color.G, v.Color.B, v.Color.A)).ToArray(), DimensionType.VEC4);
            }

            Skin skin = null;
            if (settings.ExportSkins && iMesh.BoneList?.Count > 0)
            {
                skin = CreateSkin(iMesh, vertices, attributes, skeletonRoot);
            }

            List<string> targetNames = null;
            var morphTargets = settings.ExportBlendShape ? CreateMorphTargets(iMesh, vertexCount, out targetNames) : null;

            foreach (var submesh in iMesh.SubmeshList)
            {
                if (submesh.FaceList == null || submesh.FaceList.Count == 0)
                    continue;
                var indices = new uint[submesh.FaceList.Count * 3];
                var i = 0;
                foreach (var face in submesh.FaceList)
                {
                    indices[i++] = (uint)(face.VertexIndices[0] + submesh.BaseVertex);
                    indices[i++] = (uint)(face.VertexIndices[1] + submesh.BaseVertex);
                    indices[i++] = (uint)(face.VertexIndices[2] + submesh.BaseVertex);
                }

                var primitive = mesh.CreatePrimitive();
                foreach (var (name, accessor) in attributes)
                {
                    primitive.SetVertexAccessor(name, accessor);
                }
                primitive.SetIndexAccessor(CreateIndexAccessor(indices));
                primitive.Material = GetMaterial(submesh.Material, texCoordIndex > 0);

                if (morphTargets != null)
                {
                    for (var t = 0; t < morphTargets.Count; t++)
                    {
                        primitive.SetMorphTargetAccessors(t, morphTargets[t]);
                    }
                }
            }

            if (mesh.Primitives.Count == 0)
                return;

            // glTF ignores the transform of skinned mesh nodes, so they go on an untransformed node at the scene root
            var meshNode = skin != null ? model.DefaultScene.CreateNode(node.Name) : node;
            meshNode.Mesh = mesh;
            if (skin != null)
            {
                meshNode.Skin = skin;
            }

            if (morphTargets != null)
            {
                mesh.SetMorphWeights(new float[morphTargets.Count]);
                // Blender and three.js read target names from mesh extras
                mesh.Extras = new JsonObject { ["targetNames"] = new JsonArray(targetNames.Select(x => (JsonNode)x).ToArray()) };
                morphNodes[iMesh.Path] = (meshNode, targetNames);
            }
        }

        private Skin CreateSkin(ImportedMesh iMesh, List<ImportedVertex> vertices, Dictionary<string, Accessor> attributes, GNode skeletonRoot)
        {
            var boneList = iMesh.BoneList;
            // glTF skins can't list a node twice, so bones resolving to the same node share one joint
            var joints = new List<(GNode node, NMatrix4x4 inverseBind)>();
            var nodeToJoint = new Dictionary<GNode, int>();
            var boneToJoint = new int[boneList.Count];
            for (var i = 0; i < boneList.Count; i++)
            {
                var bone = boneList[i];
                GNode jointNode = null;
                if (bone.Path != null)
                {
                    var frame = imported.RootFrame.FindFrameByPath(bone.Path);
                    if (frame != null)
                        frameToNode.TryGetValue(frame, out jointNode);
                }
                jointNode ??= skeletonRoot;

                if (!nodeToJoint.TryGetValue(jointNode, out var jointIndex))
                {
                    jointIndex = joints.Count;
                    nodeToJoint[jointNode] = jointIndex;
                    joints.Add((jointNode, GetInverseBindMatrix(bone.Matrix, jointNode)));
                }
                boneToJoint[i] = jointIndex;
            }

            var jointData = new ushort[vertices.Count * 4];
            var weightData = new NVector4[vertices.Count];
            for (var v = 0; v < vertices.Count; v++)
            {
                var vertex = vertices[v];
                Span<float> w = stackalloc float[4];
                Span<int> j = stackalloc int[4];
                var sum = 0f;
                if (vertex.BoneIndices != null && vertex.Weights != null)
                {
                    for (var k = 0; k < 4; k++)
                    {
                        var boneIndex = vertex.BoneIndices[k];
                        var weight = vertex.Weights[k];
                        if (weight > 0 && boneIndex >= 0 && boneIndex < boneToJoint.Length)
                        {
                            j[k] = boneToJoint[boneIndex];
                            w[k] = weight;
                            sum += weight;
                        }
                    }
                }
                if (sum <= 0)
                {
                    w[0] = 1f;
                    sum = 1f;
                }
                for (var k = 0; k < 4; k++)
                {
                    jointData[v * 4 + k] = (ushort)(w[k] > 0 ? j[k] : 0);
                }
                weightData[v] = new NVector4(w[0], w[1], w[2], w[3]) / sum;
            }

            attributes["JOINTS_0"] = CreateAccessor(MemoryMarshal.AsBytes(jointData.AsSpan()).ToArray(), vertices.Count, DimensionType.VEC4, EncodingType.UNSIGNED_SHORT);
            attributes["WEIGHTS_0"] = CreateVertexAccessor(weightData, DimensionType.VEC4);

            var skin = model.CreateSkin(iMesh.Path);
            skin.BindJoints(joints);
            return skin;
        }

        // Some Unity bind poses are degenerate (e.g. all zeros for unused bones); glTF requires invertible affine matrices
        private static NMatrix4x4 GetInverseBindMatrix(Matrix4x4 bindPose, GNode jointNode)
        {
            var matrix = ToNumerics(bindPose);
            matrix.M14 = 0;
            matrix.M24 = 0;
            matrix.M34 = 0;
            matrix.M44 = 1;
            var determinant = matrix.GetDeterminant();
            if (float.IsFinite(determinant) && MathF.Abs(determinant) > 1e-12f)
                return matrix;
            return NMatrix4x4.Invert(jointNode.WorldMatrix, out var inverse) ? inverse : NMatrix4x4.Identity;
        }

        private List<Dictionary<string, Accessor>> CreateMorphTargets(ImportedMesh iMesh, int vertexCount, out List<string> targetNames)
        {
            targetNames = null;
            var morph = imported.MorphList?.Find(x => x.Path == iMesh.Path);
            if (morph?.Channels == null || morph.Channels.Count == 0)
                return null;

            var targets = new List<Dictionary<string, Accessor>>();
            targetNames = new List<string>();
            foreach (var channel in morph.Channels)
            {
                // glTF has one target per channel: use the full-weight (last) frame
                var keyframe = channel.KeyframeList?.LastOrDefault();
                if (keyframe?.VertexList == null)
                    continue;

                var positions = new NVector3[vertexCount];
                var normals = keyframe.hasNormals && iMesh.hasNormal ? new NVector3[vertexCount] : null;
                foreach (var morphVertex in keyframe.VertexList)
                {
                    var index = (int)morphVertex.Index;
                    if (index >= vertexCount)
                        continue;
                    // morph positions are absolute; glTF targets are displacements
                    positions[index] = ToNumerics(morphVertex.Vertex.Vertex) - ToNumerics(iMesh.VertexList[index].Vertex);
                    if (normals != null)
                        normals[index] = ToNumerics(morphVertex.Vertex.Normal);
                }

                var target = new Dictionary<string, Accessor>
                {
                    ["POSITION"] = CreateVertexAccessor(positions, DimensionType.VEC3)
                };
                if (normals != null)
                {
                    target["NORMAL"] = CreateVertexAccessor(normals, DimensionType.VEC3);
                }
                targets.Add(target);
                targetNames.Add(channel.Name);
            }
            return targets.Count > 0 ? targets : null;
        }
        #endregion

        #region GMaterial
        private GMaterial GetMaterial(string name, bool hasTexCoords)
        {
            var iMat = string.IsNullOrEmpty(name) ? null : ImportedHelpers.FindMaterial(name, imported.MaterialList);
            if (iMat == null)
            {
                if (defaultMaterial == null)
                {
                    defaultMaterial = model.CreateMaterial("Default");
                    defaultMaterial.InitializePBRMetallicRoughness();
                    SetMetallicRoughness(defaultMaterial);
                }
                return defaultMaterial;
            }
            var materialKey = hasTexCoords ? iMat.Name : iMat.Name + "|untextured";
            if (materials.TryGetValue(materialKey, out var material))
                return material;

            material = model.CreateMaterial(iMat.Name);
            material.InitializePBRMetallicRoughness();
            SetMetallicRoughness(material);
            var diffuse = iMat.Diffuse;
            SetParameter(material, "BaseColor", "RGBA", new NVector4(diffuse.R, diffuse.G, diffuse.B, diffuse.A));
            if (diffuse.A < 1f)
                material.Alpha = AlphaMode.BLEND;

            var emissive = iMat.Emissive;
            if (emissive.R > 0 || emissive.G > 0 || emissive.B > 0)
            {
                SetParameter(material, "Emissive", "RGB", new NVector3(emissive.R, emissive.G, emissive.B));
            }

            // Textures need texture coordinates; meshes without UVs get an untextured variant
            if (hasTexCoords && iMat.Textures != null)
            {
                foreach (var texture in iMat.Textures)
                {
                    var channelName = texture.Dest switch
                    {
                        0 => "BaseColor",
                        1 or 3 => "Normal",
                        _ => null,
                    };
                    if (channelName == null)
                        continue;
                    var image = GetImage(texture.Name);
                    if (image == null)
                        continue;
                    var channel = material.FindChannel(channelName).Value;
                    if (channel.Texture != null)
                        continue;
                    channel.SetTexture(0, image);
                    var scale = new NVector2(texture.Scale.X, texture.Scale.Y);
                    var offset = new NVector2(texture.Offset.X, texture.Offset.Y);
                    if (scale != NVector2.One || offset != NVector2.Zero)
                    {
                        // Unity transforms UVs with a bottom-left origin; convert for the flipped V axis
                        channel.SetTransform(new NVector2(offset.X, 1f - scale.Y - offset.Y), scale, 0f, null);
                    }
                }
            }

            materials[materialKey] = material;
            return material;
        }

        private static void SetParameter(GMaterial material, string channelName, string parameterName, object value)
        {
            var parameter = material.FindChannel(channelName)?.Parameters.FirstOrDefault(x => x.Name == parameterName);
            if (parameter != null)
                parameter.Value = value;
        }

        private static void SetMetallicRoughness(GMaterial material)
        {
            var channel = material.FindChannel("MetallicRoughness").Value;
            channel.SetFactor("MetallicFactor", 0f);
            channel.SetFactor("RoughnessFactor", 1f);
        }

        private Image GetImage(string textureName)
        {
            if (string.IsNullOrEmpty(textureName))
                return null;
            if (images.TryGetValue(textureName, out var image))
                return image;

            var texture = ImportedHelpers.FindTexture(textureName, imported.TextureList);
            image = null;
            if (texture?.Data?.Length > 0)
            {
                var memoryImage = new MemoryImage(texture.Data);
                if (memoryImage.IsPng || memoryImage.IsJpg)
                {
                    image = model.UseImage(memoryImage);
                    image.Name = Path.GetFileNameWithoutExtension(textureName);
                }
                else
                {
                    Logger.Warning($"Texture \"{textureName}\" was skipped: glTF only supports PNG and JPEG textures.");
                }
            }
            images[textureName] = image;
            return image;
        }
        #endregion

        #region GAnimation
        private void ExportAnimation(ImportedKeyframedAnimation iAnim)
        {
            GAnimation animation = null;
            GAnimation GetAnimation() => animation ??= model.CreateAnimation(iAnim.Name ?? $"Take{model.LogicalAnimations.Count}");

            var blendShapeTracks = new Dictionary<string, List<ImportedBlendShape>>();
            foreach (var track in iAnim.TrackList)
            {
                if (track.Path == null)
                    continue;

                if (track.BlendShape != null && settings.ExportBlendShape)
                {
                    if (!blendShapeTracks.TryGetValue(track.Path, out var list))
                        blendShapeTracks[track.Path] = list = new List<ImportedBlendShape>();
                    list.Add(track.BlendShape);
                }

                var frame = imported.RootFrame.FindFrameByPath(track.Path);
                if (frame == null || !frameToNode.TryGetValue(frame, out var node))
                    continue;

                if (track.Translations.Count > 0)
                {
                    GetAnimation().CreateTranslationChannel(node, ToKeyframes(track.Translations, ToNumerics), true);
                }
                if (track.Rotations.Count > 0)
                {
                    GetAnimation().CreateRotationChannel(node, ToRotationKeyframes(track.Rotations), true);
                }
                if (track.Scalings.Count > 0)
                {
                    GetAnimation().CreateScaleChannel(node, ToKeyframes(track.Scalings, ToNumerics), true);
                }
            }

            foreach (var (path, shapes) in blendShapeTracks)
            {
                if (!morphNodes.TryGetValue(path, out var morphNode))
                    continue;
                var keyframes = SampleMorphWeights(shapes, morphNode.channels);
                if (keyframes.Count > 0)
                {
                    GetAnimation().CreateMorphChannel(morphNode.node, keyframes, morphNode.channels.Count, true);
                }
            }
        }

        // glTF animates all morph weights of a node in one channel, so merge per-channel curves on a shared timeline
        private static SortedDictionary<float, float[]> SampleMorphWeights(List<ImportedBlendShape> shapes, List<string> channelNames)
        {
            var result = new SortedDictionary<float, float[]>();
            var curves = new List<(int channel, List<ImportedKeyframe<float>> keys)>();
            foreach (var shape in shapes)
            {
                var channel = channelNames.IndexOf(shape.ChannelName);
                if (channel < 0 || shape.Keyframes.Count == 0)
                    continue;
                curves.Add((channel, shape.Keyframes.OrderBy(x => x.time).ToList()));
            }
            if (curves.Count == 0)
                return result;

            var times = curves.SelectMany(x => x.keys.Select(k => k.time)).Distinct().OrderBy(x => x);
            foreach (var time in times)
            {
                var weights = new float[channelNames.Count];
                foreach (var (channel, keys) in curves)
                {
                    // Unity blend shape weights are 0-100
                    weights[channel] = Math.Clamp(Evaluate(keys, time) / 100f, 0f, 1f);
                }
                result[time] = weights;
            }
            return result;
        }

        private static float Evaluate(List<ImportedKeyframe<float>> keys, float time)
        {
            if (time <= keys[0].time)
                return keys[0].value;
            for (var i = 1; i < keys.Count; i++)
            {
                if (time <= keys[i].time)
                {
                    var a = keys[i - 1];
                    var b = keys[i];
                    var span = b.time - a.time;
                    return span <= 0 ? b.value : a.value + (b.value - a.value) * ((time - a.time) / span);
                }
            }
            return keys[^1].value;
        }

        private static SortedDictionary<float, TOut> ToKeyframes<TIn, TOut>(List<ImportedKeyframe<TIn>> keys, Func<TIn, TOut> convert)
        {
            var result = new SortedDictionary<float, TOut>();
            foreach (var key in keys)
            {
                result[Math.Max(key.time, 0f)] = convert(key.value);
            }
            return result;
        }

        private static SortedDictionary<float, NQuaternion> ToRotationKeyframes(List<ImportedKeyframe<Quaternion>> keys)
        {
            var sorted = ToKeyframes(keys, q => NQuaternion.Normalize(ToNumerics(q)));
            // Keep consecutive keys in the same hemisphere so interpolation takes the short path
            var result = new SortedDictionary<float, NQuaternion>();
            var previous = NQuaternion.Identity;
            var first = true;
            foreach (var (time, value) in sorted)
            {
                var q = value;
                if (!first && NQuaternion.Dot(previous, q) < 0)
                    q = NQuaternion.Negate(q);
                result[time] = q;
                previous = q;
                first = false;
            }
            return result;
        }
        #endregion

        #region Accessors
        private Accessor CreateVertexAccessor<T>(T[] data, DimensionType dimension) where T : unmanaged
        {
            return CreateAccessor(MemoryMarshal.AsBytes(data.AsSpan()).ToArray(), data.Length, dimension, EncodingType.FLOAT);
        }

        private Accessor CreateAccessor(byte[] bytes, int count, DimensionType dimension, EncodingType encoding)
        {
            var view = model.UseBufferView(bytes, 0, null, 0, BufferMode.ARRAY_BUFFER);
            var accessor = model.CreateAccessor();
            accessor.SetVertexData(view, 0, count, new AttributeFormat(dimension, encoding, false));
            return accessor;
        }

        private Accessor CreateIndexAccessor(uint[] indices)
        {
            var view = model.UseBufferView(MemoryMarshal.AsBytes(indices.AsSpan()).ToArray(), 0, null, 0, BufferMode.ELEMENT_ARRAY_BUFFER);
            var accessor = model.CreateAccessor();
            accessor.SetIndexData(view, 0, indices.Length, IndexEncodingType.UNSIGNED_INT);
            return accessor;
        }
        #endregion

        #region Hierarchy
        // Frames needed by meshes: their ancestors and their bones' ancestors
        private HashSet<string> SearchHierarchy()
        {
            if (imported.MeshList == null || imported.MeshList.Count == 0)
                return null;

            var exportFrames = new HashSet<string>();
            var frameStack = new Stack<ImportedFrame>();
            frameStack.Push(imported.RootFrame);
            while (frameStack.Count > 0)
            {
                var frame = frameStack.Pop();
                var mesh = ImportedHelpers.FindMesh(frame.Path, imported.MeshList);
                if (mesh != null)
                {
                    for (var parent = frame; parent != null; parent = parent.Parent)
                        exportFrames.Add(parent.Path);

                    if (mesh.BoneList != null)
                    {
                        foreach (var bone in mesh.BoneList)
                        {
                            if (bone.Path == null || exportFrames.Contains(bone.Path))
                                continue;
                            for (var boneParent = imported.RootFrame.FindFrameByPath(bone.Path); boneParent != null; boneParent = boneParent.Parent)
                                exportFrames.Add(boneParent.Path);
                        }
                    }
                }
                for (var i = frame.Count - 1; i >= 0; i--)
                {
                    frameStack.Push(frame[i]);
                }
            }
            return exportFrames;
        }
        #endregion

        #region Conversion
        private static NVector3 ToNumerics(Vector3 v) => new NVector3(v.X, v.Y, v.Z);

        private static NQuaternion ToNumerics(Quaternion q)
        {
            var result = new NQuaternion(q.X, q.Y, q.Z, q.W);
            return result.LengthSquared() > 0 ? NQuaternion.Normalize(result) : NQuaternion.Identity;
        }

        // AssetStudio matrices use column vectors (translation in M03/M13/M23); System.Numerics uses row vectors
        private static NMatrix4x4 ToNumerics(Matrix4x4 m) => new NMatrix4x4(
            m.M00, m.M10, m.M20, m.M30,
            m.M01, m.M11, m.M21, m.M31,
            m.M02, m.M12, m.M22, m.M32,
            m.M03, m.M13, m.M23, m.M33);

        private static NVector3 SafeNormalize(NVector3 v) => SafeNormalize(v, NVector3.UnitY);

        private static NVector3 SafeNormalize(NVector3 v, NVector3 fallback)
        {
            var length = v.Length();
            return length > 1e-8f && float.IsFinite(length) ? v / length : fallback;
        }
        #endregion
    }
}
