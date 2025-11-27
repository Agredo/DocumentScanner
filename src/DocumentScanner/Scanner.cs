using DocumentScanner.Core;
using DocumentScanner.ImageProcessing;
using DocumentScanner.Visualization;
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
    /// </summary>
    /// <param name="imageBytes">Original image as byte array</param>
    /// <param name="result">Detection result to visualize</param>
    /// <param name="options">Optional visualization options</param>
    /// <returns>Visualized image as byte array</returns>
    public byte[] CreateVisualization(
        byte[] imageBytes,
        DetectionResult result,
        VisualizationOptions? options = null)
    {
        return DocumentVisualizer.CreateVisualization(imageBytes, result, options);
    }

    /// <summary>
    /// Creates a visualization of the detected document and returns it as SKBitmap.
    /// </summary>
    /// <param name="bitmap">Original image as SKBitmap</param>
    /// <param name="result">Detection result to visualize</param>
    /// <param name="options">Optional visualization options</param>
    /// <returns>Visualized image as SKBitmap</returns>
    public SKBitmap CreateVisualizationBitmap(
        SKBitmap bitmap,
        DetectionResult result,
        VisualizationOptions? options = null)
    {
        return DocumentVisualizer.CreateVisualizationBitmap(bitmap, result, options);
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
