using DocumentScanner.Core;
using DocumentScanner.ImageProcessing;
using SkiaSharp;

namespace DocumentScanner;

/// <summary>
/// Interface for document processing operations.
/// </summary>
public interface IDocumentProcessor
{
    /// <summary>
    /// Corrects the perspective of a document based on detected corners.
    /// </summary>
    byte[] CorrectPerspective(byte[] imageBytes, PointF[] corners, ProcessingOptions? options = null);

    /// <summary>
    /// Corrects the perspective of a document based on a quadrilateral.
    /// </summary>
    byte[] CorrectPerspective(byte[] imageBytes, Quadrilateral corners, ProcessingOptions? options = null);

    /// <summary>
    /// Processes a detection result and returns the corrected document image.
    /// </summary>
    byte[] ProcessDetectionResult(byte[] imageBytes, DetectionResult result, ProcessingOptions? options = null);
}

/// <summary>
/// Options for document processing and perspective correction.
/// </summary>
public class ProcessingOptions
{
    /// <summary>
    /// Target aspect ratio for the output (null = auto-detect from corners).
    /// Common values: 0.707 for A4 portrait, 1.414 for A4 landscape.
    /// </summary>
    public float? TargetAspectRatio { get; set; } = null;

    /// <summary>
    /// Maximum width of the output image (null = no limit).
    /// </summary>
    public int? MaxWidth { get; set; } = null;

    /// <summary>
    /// Maximum height of the output image (null = no limit).
    /// </summary>
    public int? MaxHeight { get; set; } = null;

    /// <summary>
    /// Output image format. Default: PNG.
    /// </summary>
    public SKEncodedImageFormat OutputFormat { get; set; } = SKEncodedImageFormat.Png;

    /// <summary>
    /// Quality for JPEG output (1-100). Default: 90.
    /// </summary>
    public int JpegQuality { get; set; } = 90;

    /// <summary>
    /// Whether to apply automatic white balance. Default: false.
    /// </summary>
    public bool ApplyWhiteBalance { get; set; } = false;

    /// <summary>
    /// Whether to enhance contrast in the output. Default: false.
    /// </summary>
    public bool EnhanceContrast { get; set; } = false;

    /// <summary>
    /// Whether to sharpen the output image. Default: false.
    /// </summary>
    public bool Sharpen { get; set; } = false;

    /// <summary>
    /// Whether to convert output to grayscale. Default: false.
    /// </summary>
    public bool ConvertToGrayscale { get; set; } = false;

    /// <summary>
    /// Whether to apply adaptive thresholding for clean black & white output. Default: false.
    /// </summary>
    public bool ApplyBinarization { get; set; } = false;

    /// <summary>
    /// Padding to add around the document (in pixels). Default: 0.
    /// </summary>
    public int Padding { get; set; } = 0;
}

/// <summary>
/// Processes detected documents by applying perspective correction
/// and optional image enhancements.
/// </summary>
public class DocumentProcessor : IDocumentProcessor
{
    private readonly ProcessingOptions _defaultOptions;

    public DocumentProcessor() : this(new ProcessingOptions()) { }

    public DocumentProcessor(ProcessingOptions defaultOptions)
    {
        _defaultOptions = defaultOptions;
    }

    /// <inheritdoc />
    public byte[] CorrectPerspective(byte[] imageBytes, PointF[] corners, ProcessingOptions? options = null)
    {
        if (corners.Length != 4)
            throw new ArgumentException("Exactly 4 corner points are required", nameof(corners));

        var quad = Quadrilateral.FromPoints(corners);
        return CorrectPerspective(imageBytes, quad, options);
    }

    /// <inheritdoc />
    public byte[] CorrectPerspective(byte[] imageBytes, Quadrilateral corners, ProcessingOptions? options = null)
    {
        options ??= _defaultOptions;

        using var sourceBitmap = ImageProcessor.LoadImage(imageBytes);
        using var correctedBitmap = CorrectPerspectiveInternal(sourceBitmap, corners, options);
        using var processedBitmap = ApplyEnhancements(correctedBitmap, options);

        return ImageProcessor.SaveImage(
            processedBitmap,
            options.OutputFormat,
            options.OutputFormat == SKEncodedImageFormat.Jpeg ? options.JpegQuality : 100
        );
    }

    /// <inheritdoc />
    public byte[] ProcessDetectionResult(byte[] imageBytes, DetectionResult result, ProcessingOptions? options = null)
    {
        if (!result.Success || result.Corners == null)
            throw new ArgumentException("Detection result is not successful or has no corners");

        return CorrectPerspective(imageBytes, result.Corners, options);
    }

