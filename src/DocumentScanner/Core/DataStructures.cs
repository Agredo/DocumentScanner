using SkiaSharp;

namespace DocumentScanner.Core;

/// <summary>
/// Represents a 2D point with floating-point coordinates.
/// </summary>
public readonly struct PointF : IEquatable<PointF>
{
    public float X { get; }
    public float Y { get; }

    public PointF(float x, float y)
    {
        X = x;
        Y = y;
    }

    public static PointF Empty => new(0, 0);

    public float DistanceTo(PointF other)
    {
        float dx = X - other.X;
        float dy = Y - other.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    public static PointF operator +(PointF a, PointF b) => new(a.X + b.X, a.Y + b.Y);
    public static PointF operator -(PointF a, PointF b) => new(a.X - b.X, a.Y - b.Y);
    public static PointF operator *(PointF p, float scalar) => new(p.X * scalar, p.Y * scalar);
    public static PointF operator /(PointF p, float scalar) => new(p.X / scalar, p.Y / scalar);

    public SKPoint ToSKPoint() => new(X, Y);
    public static PointF FromSKPoint(SKPoint p) => new(p.X, p.Y);

    public bool Equals(PointF other) => Math.Abs(X - other.X) < 0.0001f && Math.Abs(Y - other.Y) < 0.0001f;
    public override bool Equals(object? obj) => obj is PointF other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(X, Y);
    public override string ToString() => $"({X:F2}, {Y:F2})";
}

/// <summary>
/// Represents a quadrilateral (4-sided polygon) with ordered corner points.
/// Points are ordered: TopLeft, TopRight, BottomRight, BottomLeft (clockwise).
/// </summary>
public class Quadrilateral
{
    public PointF TopLeft { get; set; }
    public PointF TopRight { get; set; }
    public PointF BottomRight { get; set; }
    public PointF BottomLeft { get; set; }

    public Quadrilateral()
    {
        TopLeft = TopRight = BottomRight = BottomLeft = PointF.Empty;
    }

    public Quadrilateral(PointF topLeft, PointF topRight, PointF bottomRight, PointF bottomLeft)
    {
        TopLeft = topLeft;
        TopRight = topRight;
        BottomRight = bottomRight;
        BottomLeft = bottomLeft;
    }

    /// <summary>
    /// Creates a quadrilateral from an array of 4 points.
    /// The points are automatically ordered clockwise starting from top-left.
    /// </summary>
    public static Quadrilateral FromPoints(PointF[] points)
    {
        if (points.Length != 4)
            throw new ArgumentException("Exactly 4 points are required", nameof(points));

        return OrderPoints(points);
    }

    /// <summary>
    /// Orders 4 points into a clockwise quadrilateral starting from top-left.
    /// </summary>
    private static Quadrilateral OrderPoints(PointF[] points)
    {
        // Sort by sum (x + y) - smallest is top-left, largest is bottom-right
        var sortedBySum = points.OrderBy(p => p.X + p.Y).ToArray();
        var topLeft = sortedBySum[0];
        var bottomRight = sortedBySum[3];

        // Sort by difference (x - y) - smallest is bottom-left, largest is top-right
        var sortedByDiff = points.OrderBy(p => p.X - p.Y).ToArray();
        var bottomLeft = sortedByDiff[0];
        var topRight = sortedByDiff[3];

        return new Quadrilateral(topLeft, topRight, bottomRight, bottomLeft);
    }

    /// <summary>
    /// Returns all corners as an array in clockwise order.
    /// </summary>
    public PointF[] ToArray() => new[] { TopLeft, TopRight, BottomRight, BottomLeft };

    /// <summary>
    /// Calculates the width of the document (average of top and bottom edges).
    /// </summary>
    public float Width => (TopLeft.DistanceTo(TopRight) + BottomLeft.DistanceTo(BottomRight)) / 2;

    /// <summary>
    /// Calculates the height of the document (average of left and right edges).
    /// </summary>
    public float Height => (TopLeft.DistanceTo(BottomLeft) + TopRight.DistanceTo(BottomRight)) / 2;

    /// <summary>
    /// Calculates the area of the quadrilateral using the Shoelace formula.
    /// </summary>
    public float Area
    {
        get
        {
            var pts = ToArray();
            float area = 0;
            for (int i = 0; i < 4; i++)
            {
                int j = (i + 1) % 4;
                area += pts[i].X * pts[j].Y;
                area -= pts[j].X * pts[i].Y;
            }
            return Math.Abs(area) / 2;
        }
    }

    /// <summary>
    /// Gets the center point of the quadrilateral.
    /// </summary>
    public PointF Center
    {
        get
        {
            var pts = ToArray();
            float cx = pts.Average(p => p.X);
            float cy = pts.Average(p => p.Y);
            return new PointF(cx, cy);
        }
    }
}

/// <summary>
/// Contains information about the detected document's orientation and angle.
/// </summary>
public class DocumentAngleInfo
{
    /// <summary>
    /// The rotation angle in degrees (-180 to 180).
    /// Positive values indicate clockwise rotation.
    /// </summary>
    public float RotationAngle { get; set; }

    /// <summary>
    /// The perspective skew angle in degrees for the horizontal axis.
    /// </summary>
    public float HorizontalSkew { get; set; }

    /// <summary>
    /// The perspective skew angle in degrees for the vertical axis.
    /// </summary>
    public float VerticalSkew { get; set; }

    /// <summary>
    /// Indicates if the document appears to be upside down.
    /// </summary>
    public bool IsUpsideDown { get; set; }

    /// <summary>
    /// Confidence score for the angle detection (0.0 to 1.0).
    /// </summary>
    public float Confidence { get; set; }

    public override string ToString() =>
        $"Rotation: {RotationAngle:F1}°, HSkew: {HorizontalSkew:F1}°, VSkew: {VerticalSkew:F1}°, Confidence: {Confidence:P0}";
}

/// <summary>
/// Represents a binary mask indicating the document region in the image.
/// </summary>
public class DocumentMask : IDisposable
{
    private bool _disposed;
    private readonly byte[] _maskData;

    public int Width { get; }
    public int Height { get; }

    public DocumentMask(int width, int height)
    {
        Width = width;
        Height = height;
        _maskData = new byte[width * height];
    }

    public DocumentMask(int width, int height, byte[] data)
    {
        if (data.Length != width * height)
            throw new ArgumentException("Data size doesn't match dimensions");

        Width = width;
        Height = height;
        _maskData = data;
    }

    /// <summary>
    /// Gets or sets the mask value at the specified position.
    /// 0 = background, 255 = document region.
    /// </summary>
    public byte this[int x, int y]
    {
        get => _maskData[y * Width + x];
        set => _maskData[y * Width + x] = value;
    }

    /// <summary>
    /// Returns the raw mask data as a byte array.
    /// </summary>
    public byte[] ToByteArray() => (byte[])_maskData.Clone();

    /// <summary>
    /// Creates a mask from a quadrilateral by filling the polygon.
    /// </summary>
    public static DocumentMask FromQuadrilateral(Quadrilateral quad, int width, int height)
    {
        var mask = new DocumentMask(width, height);
        var points = quad.ToArray();

        // Scanline fill algorithm for the quadrilateral
        int minY = (int)Math.Max(0, points.Min(p => p.Y));
        int maxY = (int)Math.Min(height - 1, points.Max(p => p.Y));

        for (int y = minY; y <= maxY; y++)
        {
            var intersections = new List<float>();

            for (int i = 0; i < 4; i++)
            {
                var p1 = points[i];
                var p2 = points[(i + 1) % 4];

                if ((p1.Y <= y && p2.Y > y) || (p2.Y <= y && p1.Y > y))
                {
                    float x = p1.X + (y - p1.Y) / (p2.Y - p1.Y) * (p2.X - p1.X);
                    intersections.Add(x);
                }
            }

            intersections.Sort();

            for (int i = 0; i < intersections.Count - 1; i += 2)
            {
                int startX = (int)Math.Max(0, intersections[i]);
                int endX = (int)Math.Min(width - 1, intersections[i + 1]);

                for (int x = startX; x <= endX; x++)
                {
                    mask[x, y] = 255;
                }
            }
        }

        return mask;
    }

    /// <summary>
    /// Converts the mask to an SKBitmap for visualization.
    /// </summary>
    public SKBitmap ToSKBitmap()
    {
        var bitmap = new SKBitmap(Width, Height, SKColorType.Gray8, SKAlphaType.Opaque);
        var pixels = bitmap.GetPixels();

        unsafe
        {
            byte* ptr = (byte*)pixels.ToPointer();
            for (int i = 0; i < _maskData.Length; i++)
            {
                ptr[i] = _maskData[i];
            }
        }

        return bitmap;
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

/// <summary>
/// Contains the complete result of document detection.
/// </summary>
public class DetectionResult : IDisposable
{
    private bool _disposed;

    /// <summary>
    /// Indicates whether a document was successfully detected.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// The detected document corners as a quadrilateral.
    /// </summary>
    public Quadrilateral? Corners { get; set; }

    /// <summary>
    /// The binary mask indicating the document region.
    /// </summary>
    public DocumentMask? Mask { get; set; }

    /// <summary>
    /// Information about the document's orientation and angle.
    /// </summary>
    public DocumentAngleInfo? AngleInfo { get; set; }

    /// <summary>
    /// Overall confidence score for the detection (0.0 to 1.0).
    /// </summary>
    public float Confidence { get; set; }

    /// <summary>
    /// Error message if detection failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// The original image dimensions.
    /// </summary>
    public (int Width, int Height) OriginalSize { get; set; }

    /// <summary>
    /// Creates a successful detection result.
    /// </summary>
    public static DetectionResult Successful(
        Quadrilateral corners,
        DocumentMask mask,
        DocumentAngleInfo angleInfo,
        float confidence,
        int width,
        int height)
    {
        return new DetectionResult
        {
            Success = true,
            Corners = corners,
            Mask = mask,
            AngleInfo = angleInfo,
            Confidence = confidence,
            OriginalSize = (width, height)
        };
    }

    /// <summary>
    /// Creates a failed detection result.
    /// </summary>
    public static DetectionResult Failed(string errorMessage, int width = 0, int height = 0)
    {
        return new DetectionResult
        {
            Success = false,
            ErrorMessage = errorMessage,
            OriginalSize = (width, height)
        };
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            Mask?.Dispose();
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}

/// <summary>
/// Configuration options for document detection.
/// </summary>
public class DetectionOptions
{
    /// <summary>
    /// Minimum area ratio of the detected document relative to the image.
    /// Documents smaller than this ratio will be ignored. Default: 0.1 (10%)
    /// </summary>
    public float MinAreaRatio { get; set; } = 0.1f;

    /// <summary>
    /// Maximum area ratio of the detected document relative to the image.
    /// Documents larger than this ratio will be ignored. Default: 0.95 (95%)
    /// </summary>
    public float MaxAreaRatio { get; set; } = 0.95f;

    /// <summary>
    /// Epsilon factor for polygon approximation (relative to perimeter).
    /// Lower values result in more accurate but complex polygons. Default: 0.02
    /// </summary>
    public float ApproximationEpsilon { get; set; } = 0.02f;

    /// <summary>
    /// Gaussian blur kernel size for noise reduction. Must be odd. Default: 5
    /// </summary>
    public int BlurKernelSize { get; set; } = 5;

    /// <summary>
    /// Low threshold for Canny edge detection. Default: 50
    /// </summary>
    public int CannyLowThreshold { get; set; } = 50;

    /// <summary>
    /// High threshold for Canny edge detection. Default: 150
    /// </summary>
    public int CannyHighThreshold { get; set; } = 150;

    /// <summary>
    /// Whether to apply adaptive thresholding before edge detection. Default: true
    /// </summary>
    public bool UseAdaptiveThreshold { get; set; } = true;

    /// <summary>
    /// Block size for adaptive thresholding. Must be odd. Default: 11
    /// </summary>
    public int AdaptiveBlockSize { get; set; } = 11;

    /// <summary>
    /// Whether to enhance contrast before processing. Default: true
    /// </summary>
    public bool EnhanceContrast { get; set; } = true;

    /// <summary>
    /// Scale factor for processing (1.0 = original size). 
    /// Lower values are faster but less accurate. Default: 0.5
    /// </summary>
    public float ProcessingScale { get; set; } = 0.5f;
}
