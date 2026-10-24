using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;

namespace AssetStudio
{
    public enum SpriteMaskMode
    {
        Off,
        On,
        MaskOnly,
        Export
    }

    public static class SpriteHelper
    {
        public static SKBitmap GetImage(this Sprite m_Sprite, SpriteMaskMode spriteMaskMode = SpriteMaskMode.On)
        {
            if (m_Sprite.m_SpriteAtlas != null && m_Sprite.m_SpriteAtlas.TryGet(out var m_SpriteAtlas))
            {
                if (m_SpriteAtlas.m_RenderDataMap.TryGetValue(m_Sprite.m_RenderDataKey, out var spriteAtlasData) && spriteAtlasData.texture.TryGet(out var m_Texture2D))
                {
                    return CutImage(m_Sprite, m_Texture2D, spriteAtlasData.textureRect, spriteAtlasData.textureRectOffset, spriteAtlasData.downscaleMultiplier, spriteAtlasData.settingsRaw);
                }
            }
            else
            {
                if (m_Sprite.m_RD.texture.TryGet(out var m_Texture2D) && m_Sprite.m_RD.alphaTexture.TryGet(out var m_AlphaTexture2D) && spriteMaskMode != SpriteMaskMode.Off)
                {
                    SKBitmap tex = null;
                    if (spriteMaskMode != SpriteMaskMode.MaskOnly)
                    {
                        tex = CutImage(m_Sprite, m_Texture2D, m_Sprite.m_RD.textureRect, m_Sprite.m_RD.textureRectOffset, m_Sprite.m_RD.downscaleMultiplier, m_Sprite.m_RD.settingsRaw);
                    }
                    var alphaTex = CutImage(m_Sprite, m_AlphaTexture2D, m_Sprite.m_RD.textureRect, m_Sprite.m_RD.textureRectOffset, m_Sprite.m_RD.downscaleMultiplier, m_Sprite.m_RD.settingsRaw);

                    switch (spriteMaskMode)
                    {
                        case SpriteMaskMode.On:
                            tex.ApplyRGBMask(alphaTex, isPreview: true);
                            return tex;
                        case SpriteMaskMode.Export:
                            tex.ApplyRGBMask(alphaTex);
                            return tex;
                        case SpriteMaskMode.MaskOnly:
                            return alphaTex;
                    }
                }
                else if (m_Sprite.m_RD.texture.TryGet(out m_Texture2D))
                {
                    return CutImage(m_Sprite, m_Texture2D, m_Sprite.m_RD.textureRect, m_Sprite.m_RD.textureRectOffset, m_Sprite.m_RD.downscaleMultiplier, m_Sprite.m_RD.settingsRaw);
                }
            }
            return null;
        }

        private static void ApplyRGBMask(this SKBitmap tex, SKBitmap texMask, bool isPreview = false)
        {
            using (texMask)
            {
                var mask = texMask;
                if (tex.Width != texMask.Width || tex.Height != texMask.Height)
                {
                    mask = texMask.ResizeTo(tex.Width, tex.Height, nearestNeighbor: isPreview);
                }
                try
                {
                    var texPixels = tex.GetPixelSpan();
                    var maskPixels = mask.GetPixelSpan();
                    for (var i = 0; i < texPixels.Length; i += 4)
                    {
                        // BGRA: grayscale of the mask's RGB becomes the texture's alpha
                        texPixels[i + 3] = (byte)((maskPixels[i] + maskPixels[i + 1] + maskPixels[i + 2]) / 3);
                    }
                }
                finally
                {
                    if (!ReferenceEquals(mask, texMask))
                        mask.Dispose();
                }
            }
        }

        private static SKBitmap CutImage(Sprite m_Sprite, Texture2D m_Texture2D, Rectf textureRect, Vector2 textureRectOffset, float downscaleMultiplier, SpriteSettings settingsRaw)
        {
            var originalImage = m_Texture2D.ConvertToImage(false);
            if (originalImage != null)
            {
                if (downscaleMultiplier > 0f && downscaleMultiplier != 1f)
                {
                    var width = (int)(m_Texture2D.m_Width / downscaleMultiplier);
                    var height = (int)(m_Texture2D.m_Height / downscaleMultiplier);
                    var resized = originalImage.ResizeTo(width, height);
                    originalImage.Dispose();
                    originalImage = resized;
                }
                var rectX = (int)MathF.Floor(textureRect.x);
                var rectY = (int)MathF.Floor(textureRect.y);
                var rectRight = (int)MathF.Ceiling(textureRect.x + textureRect.width);
                var rectBottom = (int)MathF.Ceiling(textureRect.y + textureRect.height);
                rectRight = Math.Min(rectRight, originalImage.Width);
                rectBottom = Math.Min(rectBottom, originalImage.Height);
                var rect = new SKRectI(rectX, rectY, rectRight, rectBottom);
                var spriteImage = originalImage.Crop(rect);
                originalImage.Dispose();
                if (settingsRaw.packed == 1)
                {
                    //RotateAndFlip
                    switch (settingsRaw.packingRotation)
                    {
                        case SpritePackingRotation.FlipHorizontal:
                            spriteImage.FlipHorizontal();
                            break;
                        case SpritePackingRotation.FlipVertical:
                            spriteImage.FlipVertical();
                            break;
                        case SpritePackingRotation.Rotate180:
                            spriteImage.Rotate180();
                            break;
                        case SpritePackingRotation.Rotate90:
                            var rotated = spriteImage.Rotate270();
                            spriteImage.Dispose();
                            spriteImage = rotated;
                            break;
                    }
                }

                //Tight
                if (settingsRaw.packingMode == SpritePackingMode.Tight)
                {
                    try
                    {
                        var matrix = Matrix3x2.CreateScale(m_Sprite.m_PixelsToUnits);
                        matrix *= Matrix3x2.CreateTranslation(m_Sprite.m_Rect.width * m_Sprite.m_Pivot.X - textureRectOffset.X, m_Sprite.m_Rect.height * m_Sprite.m_Pivot.Y - textureRectOffset.Y);
                        var triangles = GetTriangles(m_Sprite.m_RD);
                        ClearOutsideTriangles(spriteImage, triangles, matrix);
                    }
                    catch (Exception e)
                    {
                        Logger.Warning($"{m_Sprite.m_Name} Unable to render the packed sprite correctly.\n{e}");
                    }
                }

                spriteImage.FlipVertical();
                return spriteImage;
            }

            return null;
        }