    /// <summary>
    /// Internal perspective correction using the transformation matrix.
    /// </summary>
    private SKBitmap CorrectPerspectiveInternal(SKBitmap source, Quadrilateral corners, ProcessingOptions options)
    {
        // Calculate the perspective transformation
        var (transform, width, height) = PerspectiveTransform.CreateDocumentTransform(
            corners,
            options.TargetAspectRatio
        );

        // Apply size limits
        int outputWidth = width;
        int outputHeight = height;

        if (options.MaxWidth.HasValue || options.MaxHeight.HasValue)
        {
            float scaleX = options.MaxWidth.HasValue ? (float)options.MaxWidth.Value / width : 1;
            float scaleY = options.MaxHeight.HasValue ? (float)options.MaxHeight.Value / height : 1;
            float scale = Math.Min(scaleX, scaleY);

            if (scale < 1)
            {
                outputWidth = (int)(width * scale);
                outputHeight = (int)(height * scale);
            }
        }

        // Add padding
        int paddedWidth = outputWidth + 2 * options.Padding;
        int paddedHeight = outputHeight + 2 * options.Padding;

        // Recalculate transform for final size with padding
        var dstCorners = new PointF[]
        {
            new(options.Padding, options.Padding),
            new(options.Padding + outputWidth, options.Padding),
            new(options.Padding + outputWidth, options.Padding + outputHeight),
            new(options.Padding, options.Padding + outputHeight)
        };

        var srcCorners = corners.ToArray();
        var finalTransform = PerspectiveTransform.GetPerspectiveTransform(srcCorners, dstCorners);

        // Apply the perspective warp
        var warped = PerspectiveTransform.WarpPerspective(source, finalTransform, paddedWidth, paddedHeight);

        return warped;
    }

