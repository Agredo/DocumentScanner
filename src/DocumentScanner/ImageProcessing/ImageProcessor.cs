using SkiaSharp;

namespace DocumentScanner.ImageProcessing;

/// <summary>
/// Provides low-level image processing operations.
/// </summary>
public static class ImageProcessor
{
    /// <summary>
    /// Loads an image from a byte array.
    /// </summary>
    public static SKBitmap LoadImage(byte[] imageBytes)
    {
        using var stream = new MemoryStream(imageBytes);
        var bitmap = SKBitmap.Decode(stream);

        if (bitmap == null)
            throw new ArgumentException("Invalid image data");

        // Ensure we have a workable pixel format
        if (bitmap.ColorType != SKColorType.Rgba8888 && bitmap.ColorType != SKColorType.Bgra8888)
        {
            var converted = new SKBitmap(bitmap.Width, bitmap.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(converted);
            canvas.DrawBitmap(bitmap, 0, 0);
            bitmap.Dispose();
            return converted;
        }

        return bitmap;
    }

    /// <summary>
    /// Saves a bitmap to a byte array in the specified format.
    /// </summary>
    public static byte[] SaveImage(SKBitmap bitmap, SKEncodedImageFormat format = SKEncodedImageFormat.Png, int quality = 100)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, quality);
        return data.ToArray();
    }

    /// <summary>
    /// Converts an image to grayscale.
    /// </summary>
    public static byte[,] ToGrayscale(SKBitmap bitmap)
    {
        int width = bitmap.Width;
        int height = bitmap.Height;
        var grayscale = new byte[height, width];
        var pixels = bitmap.GetPixels();

        unsafe
        {
            byte* ptr = (byte*)pixels.ToPointer();
            int bytesPerPixel = bitmap.BytesPerPixel;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int offset = (y * width + x) * bytesPerPixel;

                    // Handle both RGBA and BGRA formats
                    byte r, g, b;
                    if (bitmap.ColorType == SKColorType.Bgra8888)
                    {
                        b = ptr[offset];
                        g = ptr[offset + 1];
                        r = ptr[offset + 2];
                    }
                    else
                    {
                        r = ptr[offset];
                        g = ptr[offset + 1];
                        b = ptr[offset + 2];
                    }

                    // Luminosity method for grayscale conversion
                    grayscale[y, x] = (byte)(0.299f * r + 0.587f * g + 0.114f * b);
                }
            }
        }

        return grayscale;
    }

    /// <summary>
    /// Converts a grayscale array back to an SKBitmap.
    /// </summary>
    public static SKBitmap FromGrayscale(byte[,] grayscale)
    {
        int height = grayscale.GetLength(0);
        int width = grayscale.GetLength(1);
        var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque);
        var pixels = bitmap.GetPixels();

        unsafe
        {
            byte* ptr = (byte*)pixels.ToPointer();

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int offset = (y * width + x) * 4;
                    byte gray = grayscale[y, x];
                    ptr[offset] = gray;     // R
                    ptr[offset + 1] = gray; // G
                    ptr[offset + 2] = gray; // B
                    ptr[offset + 3] = 255;  // A
                }
            }
        }

        return bitmap;
    }

    /// <summary>
    /// Applies Gaussian blur to a grayscale image.
    /// </summary>
    public static byte[,] GaussianBlur(byte[,] image, int kernelSize = 5)
    {
        if (kernelSize % 2 == 0)
            kernelSize++;

        int height = image.GetLength(0);
        int width = image.GetLength(1);
        var result = new byte[height, width];

        // Generate Gaussian kernel
        float[,] kernel = GenerateGaussianKernel(kernelSize);
        int radius = kernelSize / 2;

        // Apply convolution
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float sum = 0;

                for (int ky = -radius; ky <= radius; ky++)
                {
                    for (int kx = -radius; kx <= radius; kx++)
                    {
                        int py = Math.Clamp(y + ky, 0, height - 1);
                        int px = Math.Clamp(x + kx, 0, width - 1);
                        sum += image[py, px] * kernel[ky + radius, kx + radius];
                    }
                }

                result[y, x] = (byte)Math.Clamp(sum, 0, 255);
            }
        }

        return result;
    }

    /// <summary>
    /// Generates a Gaussian kernel of the specified size.
    /// </summary>
    private static float[,] GenerateGaussianKernel(int size)
    {
        var kernel = new float[size, size];
        float sigma = size / 6.0f;
        float sum = 0;
        int radius = size / 2;

        for (int y = -radius; y <= radius; y++)
        {
            for (int x = -radius; x <= radius; x++)
            {
                float value = (float)Math.Exp(-(x * x + y * y) / (2 * sigma * sigma));
                kernel[y + radius, x + radius] = value;
                sum += value;
            }
        }

        // Normalize
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                kernel[y, x] /= sum;
            }
        }

        return kernel;
    }

    /// <summary>
    /// Enhances contrast using histogram equalization.
    /// </summary>
    public static byte[,] EnhanceContrast(byte[,] image)
    {
        int height = image.GetLength(0);
        int width = image.GetLength(1);
        var result = new byte[height, width];

        // Calculate histogram
        int[] histogram = new int[256];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                histogram[image[y, x]]++;
            }
        }

        // Calculate cumulative distribution function
        int[] cdf = new int[256];
        cdf[0] = histogram[0];
        for (int i = 1; i < 256; i++)
        {
            cdf[i] = cdf[i - 1] + histogram[i];
        }

        // Find minimum non-zero CDF value
        int cdfMin = 0;
        for (int i = 0; i < 256; i++)
        {
            if (cdf[i] > 0)
            {
                cdfMin = cdf[i];
                break;
            }
        }

        int totalPixels = width * height;

        // Apply histogram equalization
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int value = image[y, x];
                int newValue = (int)((cdf[value] - cdfMin) * 255.0f / (totalPixels - cdfMin));
                result[y, x] = (byte)Math.Clamp(newValue, 0, 255);
            }
        }

        return result;
    }

    /// <summary>
    /// Applies CLAHE (Contrast Limited Adaptive Histogram Equalization).
    /// </summary>
    public static byte[,] ApplyCLAHE(byte[,] image, int tileSize = 8, float clipLimit = 2.0f)
    {
        int height = image.GetLength(0);
        int width = image.GetLength(1);
        var result = new byte[height, width];

        int tilesY = (height + tileSize - 1) / tileSize;
        int tilesX = (width + tileSize - 1) / tileSize;

        // Process each tile
        var tileLUTs = new byte[tilesY, tilesX, 256];

        for (int ty = 0; ty < tilesY; ty++)
        {
            for (int tx = 0; tx < tilesX; tx++)
            {
                int startY = ty * tileSize;
                int startX = tx * tileSize;
                int endY = Math.Min(startY + tileSize, height);
                int endX = Math.Min(startX + tileSize, width);

                // Calculate histogram for this tile
                int[] histogram = new int[256];
                int pixelCount = 0;

                for (int y = startY; y < endY; y++)
                {
                    for (int x = startX; x < endX; x++)
                    {
                        histogram[image[y, x]]++;
                        pixelCount++;
                    }
                }

                // Clip histogram
                int clipThreshold = (int)(clipLimit * pixelCount / 256);
                int excess = 0;

                for (int i = 0; i < 256; i++)
                {
                    if (histogram[i] > clipThreshold)
                    {
                        excess += histogram[i] - clipThreshold;
                        histogram[i] = clipThreshold;
                    }
                }

                // Redistribute excess
                int avgIncrease = excess / 256;
                for (int i = 0; i < 256; i++)
                {
                    histogram[i] += avgIncrease;
                }

                // Build CDF and LUT
                int[] cdf = new int[256];
                cdf[0] = histogram[0];
                for (int i = 1; i < 256; i++)
                {
                    cdf[i] = cdf[i - 1] + histogram[i];
                }

                for (int i = 0; i < 256; i++)
                {
                    tileLUTs[ty, tx, i] = (byte)Math.Clamp(cdf[i] * 255 / pixelCount, 0, 255);
                }
            }
        }

        // Apply with bilinear interpolation between tiles
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float tileY = (float)y / tileSize - 0.5f;
                float tileX = (float)x / tileSize - 0.5f;

                int ty1 = Math.Clamp((int)tileY, 0, tilesY - 1);
                int tx1 = Math.Clamp((int)tileX, 0, tilesX - 1);
                int ty2 = Math.Min(ty1 + 1, tilesY - 1);
                int tx2 = Math.Min(tx1 + 1, tilesX - 1);

                float fy = tileY - ty1;
                float fx = tileX - tx1;
                fy = Math.Clamp(fy, 0, 1);
                fx = Math.Clamp(fx, 0, 1);

                byte value = image[y, x];

                // Bilinear interpolation
                float v00 = tileLUTs[ty1, tx1, value];
                float v10 = tileLUTs[ty1, tx2, value];
                float v01 = tileLUTs[ty2, tx1, value];
                float v11 = tileLUTs[ty2, tx2, value];

                float top = v00 * (1 - fx) + v10 * fx;
                float bottom = v01 * (1 - fx) + v11 * fx;
                float finalValue = top * (1 - fy) + bottom * fy;

                result[y, x] = (byte)Math.Clamp(finalValue, 0, 255);
            }
        }

        return result;
    }

    /// <summary>
    /// Resizes an image using bilinear interpolation.
    /// </summary>
    public static SKBitmap Resize(SKBitmap source, int newWidth, int newHeight)
    {
        var resized = new SKBitmap(newWidth, newHeight, source.ColorType, source.AlphaType);

        using var canvas = new SKCanvas(resized);
        using var paint = new SKPaint
        {
            FilterQuality = SKFilterQuality.High,
            IsAntialias = true
        };

        canvas.DrawBitmap(source, new SKRect(0, 0, newWidth, newHeight), paint);

        return resized;
    }

    /// <summary>
    /// Resizes a grayscale image.
    /// </summary>
    public static byte[,] ResizeGrayscale(byte[,] image, int newWidth, int newHeight)
    {
        int srcHeight = image.GetLength(0);
        int srcWidth = image.GetLength(1);
        var result = new byte[newHeight, newWidth];

        float xRatio = (float)srcWidth / newWidth;
        float yRatio = (float)srcHeight / newHeight;

        for (int y = 0; y < newHeight; y++)
        {
            for (int x = 0; x < newWidth; x++)
            {
                float srcX = x * xRatio;
                float srcY = y * yRatio;

                int x1 = (int)srcX;
                int y1 = (int)srcY;
                int x2 = Math.Min(x1 + 1, srcWidth - 1);
                int y2 = Math.Min(y1 + 1, srcHeight - 1);

                float xFrac = srcX - x1;
                float yFrac = srcY - y1;

                // Bilinear interpolation
                float top = image[y1, x1] * (1 - xFrac) + image[y1, x2] * xFrac;
                float bottom = image[y2, x1] * (1 - xFrac) + image[y2, x2] * xFrac;
                float value = top * (1 - yFrac) + bottom * yFrac;

                result[y, x] = (byte)Math.Clamp(value, 0, 255);
            }
        }

        return result;
    }
}
