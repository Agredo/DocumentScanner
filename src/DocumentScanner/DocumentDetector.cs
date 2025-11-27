using DocumentScanner.Core;
using DocumentScanner.ImageProcessing;
using SkiaSharp;

namespace DocumentScanner;

/// <summary>
/// Interface for document detection operations.
/// </summary>
public interface IDocumentDetector
{
    /// <summary>
    /// Detects a document in the given image.
    /// </summary>
    /// <param name="imageBytes">The image as a byte array (PNG, JPEG, etc.)</param>
    /// <param name="options">Optional detection parameters</param>
    /// <returns>Detection result containing corners, mask, and angle information</returns>
    DetectionResult Detect(byte[] imageBytes, DetectionOptions? options = null);

    /// <summary>
    /// Detects a document in the given SKBitmap.
    /// </summary>
    DetectionResult Detect(SKBitmap bitmap, DetectionOptions? options = null);
}

/// <summary>
/// Main document detector class that implements document detection using
/// edge detection, contour finding, and quadrilateral approximation.
/// </summary>
public class DocumentDetector : IDocumentDetector
{
    private readonly DetectionOptions _defaultOptions;

    public DocumentDetector() : this(new DetectionOptions()) { }

    public DocumentDetector(DetectionOptions defaultOptions)
    {
        _defaultOptions = defaultOptions;
    }

