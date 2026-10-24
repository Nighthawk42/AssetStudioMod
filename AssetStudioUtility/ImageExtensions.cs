using SkiaSharp;
using System;
using System.Buffers.Binary;
using System.IO;

namespace AssetStudio
{
    public static class ImageExtensions
    {
        // All images are kept as unpremultiplied BGRA, so color data of fully transparent pixels survives export
        public static SKBitmap CreateBgraBitmap(int width, int height)
        {
            return new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul));
        }

        // Copies a w*h region from the top-left of a BGRA buffer with the given source width
        public static SKBitmap CreateBgraBitmap(ReadOnlySpan<byte> bgra, int sourceWidth, int width, int height, bool flipVertical)
        {
            var bitmap = CreateBgraBitmap(width, height);
            var dst = bitmap.GetPixelSpan();
            var rowLength = width * 4;
            var srcStride = sourceWidth * 4;
            for (var y = 0; y < height; y++)
            {
                var srcY = flipVertical ? height - 1 - y : y;
                bgra.Slice(srcY * srcStride, rowLength).CopyTo(dst.Slice(y * rowLength, rowLength));
            }
            return bitmap;
        }

        public static SKBitmap Crop(this SKBitmap image, SKRectI rect)
        {
            var result = CreateBgraBitmap(rect.Width, rect.Height);
            var src = image.GetPixelSpan();
            var dst = result.GetPixelSpan();
            var rowLength = rect.Width * 4;
            for (var y = 0; y < rect.Height; y++)
            {
                src.Slice(((rect.Top + y) * image.Width + rect.Left) * 4, rowLength).CopyTo(dst.Slice(y * rowLength, rowLength));
            }
            return result;
        }

        public static void FlipVertical(this SKBitmap image)
        {
            var pixels = image.GetPixelSpan();
            var rowLength = image.Width * 4;
            var temp = new byte[rowLength];
            for (int top = 0, bottom = image.Height - 1; top < bottom; top++, bottom--)
            {
                var topRow = pixels.Slice(top * rowLength, rowLength);
                var bottomRow = pixels.Slice(bottom * rowLength, rowLength);
                topRow.CopyTo(temp);
                bottomRow.CopyTo(topRow);
                temp.CopyTo(bottomRow);
            }
        }

        public static void FlipHorizontal(this SKBitmap image)
        {
            var pixels = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(image.GetPixelSpan());
            for (var y = 0; y < image.Height; y++)
            {
                pixels.Slice(y * image.Width, image.Width).Reverse();
            }
        }

        public static void Rotate180(this SKBitmap image)
        {
            System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(image.GetPixelSpan()).Reverse();
        }

        // Rotates 90 degrees counter-clockwise (= 270 degrees clockwise)
        public static SKBitmap Rotate270(this SKBitmap image)
        {
            var width = image.Width;
            var height = image.Height;
            var result = CreateBgraBitmap(height, width);
            var src = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(image.GetPixelSpan());
            var dst = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(result.GetPixelSpan());
            for (var y = 0; y < width; y++)
            {
                for (var x = 0; x < height; x++)
                {
                    dst[y * height + x] = src[x * width + (width - 1 - y)];
                }
            }
            return result;
        }

        public static SKBitmap ResizeTo(this SKBitmap image, int width, int height, bool nearestNeighbor = false)
        {
            var sampling = nearestNeighbor
                ? new SKSamplingOptions(SKFilterMode.Nearest)
                : new SKSamplingOptions(SKCubicResampler.CatmullRom);
            var result = CreateBgraBitmap(width, height);
            if (!image.ScalePixels(result, sampling))
            {
                result.Dispose();
                throw new InvalidOperationException($"Failed to resize image to {width}x{height}.");
            }
            return result;
        }

        public static void WriteToStream(this SKBitmap image, Stream stream, ImageFormat imageFormat)
        {
            switch (imageFormat)
            {
                case ImageFormat.Jpeg:
                    Encode(image, stream, new SKJpegEncoderOptions(90, SKJpegEncoderDownsample.Downsample420, SKJpegEncoderAlphaOption.Ignore));
                    break;
                case ImageFormat.Png:
                    Encode(image, stream, new SKPngEncoderOptions(SKPngEncoderFilterFlags.AllFilters, 6));
                    break;
                case ImageFormat.Bmp:
                    WriteBmp(image, stream);
                    break;
                case ImageFormat.Tga:
                    WriteTga(image, stream);
                    break;
                case ImageFormat.Webp:
                    Encode(image, stream, new SKWebpEncoderOptions(SKWebpEncoderCompression.Lossless, 50));
                    break;
            }
        }

        public static MemoryStream ConvertToStream(this SKBitmap image, ImageFormat imageFormat)
        {
            var stream = new MemoryStream();
            image.WriteToStream(stream, imageFormat);
            return stream;
        }

        private static void Encode(SKBitmap image, Stream stream, SKJpegEncoderOptions options)
        {
            using var pixmap = image.PeekPixels();
            if (!pixmap.Encode(stream, options))
                throw new IOException("Failed to encode JPEG image.");
        }

        private static void Encode(SKBitmap image, Stream stream, SKPngEncoderOptions options)
        {
            using var pixmap = image.PeekPixels();
            if (!pixmap.Encode(stream, options))
                throw new IOException("Failed to encode PNG image.");
        }

        private static void Encode(SKBitmap image, Stream stream, SKWebpEncoderOptions options)
        {
            using var pixmap = image.PeekPixels();
            if (!pixmap.Encode(stream, options))
                throw new IOException("Failed to encode WebP image.");
        }

        // 32-bit BMP with alpha channel (BITMAPV4HEADER, BI_BITFIELDS), bottom-up rows
        private static void WriteBmp(SKBitmap image, Stream stream)
        {
            const int fileHeaderSize = 14;
            const int infoHeaderSize = 108;
            var pixelDataSize = image.Width * image.Height * 4;
            var header = new byte[fileHeaderSize + infoHeaderSize];
            var span = header.AsSpan();
            span[0] = (byte)'B';
            span[1] = (byte)'M';
            BinaryPrimitives.WriteInt32LittleEndian(span[2..], header.Length + pixelDataSize);
            BinaryPrimitives.WriteInt32LittleEndian(span[10..], header.Length);
            var info = span[fileHeaderSize..];
            BinaryPrimitives.WriteInt32LittleEndian(info, infoHeaderSize);
            BinaryPrimitives.WriteInt32LittleEndian(info[4..], image.Width);
            BinaryPrimitives.WriteInt32LittleEndian(info[8..], image.Height);
            BinaryPrimitives.WriteInt16LittleEndian(info[12..], 1); // planes
            BinaryPrimitives.WriteInt16LittleEndian(info[14..], 32); // bpp
            BinaryPrimitives.WriteInt32LittleEndian(info[16..], 3); // BI_BITFIELDS
            BinaryPrimitives.WriteInt32LittleEndian(info[20..], pixelDataSize);
            BinaryPrimitives.WriteInt32LittleEndian(info[24..], 2835); // 72 DPI
            BinaryPrimitives.WriteInt32LittleEndian(info[28..], 2835);
            BinaryPrimitives.WriteUInt32LittleEndian(info[40..], 0x00FF0000); // R mask
            BinaryPrimitives.WriteUInt32LittleEndian(info[44..], 0x0000FF00); // G mask
            BinaryPrimitives.WriteUInt32LittleEndian(info[48..], 0x000000FF); // B mask
            BinaryPrimitives.WriteUInt32LittleEndian(info[52..], 0xFF000000); // A mask
            BinaryPrimitives.WriteUInt32LittleEndian(info[56..], 0x73524742); // LCS_sRGB
            stream.Write(header);

            var pixels = image.GetPixelSpan();
            var rowLength = image.Width * 4;
            for (var y = image.Height - 1; y >= 0; y--)
            {
                stream.Write(pixels.Slice(y * rowLength, rowLength));
            }
        }

        // Uncompressed 32-bit true-color TGA, top-left origin
        private static void WriteTga(SKBitmap image, Stream stream)
        {
            var header = new byte[18];
            header[2] = 2; // uncompressed true-color
            BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(12), (short)image.Width);
            BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(14), (short)image.Height);
            header[16] = 32;
            header[17] = 0x28; // 8 alpha bits, top-left origin
            stream.Write(header);
            stream.Write(image.GetPixelSpan());
        }
    }
}