    /// <summary>
    /// Applies optional image enhancements to the corrected document.
    /// </summary>
    private SKBitmap ApplyEnhancements(SKBitmap bitmap, ProcessingOptions options)
    {
        SKBitmap current = bitmap;
        bool needsDispose = false;

        try
        {
            // Convert to grayscale if requested
            if (options.ConvertToGrayscale || options.ApplyBinarization)
            {
                var grayscale = ImageProcessor.ToGrayscale(current);

                if (options.ApplyBinarization)
                {
                    grayscale = Thresholder.SauvolaThreshold(grayscale, 15, 0.3f);
                }

                var newBitmap = ImageProcessor.FromGrayscale(grayscale);

                if (needsDispose) current.Dispose();
                current = newBitmap;
                needsDispose = true;
            }

            // Apply white balance
            if (options.ApplyWhiteBalance && !options.ApplyBinarization)
            {
                var balanced = ApplyWhiteBalance(current);
                if (needsDispose) current.Dispose();
                current = balanced;
                needsDispose = true;
            }

            // Enhance contrast
            if (options.EnhanceContrast && !options.ApplyBinarization)
            {
                var enhanced = ApplyContrastEnhancement(current);
                if (needsDispose) current.Dispose();
                current = enhanced;
                needsDispose = true;
            }

            // Sharpen
            if (options.Sharpen)
            {
                var sharpened = ApplySharpen(current);
                if (needsDispose) current.Dispose();
                current = sharpened;
                needsDispose = true;
            }

            // If we created any new bitmaps, we need to return the final one
            // without disposing it
            needsDispose = false;
            return current;
        }
        catch
        {
            if (needsDispose && current != bitmap)
                current.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Applies automatic white balance correction.
    /// </summary>
    private SKBitmap ApplyWhiteBalance(SKBitmap bitmap)
    {
        var result = new SKBitmap(bitmap.Width, bitmap.Height, bitmap.ColorType, bitmap.AlphaType);
        var srcPixels = bitmap.GetPixels();
        var dstPixels = result.GetPixels();
        int bytesPerPixel = bitmap.BytesPerPixel;
        int pixelCount = bitmap.Width * bitmap.Height;

        // Calculate average color
        long sumR = 0, sumG = 0, sumB = 0;

        unsafe
        {
            byte* src = (byte*)srcPixels.ToPointer();

            for (int i = 0; i < pixelCount; i++)
            {
                int offset = i * bytesPerPixel;
                sumR += src[offset];
                sumG += src[offset + 1];
                sumB += src[offset + 2];
            }
        }

        float avgR = (float)sumR / pixelCount;
        float avgG = (float)sumG / pixelCount;
        float avgB = (float)sumB / pixelCount;
        float avgGray = (avgR + avgG + avgB) / 3;

        // Calculate correction factors
        float factorR = avgGray / Math.Max(avgR, 1);
        float factorG = avgGray / Math.Max(avgG, 1);
        float factorB = avgGray / Math.Max(avgB, 1);

        // Clamp factors to avoid extreme corrections
        factorR = Math.Clamp(factorR, 0.5f, 2.0f);
        factorG = Math.Clamp(factorG, 0.5f, 2.0f);
        factorB = Math.Clamp(factorB, 0.5f, 2.0f);

        unsafe
        {
            byte* src = (byte*)srcPixels.ToPointer();
            byte* dst = (byte*)dstPixels.ToPointer();

            for (int i = 0; i < pixelCount; i++)
            {
                int offset = i * bytesPerPixel;

                dst[offset] = (byte)Math.Clamp(src[offset] * factorR, 0, 255);
                dst[offset + 1] = (byte)Math.Clamp(src[offset + 1] * factorG, 0, 255);
                dst[offset + 2] = (byte)Math.Clamp(src[offset + 2] * factorB, 0, 255);

                if (bytesPerPixel == 4)
                    dst[offset + 3] = src[offset + 3];
            }
        }

        return result;
    }

    /// <summary>
    /// Applies contrast enhancement using histogram stretching.
    /// </summary>
    private SKBitmap ApplyContrastEnhancement(SKBitmap bitmap)
    {
        var result = new SKBitmap(bitmap.Width, bitmap.Height, bitmap.ColorType, bitmap.AlphaType);
        var srcPixels = bitmap.GetPixels();
        var dstPixels = result.GetPixels();
        int bytesPerPixel = bitmap.BytesPerPixel;
        int pixelCount = bitmap.Width * bitmap.Height;

        // Find min and max values for each channel
        byte minR = 255, maxR = 0;
        byte minG = 255, maxG = 0;
        byte minB = 255, maxB = 0;

        unsafe
        {
            byte* src = (byte*)srcPixels.ToPointer();

            for (int i = 0; i < pixelCount; i++)
            {
                int offset = i * bytesPerPixel;

                minR = Math.Min(minR, src[offset]);
                maxR = Math.Max(maxR, src[offset]);
                minG = Math.Min(minG, src[offset + 1]);
                maxG = Math.Max(maxG, src[offset + 1]);
                minB = Math.Min(minB, src[offset + 2]);
                maxB = Math.Max(maxB, src[offset + 2]);
            }
        }

        // Create lookup tables
        byte[] lutR = CreateStretchLUT(minR, maxR);
        byte[] lutG = CreateStretchLUT(minG, maxG);
        byte[] lutB = CreateStretchLUT(minB, maxB);

        unsafe
        {
            byte* src = (byte*)srcPixels.ToPointer();
            byte* dst = (byte*)dstPixels.ToPointer();

            for (int i = 0; i < pixelCount; i++)
            {
                int offset = i * bytesPerPixel;

                dst[offset] = lutR[src[offset]];
                dst[offset + 1] = lutG[src[offset + 1]];
                dst[offset + 2] = lutB[src[offset + 2]];

                if (bytesPerPixel == 4)
                    dst[offset + 3] = src[offset + 3];
            }
        }

        return result;
    }

    /// <summary>
    /// Creates a lookup table for histogram stretching.
    /// </summary>
    private byte[] CreateStretchLUT(byte min, byte max)
    {
        var lut = new byte[256];
        float range = max - min;

        if (range < 1) range = 1;

        for (int i = 0; i < 256; i++)
        {
            lut[i] = (byte)Math.Clamp((i - min) * 255 / range, 0, 255);
        }

        return lut;
    }

    /// <summary>
    /// Applies unsharp masking for sharpening.
    /// </summary>
    private SKBitmap ApplySharpen(SKBitmap bitmap)
    {
        // Sharpen using unsharp mask: output = original + (original - blurred) * amount
        var grayscale = ImageProcessor.ToGrayscale(bitmap);
        var blurred = ImageProcessor.GaussianBlur(grayscale, 3);

        var result = new SKBitmap(bitmap.Width, bitmap.Height, bitmap.ColorType, bitmap.AlphaType);
        var srcPixels = bitmap.GetPixels();
        var dstPixels = result.GetPixels();
        int bytesPerPixel = bitmap.BytesPerPixel;
        int width = bitmap.Width;
        int height = bitmap.Height;

        float amount = 0.5f; // Sharpening strength

        unsafe
        {
            byte* src = (byte*)srcPixels.ToPointer();
            byte* dst = (byte*)dstPixels.ToPointer();

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int offset = (y * width + x) * bytesPerPixel;
                    float diff = grayscale[y, x] - blurred[y, x];

                    for (int c = 0; c < 3; c++)
                    {
                        float sharpened = src[offset + c] + diff * amount;
                        dst[offset + c] = (byte)Math.Clamp(sharpened, 0, 255);
                    }

                    if (bytesPerPixel == 4)
                        dst[offset + 3] = src[offset + 3];
                }
            }
        }

        return result;
    }
}