        // Makes every pixel outside the sprite mesh fully transparent
        private static void ClearOutsideTriangles(SKBitmap image, Vector2[][] triangles, Matrix3x2 matrix)
        {
            using var mask = new SKBitmap(new SKImageInfo(image.Width, image.Height, SKColorType.Alpha8, SKAlphaType.Premul));
            using (var canvas = new SKCanvas(mask))
            using (var paint = new SKPaint { IsAntialias = false, Color = SKColors.White, Style = SKPaintStyle.Fill })
            using (var pathBuilder = new SKPathBuilder())
            {
                canvas.Clear(SKColors.Transparent);
                // Each triangle is drawn on its own, so overlapping triangles with opposite winding still union
                foreach (var triangle in triangles)
                {
                    for (var i = 0; i < triangle.Length; i++)
                    {
                        var point = System.Numerics.Vector2.Transform(new System.Numerics.Vector2(triangle[i].X, triangle[i].Y), matrix);
                        if (i == 0)
                            pathBuilder.MoveTo(point.X, point.Y);
                        else
                            pathBuilder.LineTo(point.X, point.Y);
                    }
                    pathBuilder.Close();
                    using var path = pathBuilder.Detach();
                    canvas.DrawPath(path, paint);
                }
            }

            var maskPixels = mask.GetPixelSpan();
            var maskStride = mask.RowBytes;
            var pixels = MemoryMarshal.Cast<byte, uint>(image.GetPixelSpan());
            for (var y = 0; y < image.Height; y++)
            {
                var maskRow = maskPixels.Slice(y * maskStride, image.Width);
                var row = pixels.Slice(y * image.Width, image.Width);
                for (var x = 0; x < row.Length; x++)
                {
                    if (maskRow[x] == 0)
                        row[x] = 0;
                }
            }
        }

        private static Vector2[][] GetTriangles(SpriteRenderData m_RD)
        {
            if (m_RD.vertices != null) //5.6 down
            {
                var vertices = m_RD.vertices.Select(x => (Vector2)x.pos).ToArray();
                var triangleCount = m_RD.indices.Length / 3;
                var triangles = new Vector2[triangleCount][];
                for (int i = 0; i < triangleCount; i++)
                {
                    var first = m_RD.indices[i * 3];
                    var second = m_RD.indices[i * 3 + 1];
                    var third = m_RD.indices[i * 3 + 2];
                    var triangle = new[] { vertices[first], vertices[second], vertices[third] };
                    triangles[i] = triangle;
                }
                return triangles;
            }
            else //5.6 and up
            {
                var triangles = new List<Vector2[]>();
                var m_VertexData = m_RD.m_VertexData;
                var m_Channel = m_VertexData.m_Channels[0]; //kShaderChannelVertex
                var m_Stream = m_VertexData.m_Streams[m_Channel.stream];
                using (var vertexReader = new BinaryReader(new MemoryStream(m_VertexData.m_DataSize)))
                {
                    using (var indexReader = new BinaryReader(new MemoryStream(m_RD.m_IndexBuffer)))
                    {
                        foreach (var subMesh in m_RD.m_SubMeshes)
                        {
                            vertexReader.BaseStream.Position = m_Stream.offset + subMesh.firstVertex * m_Stream.stride + m_Channel.offset;

                            var vertices = new Vector2[subMesh.vertexCount];
                            for (int v = 0; v < subMesh.vertexCount; v++)
                            {
                                vertices[v] = vertexReader.ReadVector3();
                                vertexReader.BaseStream.Position += m_Stream.stride - 12;
                            }

                            indexReader.BaseStream.Position = subMesh.firstByte;

                            var triangleCount = subMesh.indexCount / 3u;
                            for (int i = 0; i < triangleCount; i++)
                            {
                                var first = indexReader.ReadUInt16() - subMesh.firstVertex;
                                var second = indexReader.ReadUInt16() - subMesh.firstVertex;
                                var third = indexReader.ReadUInt16() - subMesh.firstVertex;
                                var triangle = new[] { vertices[first], vertices[second], vertices[third] };
                                triangles.Add(triangle);
                            }
                        }
                    }
                }
                return triangles.ToArray();
            }
        }
    }
}
