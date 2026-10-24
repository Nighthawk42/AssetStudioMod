using System;
using System.IO;
using SkiaSharp;

namespace AssetStudio
{
    public static class Texture2DExtensions
    {
        public static SKBitmap ConvertToImage(this Texture2D m_Texture2D, bool flip)
        {
            var converter = new Texture2DConverter(m_Texture2D);
            var buff = BigArrayPool<byte>.Shared.Rent(converter.OutputDataSize);
            var spanBuff = buff.AsSpan(0, converter.OutputDataSize);
            try
            {
                if (!converter.DecodeTexture2D(buff))
                    return null;

                // Switch textures are decoded at their padded size and cropped here
                var sourceWidth = converter.UsesSwitchSwizzle
                    ? converter.GetUncroppedSize().Width
                    : m_Texture2D.m_Width;
                return ImageExtensions.CreateBgraBitmap(spanBuff, sourceWidth, m_Texture2D.m_Width, m_Texture2D.m_Height, flip);
            }
            finally
            {
                BigArrayPool<byte>.Shared.Return(buff, clearArray: true);
            }
        }

        public static MemoryStream ConvertToStream(this Texture2D m_Texture2D, ImageFormat imageFormat, bool flip)
        {
            var image = ConvertToImage(m_Texture2D, flip);
            if (image != null)
            {
                using (image)
                {
                    return image.ConvertToStream(imageFormat);
                }
            }
            return null;
        }
    }
}
