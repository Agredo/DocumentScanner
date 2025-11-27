using DocumentScanner.Core;
using DocumentScanner.ImageProcessing;
using SkiaSharp;

namespace DocumentScanner;

/// <summary>
/// Result of a complete scan operation (detection + correction).
/// </summary>
public class ScanResult : IDisposable
{
    private bool _disposed;

    /// <summary>
    /// Whether the scan was successful.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// The corrected document image as a byte array.
    /// Only available if Success is true.
    /// </summary>
    public byte[]? CorrectedImage { get; set; }

    /// <summary>
    /// The detection result containing corners, mask, and angle info.
    /// </summary>
    public DetectionResult? DetectionResult { get; set; }

    /// <summary>
    /// Error message if the scan failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Creates a successful scan result.
    /// </summary>
    public static ScanResult Successful(byte[] correctedImage, DetectionResult detectionResult)
    {
        return new ScanResult
        {
            Success = true,
            CorrectedImage = correctedImage,
            DetectionResult = detectionResult
        };
    }

    /// <summary>
    /// Creates a failed scan result.
    /// </summary>
    public static ScanResult Failed(string errorMessage, DetectionResult? detectionResult = null)
    {
        return new ScanResult
        {
            Success = false,
            ErrorMessage = errorMessage,
            DetectionResult = detectionResult
        };
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            DetectionResult?.Dispose();
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}

/// <summary>
/// Main facade class for document scanning operations.
/// Provides a simplified API for detecting and processing documents.
/// </summary>
public class Scanner : IDisposable
{
    private readonly IDocumentDetector _detector;
    private readonly IDocumentProcessor _processor;
    private bool _disposed;

    /// <summary>
    /// Creates a new Scanner with default options.
    /// </summary>
    public Scanner() : this(new DetectionOptions(), new ProcessingOptions()) { }

    /// <summary>
    /// Creates a new Scanner with custom detection options.
    /// </summary>
    public Scanner(DetectionOptions detectionOptions)
        : this(detectionOptions, new ProcessingOptions()) { }

    /// <summary>
    /// Creates a new Scanner with custom options.
    /// </summary>
    public Scanner(DetectionOptions detectionOptions, ProcessingOptions processingOptions)
    {
        _detector = new DocumentDetector(detectionOptions);
        _processor = new DocumentProcessor(processingOptions);
    }

    /// <summary>
    /// Creates a new Scanner with custom detector and processor implementations.
    /// </summary>
    public Scanner(IDocumentDetector detector, IDocumentProcessor processor)
    {
        _detector = detector;
        _processor = processor;
    }

    /// <summary>
    /// Scans an image, detecting and correcting the document in one operation.
    /// </summary>
    /// <param name="imageBytes">The source image as a byte array.</param>
    /// <param name="detectionOptions">Optional custom detection options.</param>
    /// <param name="processingOptions">Optional custom processing options.</param>
    /// <returns>A ScanResult containing the corrected document and detection info.</returns>
    public ScanResult Scan(
        byte[] imageBytes,
        DetectionOptions? detectionOptions = null,
        ProcessingOptions? processingOptions = null)
    {
        try
        {
            // Step 1: Detect the document
            var detectionResult = _detector.Detect(imageBytes, detectionOptions);

            if (!detectionResult.Success || detectionResult.Corners == null)
            {
                return ScanResult.Failed(
                    detectionResult.ErrorMessage ?? "Document detection failed",
                    detectionResult
                );
            }

            // Step 2: Process the document (perspective correction + enhancements)
            var correctedImage = _processor.CorrectPerspective(
                imageBytes,
                detectionResult.Corners,
                processingOptions
            );

            return ScanResult.Successful(correctedImage, detectionResult);
        }
        catch (Exception ex)
        {
            return ScanResult.Failed($"Scan failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Asynchronously scans an image.
    /// </summary>
    public Task<ScanResult> ScanAsync(
        byte[] imageBytes,
        DetectionOptions? detectionOptions = null,
        ProcessingOptions? processingOptions = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Scan(imageBytes, detectionOptions, processingOptions);
        }, cancellationToken);
    }

    /// <summary>
    /// Detects a document without applying perspective correction.
    /// Useful when you want to preview the detection before processing.
    /// </summary>
    public DetectionResult Detect(byte[] imageBytes, DetectionOptions? options = null)
    {
        return _detector.Detect(imageBytes, options);
    }

    /// <summary>
    /// Asynchronously detects a document.
    /// </summary>
    public Task<DetectionResult> DetectAsync(
        byte[] imageBytes,
        DetectionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Detect(imageBytes, options);
        }, cancellationToken);
    }

    /// <summary>
    /// Processes a detection result to get the corrected document.
    /// </summary>
    public byte[] Process(
        byte[] imageBytes,
        DetectionResult result,
        ProcessingOptions? options = null)
    {
        return _processor.ProcessDetectionResult(imageBytes, result, options);
    }

    /// <summary>
    /// Corrects perspective using manually specified corner points.
    /// </summary>
    public byte[] CorrectPerspective(
        byte[] imageBytes,
        PointF[] corners,
        ProcessingOptions? options = null)
    {
        return _processor.CorrectPerspective(imageBytes, corners, options);
    }

    /// <summary>
    /// Corrects perspective using a quadrilateral.
    /// </summary>
    public byte[] CorrectPerspective(
        byte[] imageBytes,
        Quadrilateral corners,
        ProcessingOptions? options = null)
    {
        return _processor.CorrectPerspective(imageBytes, corners, options);
    }

    /// <summary>
    /// Creates a visualization of the detected document on the original image.
    /// Useful for previewing detection results.
    /// </summary>
    public byte[] CreateDetectionVisualization(
        byte[] imageBytes,
        DetectionResult result,
        SKColor? lineColor = null,
        SKColor? cornerColor = null,
        float lineWidth = 3)
    {
        if (!result.Success || result.Corners == null)
            return imageBytes;

        using var bitmap = ImageProcessor.LoadImage(imageBytes);
        using var canvas = new SKCanvas(bitmap);

        var corners = result.Corners.ToArray();

        // Draw the quadrilateral
        using var linePaint = new SKPaint
        {
            Color = lineColor ?? SKColors.LimeGreen,
            StrokeWidth = lineWidth,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true
        };

        var path = new SKPath();
        path.MoveTo(corners[0].ToSKPoint());
        for (int i = 1; i < corners.Length; i++)
        {
            path.LineTo(corners[i].ToSKPoint());
        }
        path.Close();
        canvas.DrawPath(path, linePaint);

        // Draw corner points
        using var cornerPaint = new SKPaint
        {
            Color = cornerColor ?? SKColors.Red,
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };

        float cornerRadius = lineWidth * 2;
        foreach (var corner in corners)
        {
            canvas.DrawCircle(corner.ToSKPoint(), cornerRadius, cornerPaint);
        }

        // Add corner labels
        using var textPaint = new SKPaint
        {
            Color = SKColors.White,
            TextSize = 16,
            IsAntialias = true
        };

        string[] labels = { "TL", "TR", "BR", "BL" };
        for (int i = 0; i < corners.Length; i++)
        {
            canvas.DrawText(
                labels[i],
                corners[i].X + cornerRadius + 5,
                corners[i].Y + 5,
                textPaint
            );
        }

        return ImageProcessor.SaveImage(bitmap, SKEncodedImageFormat.Png);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
