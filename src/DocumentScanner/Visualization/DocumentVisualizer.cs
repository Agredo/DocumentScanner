using DocumentScanner.Core;
using SkiaSharp;

namespace DocumentScanner.Visualization;

/// <summary>
/// Provides visualization options for document detection results.
/// </summary>
public class VisualizationOptions
{
    /// <summary>
    /// Color for the document overlay fill. Default: Green with 60/255 alpha.
    /// </summary>
    public SKColor FillColor { get; set; } = new SKColor(0, 255, 0, 60);

    /// <summary>
    /// Color for the document border. Default: LimeGreen.
    /// </summary>
    public SKColor BorderColor { get; set; } = SKColors.LimeGreen;

    /// <summary>
    /// Width of the document border in pixels. Default: 8.
    /// </summary>
    public float BorderWidth { get; set; } = 8f;

    /// <summary>
    /// Color for corner markers. Default: Red.
    /// </summary>
    public SKColor CornerColor { get; set; } = SKColors.Red;

    /// <summary>
    /// Color for corner marker borders. Default: White.
    /// </summary>
    public SKColor CornerBorderColor { get; set; } = SKColors.White;

    /// <summary>
    /// Radius of corner markers in pixels. Default: 20.
    /// </summary>
    public float CornerRadius { get; set; } = 20f;

    /// <summary>
    /// Width of corner marker borders in pixels. Default: 4.
    /// </summary>
    public float CornerBorderWidth { get; set; } = 4f;

    /// <summary>
    /// Whether to show corner labels (TL, TR, BR, BL). Default: true.
    /// </summary>
    public bool ShowCornerLabels { get; set; } = true;

    /// <summary>
    /// Font size for corner labels. Default: 40.
    /// </summary>
    public float LabelFontSize { get; set; } = 40f;

    /// <summary>
    /// Color for corner labels. Default: White.
    /// </summary>
    public SKColor LabelColor { get; set; } = SKColors.White;

    /// <summary>
    /// Whether to show confidence score. Default: true.
    /// </summary>
    public bool ShowConfidence { get; set; } = true;

    /// <summary>
    /// Font size for confidence score. Default: 50.
    /// </summary>
    public float ConfidenceFontSize { get; set; } = 50f;

    /// <summary>
    /// Color for confidence text. Default: White.
    /// </summary>
    public SKColor ConfidenceTextColor { get; set; } = SKColors.White;

    /// <summary>
    /// Background color for confidence label. Default: Black with 180/255 alpha.
    /// </summary>
    public SKColor ConfidenceBackgroundColor { get; set; } = new SKColor(0, 0, 0, 180);

    /// <summary>
    /// Output image format. Default: JPEG.
    /// </summary>
    public SKEncodedImageFormat OutputFormat { get; set; } = SKEncodedImageFormat.Jpeg;

    /// <summary>
    /// Output image quality (0-100). Default: 95.
    /// </summary>
    public int OutputQuality { get; set; } = 95;
}

/// <summary>
/// Provides methods to visualize document detection results.
/// </summary>
public static class DocumentVisualizer
{
    /// <summary>
    /// Creates a visualization of the detected document on the original image.
    /// </summary>
    /// <param name="imageBytes">Original image as byte array</param>
    /// <param name="result">Detection result to visualize</param>
    /// <param name="options">Optional visualization options</param>
    /// <returns>Visualized image as byte array</returns>
    public static byte[] CreateVisualization(byte[] imageBytes, DetectionResult result, VisualizationOptions? options = null)
    {
        using var bitmap = ImageProcessing.ImageProcessor.LoadImage(imageBytes);
        return CreateVisualization(bitmap, result, options);
    }

    /// <summary>
    /// Creates a visualization of the detected document on the original image.
    /// </summary>
    /// <param name="bitmap">Original image as SKBitmap</param>
    /// <param name="result">Detection result to visualize</param>
    /// <param name="options">Optional visualization options</param>
    /// <returns>Visualized image as byte array</returns>
    public static byte[] CreateVisualization(SKBitmap bitmap, DetectionResult result, VisualizationOptions? options = null)
    {
        options ??= new VisualizationOptions();

        // Create a copy of the original image
        var visualized = new SKBitmap(bitmap.Width, bitmap.Height, bitmap.ColorType, bitmap.AlphaType);
        using var canvas = new SKCanvas(visualized);

        // Draw original image
        canvas.DrawBitmap(bitmap, 0, 0);

        // Only draw visualization if detection was successful
        if (result.Success && result.Corners != null)
        {
            DrawDocumentOverlay(canvas, result.Corners, options);
            DrawCorners(canvas, result.Corners, options);

            if (options.ShowConfidence)
            {
                DrawConfidenceScore(canvas, result.Confidence, options);
            }
        }

        // Encode to byte array
        using var image = SKImage.FromBitmap(visualized);
        using var data = image.Encode(options.OutputFormat, options.OutputQuality);
        return data.ToArray();
    }