    /// <inheritdoc />
    public DetectionResult Detect(byte[] imageBytes, DetectionOptions? options = null)
    {
        try
        {
            using var bitmap = ImageProcessor.LoadImage(imageBytes);
            return Detect(bitmap, options);
        }
        catch (Exception ex)
        {
            return DetectionResult.Failed($"Failed to load image: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public DetectionResult Detect(SKBitmap bitmap, DetectionOptions? options = null)
    {
        options ??= _defaultOptions;

        try
        {
            int originalWidth = bitmap.Width;
            int originalHeight = bitmap.Height;

            // Step 1: Scale down for faster processing
            int processWidth = (int)(originalWidth * options.ProcessingScale);
            int processHeight = (int)(originalHeight * options.ProcessingScale);
            float scaleX = (float)originalWidth / processWidth;
            float scaleY = (float)originalHeight / processHeight;

            using var scaledBitmap = ImageProcessor.Resize(bitmap, processWidth, processHeight);

            // Step 2: Convert to grayscale
            var grayscale = ImageProcessor.ToGrayscale(scaledBitmap);

            // Step 3: Enhance contrast if enabled
            if (options.EnhanceContrast)
            {
                if (options.UseSimpleHistogramEqualization)
                {
                    grayscale = ImageProcessor.EnhanceContrast(grayscale);
                }
                else
                {
                    grayscale = ImageProcessor.ApplyCLAHE(grayscale, 8, 2.0f);
                }
            }

            // Step 4: Apply stronger Gaussian blur to reduce texture noise from backgrounds
            // Use a larger kernel for noisy/textured images
            int blurKernel = options.BlurKernelSize;
            if (blurKernel < 7)
            {
                blurKernel = 7; // Increase minimum blur to reduce texture noise
            }
            var blurred = ImageProcessor.GaussianBlur(grayscale, blurKernel);

            // Step 5: Detect edges
            byte[,] edges;
            if (options.UseAdaptiveThreshold)
            {
                // Use adaptive thresholding followed by edge detection
                var thresholded = Thresholder.SauvolaThreshold(blurred, options.AdaptiveBlockSize);
                edges = EdgeDetector.Canny(thresholded, options.CannyLowThreshold, options.CannyHighThreshold);
            }
            else
            {
                edges = EdgeDetector.Canny(blurred, options.CannyLowThreshold, options.CannyHighThreshold);
            }

            // Step 6: Apply stronger morphological closing to connect document edges
            // Use larger kernel to better connect document edges
            edges = EdgeDetector.MorphologicalClose(edges, 5);

            // Step 7: Find contours
            // Quick check: if there are very few edge pixels, return early
            int edgeCount = 0;
            for (int y = 0; y < processHeight && edgeCount < 100; y++)
            {
                for (int x = 0; x < processWidth && edgeCount < 100; x++)
                {
                    if (edges[y, x] > 0) edgeCount++;
                }
            }
            
            if (edgeCount == 0)
            {
                return DetectionResult.Failed("No edges found in image", originalWidth, originalHeight);
            }
            
            // Use the simpler BFS-based contour detection (more robust, less prone to OutOfMemoryException)
            var contours = ContourDetector.FindContoursSimple(edges);

            if (contours.Count == 0)
            {
                return DetectionResult.Failed("No contours found in image", originalWidth, originalHeight);
            }

            // Step 8: Find the best quadrilateral
            var imageArea = processWidth * processHeight;
            var bestQuad = FindBestQuadrilateral(contours, imageArea, processWidth, processHeight, options);

            if (bestQuad == null)
            {
                return DetectionResult.Failed("No valid document quadrilateral found", originalWidth, originalHeight);
            }

            // Step 9: Scale corners back to original image size
            var scaledQuad = new Quadrilateral(
                new PointF(bestQuad.TopLeft.X * scaleX, bestQuad.TopLeft.Y * scaleY),
                new PointF(bestQuad.TopRight.X * scaleX, bestQuad.TopRight.Y * scaleY),
                new PointF(bestQuad.BottomRight.X * scaleX, bestQuad.BottomRight.Y * scaleY),
                new PointF(bestQuad.BottomLeft.X * scaleX, bestQuad.BottomLeft.Y * scaleY)
            );

            // Step 10: Calculate angle information
            var angleInfo = CalculateAngleInfo(scaledQuad);

            // Step 11: Create mask
            var mask = DocumentMask.FromQuadrilateral(scaledQuad, originalWidth, originalHeight);

            // Step 12: Calculate confidence score
            float confidence = CalculateConfidence(scaledQuad, originalWidth, originalHeight);

            return DetectionResult.Successful(scaledQuad, mask, angleInfo, confidence, originalWidth, originalHeight);
        }
        catch (Exception ex)
        {
            return DetectionResult.Failed($"Detection failed: {ex.Message}", bitmap.Width, bitmap.Height);
        }
    }

    /// <summary>
    /// Finds the best quadrilateral from a list of contours.
    /// </summary>
    private Quadrilateral? FindBestQuadrilateral(
        List<ContourDetector.Contour> contours,
        float imageArea,
        int imageWidth,
        int imageHeight,
        DetectionOptions options)
    {
        float minArea = imageArea * options.MinAreaRatio;
        float maxArea = imageArea * options.MaxAreaRatio;

        var candidates = new List<(Quadrilateral Quad, float Score)>();

        // Define border margin for detecting contours that touch edges
        float borderMargin = Math.Min(imageWidth, imageHeight) * 0.05f; // 5% margin

        foreach (var contour in contours)
        {
            // Use bounding box area instead of Shoelace area (BFS gives unordered points)
            var bounds = contour.BoundingRect;
            float boundingArea = bounds.Width * bounds.Height;
            
            if (boundingArea < minArea || boundingArea > maxArea)
            {
                continue;
            }

            // Check if contour touches image borders (documents usually have some margin)
            bool touchesBorder = 
                bounds.X < borderMargin || 
                bounds.Y < borderMargin ||
                (bounds.X + bounds.Width) > (imageWidth - borderMargin) ||
                (bounds.Y + bounds.Height) > (imageHeight - borderMargin);

            // Get convex hull
            var hull = ContourDetector.ConvexHull(contour.Points);

            if (hull.Count < 4)
            {
                continue;
            }

            // Approximate to polygon
            // Use bounding box perimeter instead of Shoelace (BFS gives unordered points)
            float boundingPerimeter = 2 * (bounds.Width + bounds.Height);
            float epsilon = options.ApproximationEpsilon * boundingPerimeter;
            var approximatedContour = new ContourDetector.Contour { Points = hull };
            var approx = ContourDetector.ApproximatePolygon(approximatedContour, epsilon);

            // Check if we got a quadrilateral
            if (approx.Points.Count == 4)
            {
                var quad = Quadrilateral.FromPoints(approx.Points.ToArray());
                
                if (!IsValidQuadrilateral(quad))
                {
                    continue;
                }
                
                float score = ScoreQuadrilateral(quad, imageArea, touchesBorder);

                if (score > 0)
                {
                    candidates.Add((quad, score));
                }
            }
            else if (approx.Points.Count > 4)
            {
                // Try to reduce to 4 points by finding the best 4
                var bestFourPoints = FindBestFourPoints(approx.Points);

                if (bestFourPoints != null)
                {
                    var quad = Quadrilateral.FromPoints(bestFourPoints);
                    
                    if (!IsValidQuadrilateral(quad))
                    {
                        continue;
                    }
                    
                    float score = ScoreQuadrilateral(quad, imageArea, touchesBorder);

                    if (score > 0)
                    {
                        candidates.Add((quad, score));
                    }
                }
            }
        }

        if (candidates.Count == 0)
            return null;

        // Return the quadrilateral with the highest score
        return candidates.OrderByDescending(c => c.Score).First().Quad;
    }

    /// <summary>
    /// Finds the best 4 points from a polygon that form a quadrilateral.
    /// </summary>
    private PointF[]? FindBestFourPoints(List<PointF> points)
    {
        if (points.Count < 4)
            return null;

        // Strategy: Find the 4 points that maximize the enclosed area
        // while maintaining reasonable angles

        float bestArea = 0;
        PointF[]? bestPoints = null;

        // Try all combinations of 4 points (feasible for small n)
        if (points.Count <= 8)
        {
            for (int i = 0; i < points.Count; i++)
            {
                for (int j = i + 1; j < points.Count; j++)
                {
                    for (int k = j + 1; k < points.Count; k++)
                    {
                        for (int l = k + 1; l < points.Count; l++)
                        {
                            var testPoints = new[] { points[i], points[j], points[k], points[l] };
                            var quad = Quadrilateral.FromPoints(testPoints);

                            if (IsValidQuadrilateral(quad) && quad.Area > bestArea)
                            {
                                bestArea = quad.Area;
                                bestPoints = testPoints;
                            }
                        }
                    }
                }
            }
        }
        else
        {
            // For larger polygons, use corner detection heuristic
            // Find 4 points with maximum total distance to centroid
            float cx = points.Average(p => p.X);
            float cy = points.Average(p => p.Y);
            var centroid = new PointF(cx, cy);

            var sortedByDistance = points
                .OrderByDescending(p => p.DistanceTo(centroid))
                .Take(6)
                .ToList();

            // Try combinations of the top 6 distant points
            for (int i = 0; i < sortedByDistance.Count; i++)
            {
                for (int j = i + 1; j < sortedByDistance.Count; j++)
                {
                    for (int k = j + 1; k < sortedByDistance.Count; k++)
                    {
                        for (int l = k + 1; l < sortedByDistance.Count; l++)
                        {
                            var testPoints = new[]
                            {
                                sortedByDistance[i], sortedByDistance[j],
                                sortedByDistance[k], sortedByDistance[l]
                            };
                            var quad = Quadrilateral.FromPoints(testPoints);

                            if (IsValidQuadrilateral(quad) && quad.Area > bestArea)
                            {
                                bestArea = quad.Area;
                                bestPoints = testPoints;
                            }
                        }
                    }
                }
            }
        }

        return bestPoints;
    }

    /// <summary>
    /// Checks if a quadrilateral is valid (convex with reasonable angles).
    /// </summary>
    private bool IsValidQuadrilateral(Quadrilateral quad)
    {
        var points = quad.ToArray();

        // Check for convexity by ensuring all cross products have the same sign
        bool? sign = null;

        for (int i = 0; i < 4; i++)
        {
            var p1 = points[i];
            var p2 = points[(i + 1) % 4];
            var p3 = points[(i + 2) % 4];

            float cross = (p2.X - p1.X) * (p3.Y - p2.Y) - (p2.Y - p1.Y) * (p3.X - p2.X);

            if (Math.Abs(cross) > 0.01f)
            {
                bool currentSign = cross > 0;
                if (sign == null)
                {
                    sign = currentSign;
                }
                else if (sign != currentSign)
                {
                    return false; // Not convex
                }
            }
        }

        // Check that all angles are reasonable (between 30 and 150 degrees)
        for (int i = 0; i < 4; i++)
        {
            var p1 = points[(i + 3) % 4];
            var p2 = points[i];
            var p3 = points[(i + 1) % 4];

            float angle = CalculateAngle(p1, p2, p3);

            if (angle < 30 || angle > 150)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Calculates the angle at p2 in degrees.
    /// </summary>
    private float CalculateAngle(PointF p1, PointF p2, PointF p3)
    {
        var v1 = new PointF(p1.X - p2.X, p1.Y - p2.Y);
        var v2 = new PointF(p3.X - p2.X, p3.Y - p2.Y);

        float dot = v1.X * v2.X + v1.Y * v2.Y;
        float mag1 = MathF.Sqrt(v1.X * v1.X + v1.Y * v1.Y);
        float mag2 = MathF.Sqrt(v2.X * v2.X + v2.Y * v2.Y);

        if (mag1 < 0.0001f || mag2 < 0.0001f)
            return 90;

        float cos = Math.Clamp(dot / (mag1 * mag2), -1, 1);
        return MathF.Acos(cos) * 180 / MathF.PI;
    }

    /// <summary>
    /// Scores a quadrilateral based on various criteria.
    /// </summary>
    private float ScoreQuadrilateral(Quadrilateral quad, float imageArea, bool touchesBorder)
    {
        float score = 0;

        // Factor 1: Area ratio (prefer larger documents, but not too large)
        float areaRatio = quad.Area / imageArea;
        // Penalize very small and very large areas
        if (areaRatio < 0.1f)
        {
            score += areaRatio * 500; // Small penalty
        }
        else if (areaRatio > 0.85f)
        {
            score += (1 - areaRatio) * 200; // Penalize very large (likely background)
        }
        else
        {
            score += areaRatio * 100;
        }

        // Factor 2: Aspect ratio similarity to common document formats
        float aspectRatio = quad.Width / quad.Height;

        // Common aspect ratios: A4 (0.707), Letter (0.773), 4:3 (0.75), 3:2 (0.667)
        float[] commonRatios = { 0.707f, 0.773f, 0.75f, 0.667f, 1.0f, 1.414f, 1.294f, 1.333f, 1.5f };
        float minRatioDiff = commonRatios.Min(r => Math.Abs(aspectRatio - r));
        score += (1 - minRatioDiff) * 30;

        // Factor 3: Corner angle regularity (prefer 90-degree angles)
        var corners = quad.ToArray();
        float angleDeviation = 0;

        for (int i = 0; i < 4; i++)
        {
            var p1 = corners[(i + 3) % 4];
            var p2 = corners[i];
            var p3 = corners[(i + 1) % 4];

            float angle = CalculateAngle(p1, p2, p3);
            angleDeviation += Math.Abs(90 - angle);
        }

        score += Math.Max(0, 40 - angleDeviation * 0.5f);

        // Factor 4: Edge length consistency
        float[] edgeLengths = {
            quad.TopLeft.DistanceTo(quad.TopRight),
            quad.TopRight.DistanceTo(quad.BottomRight),
            quad.BottomRight.DistanceTo(quad.BottomLeft),
            quad.BottomLeft.DistanceTo(quad.TopLeft)
        };

        float avgEdge = edgeLengths.Average();
        float edgeVariance = edgeLengths.Sum(e => MathF.Pow(e - avgEdge, 2)) / 4;
        float edgeStdDev = MathF.Sqrt(edgeVariance);
        float edgeRegularity = 1 - Math.Min(1, edgeStdDev / avgEdge);
        score += edgeRegularity * 20;

        // Factor 5: Penalize contours that touch image borders
        // Documents usually have some margin from the frame edges
        if (touchesBorder)
        {
            score *= 0.5f; // Heavy penalty for touching borders
        }

        return score;
    }

    /// <summary>
    /// Calculates angle and orientation information for the detected document.
    /// </summary>
    private DocumentAngleInfo CalculateAngleInfo(Quadrilateral quad)
    {
        var info = new DocumentAngleInfo();

        // Calculate rotation angle based on the top edge
        float topEdgeAngle = MathF.Atan2(
            quad.TopRight.Y - quad.TopLeft.Y,
            quad.TopRight.X - quad.TopLeft.X
        ) * 180 / MathF.PI;

        info.RotationAngle = topEdgeAngle;

        // Calculate horizontal skew (difference between top and bottom edge angles)
        float bottomEdgeAngle = MathF.Atan2(
            quad.BottomRight.Y - quad.BottomLeft.Y,
            quad.BottomRight.X - quad.BottomLeft.X
        ) * 180 / MathF.PI;

        info.HorizontalSkew = topEdgeAngle - bottomEdgeAngle;

        // Calculate vertical skew (difference between left and right edge angles)
        float leftEdgeAngle = MathF.Atan2(
            quad.BottomLeft.Y - quad.TopLeft.Y,
            quad.BottomLeft.X - quad.TopLeft.X
        ) * 180 / MathF.PI;

        float rightEdgeAngle = MathF.Atan2(
            quad.BottomRight.Y - quad.TopRight.Y,
            quad.BottomRight.X - quad.TopRight.X
        ) * 180 / MathF.PI;

        info.VerticalSkew = rightEdgeAngle - leftEdgeAngle;

        // Determine if document might be upside down
        // (This is a heuristic - actual text direction detection would require OCR)
        info.IsUpsideDown = Math.Abs(topEdgeAngle) > 90;

        // Calculate confidence based on how "rectangular" the angles are
        float totalSkew = Math.Abs(info.HorizontalSkew) + Math.Abs(info.VerticalSkew);
        info.Confidence = Math.Max(0, 1 - totalSkew / 90);

        return info;
    }

    /// <summary>
    /// Calculates an overall confidence score for the detection.
    /// </summary>
    private float CalculateConfidence(Quadrilateral quad, int imageWidth, int imageHeight)
    {
        float imageArea = imageWidth * imageHeight;

        // Factor 1: Area coverage (too small or too large is suspicious)
        float areaRatio = quad.Area / imageArea;
        float areaScore = 1 - 2 * Math.Abs(areaRatio - 0.5f);

        // Factor 2: Shape regularity (how close to a rectangle)
        var corners = quad.ToArray();
        float angleSum = 0;
        float angleDeviation = 0;

        for (int i = 0; i < 4; i++)
        {
            var p1 = corners[(i + 3) % 4];
            var p2 = corners[i];
            var p3 = corners[(i + 1) % 4];

            float angle = CalculateAngle(p1, p2, p3);
            angleSum += angle;
            angleDeviation += Math.Abs(90 - angle);
        }

        float shapeScore = Math.Max(0, 1 - angleDeviation / 180);

        // Factor 3: Edge parallelism
        float topAngle = MathF.Atan2(quad.TopRight.Y - quad.TopLeft.Y, quad.TopRight.X - quad.TopLeft.X);
        float bottomAngle = MathF.Atan2(quad.BottomRight.Y - quad.BottomLeft.Y, quad.BottomRight.X - quad.BottomLeft.X);
        float leftAngle = MathF.Atan2(quad.BottomLeft.Y - quad.TopLeft.Y, quad.BottomLeft.X - quad.TopLeft.X);
        float rightAngle = MathF.Atan2(quad.BottomRight.Y - quad.TopRight.Y, quad.BottomRight.X - quad.TopRight.X);

        float horizontalParallel = 1 - Math.Min(1, Math.Abs(topAngle - bottomAngle) * 2);
        float verticalParallel = 1 - Math.Min(1, Math.Abs(leftAngle - rightAngle) * 2);
        float parallelScore = (horizontalParallel + verticalParallel) / 2;

        // Combine scores
        return (areaScore * 0.3f + shapeScore * 0.4f + parallelScore * 0.3f);
    }
}