    /// <summary>
    /// Creates a visualization and returns it as an SKBitmap.
    /// </summary>
    /// <param name="bitmap">Original image as SKBitmap</param>
    /// <param name="result">Detection result to visualize</param>
    /// <param name="options">Optional visualization options</param>
    /// <returns>Visualized image as SKBitmap</returns>
    public static SKBitmap CreateVisualizationBitmap(SKBitmap bitmap, DetectionResult result, VisualizationOptions? options = null)
    {
        options ??= new VisualizationOptions();

        // Create a copy of the original image
        var visualized = new SKBitmap(bitmap.Width, bitmap.Height, bitmap.ColorType, bitmap.AlphaType);
        using var canvas = new SKCanvas(visualized);

        // Draw original image
        canvas.DrawBitmap(bitmap, 0, 0);

        // Only draw visualization if detection was successful
        if (result.Success && result.Corners != null)
        {
            DrawDocumentOverlay(canvas, result.Corners, options);
            DrawCorners(canvas, result.Corners, options);

            if (options.ShowConfidence)
            {
                DrawConfidenceScore(canvas, result.Confidence, options);
            }
        }

        return visualized;
    }

    /// <summary>
    /// Draws the semi-transparent overlay and border on the detected document.
    /// </summary>
    private static void DrawDocumentOverlay(SKCanvas canvas, Quadrilateral quad, VisualizationOptions options)
    {
        var corners = quad.ToArray();

        // Create path for the quadrilateral
        var path = new SKPath();
        path.MoveTo(corners[0].ToSKPoint());
        for (int i = 1; i < corners.Length; i++)
        {
            path.LineTo(corners[i].ToSKPoint());
        }
        path.Close();

        // Draw semi-transparent fill
        using var fillPaint = new SKPaint
        {
            Color = options.FillColor,
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };
        canvas.DrawPath(path, fillPaint);

        // Draw border
        using var borderPaint = new SKPaint
        {
            Color = options.BorderColor,
            StrokeWidth = options.BorderWidth,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true
        };
        canvas.DrawPath(path, borderPaint);
    }

    /// <summary>
    /// Draws corner markers and labels.
    /// </summary>
    private static void DrawCorners(SKCanvas canvas, Quadrilateral quad, VisualizationOptions options)
    {
        var corners = quad.ToArray();
        string[] labels = { "TL", "TR", "BR", "BL" };

        using var cornerPaint = new SKPaint
        {
            Color = options.CornerColor,
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };

        using var cornerBorderPaint = new SKPaint
        {
            Color = options.CornerBorderColor,
            StrokeWidth = options.CornerBorderWidth,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true
        };

        for (int i = 0; i < corners.Length; i++)
        {
            var point = corners[i].ToSKPoint();

            // Draw white border around corner
            canvas.DrawCircle(point, options.CornerRadius, cornerBorderPaint);

            // Draw corner circle
            canvas.DrawCircle(point, options.CornerRadius, cornerPaint);

            // Draw label if enabled
            if (options.ShowCornerLabels)
            {
                using var textPaint = new SKPaint
                {
                    Color = options.LabelColor,
                    TextSize = options.LabelFontSize,
                    IsAntialias = true,
                    Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold),
                    TextAlign = SKTextAlign.Center
                };

                // Position label above the corner
                canvas.DrawText(labels[i], point.X, point.Y - options.CornerRadius - 20, textPaint);
            }
        }
    }

    /// <summary>
    /// Draws the confidence score in the top-left corner.
    /// </summary>
    private static void DrawConfidenceScore(SKCanvas canvas, float confidence, VisualizationOptions options)
    {
        using var confidencePaint = new SKPaint
        {
            Color = options.ConfidenceTextColor,
            TextSize = options.ConfidenceFontSize,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold)
        };

        using var confidenceBackgroundPaint = new SKPaint
        {
            Color = options.ConfidenceBackgroundColor,
            Style = SKPaintStyle.Fill
        };

        string confidenceText = $"Confidence: {confidence:P0}";
        var textBounds = new SKRect();
        confidencePaint.MeasureText(confidenceText, ref textBounds);

        // Draw background rectangle
        var bgRect = new SKRect(20, 20, 40 + textBounds.Width, 80);
        canvas.DrawRoundRect(bgRect, 10, 10, confidenceBackgroundPaint);

        // Draw text
        canvas.DrawText(confidenceText, 30, 70, confidencePaint);
    }
}
