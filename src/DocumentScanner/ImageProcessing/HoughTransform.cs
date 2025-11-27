using DocumentScanner.Core;

namespace DocumentScanner.ImageProcessing;

/// <summary>
/// Implements the Hough Line Transform for detecting lines in images.
/// </summary>
public static class HoughTransform
{
    /// <summary>
    /// Represents a detected line in polar coordinates.
    /// </summary>
    public struct HoughLine
    {
        /// <summary>
        /// Distance from the origin to the closest point on the line.
        /// </summary>
        public float Rho { get; set; }

        /// <summary>
        /// Angle of the perpendicular from the origin to the line (in radians).
        /// </summary>
        public float Theta { get; set; }

        /// <summary>
        /// Number of votes/accumulator value for this line.
        /// </summary>
        public int Votes { get; set; }

        /// <summary>
        /// Converts the line to two endpoints for drawing.
        /// </summary>
        public (PointF Start, PointF End) ToEndpoints(int imageWidth, int imageHeight)
        {
            float cos = MathF.Cos(Theta);
            float sin = MathF.Sin(Theta);
            float x0 = cos * Rho;
            float y0 = sin * Rho;

            // Extend line to image boundaries
            float length = imageWidth + imageHeight;
            float x1 = x0 + length * (-sin);
            float y1 = y0 + length * cos;
            float x2 = x0 - length * (-sin);
            float y2 = y0 - length * cos;

            return (new PointF(x1, y1), new PointF(x2, y2));
        }

        /// <summary>
        /// Returns the angle in degrees (0-180).
        /// </summary>
        public float AngleDegrees => Theta * 180 / MathF.PI;
    }

    /// <summary>
    /// Detects lines in a binary edge image using the Standard Hough Transform.
    /// </summary>
    /// <param name="edges">Binary edge image (non-zero = edge)</param>
    /// <param name="rhoResolution">Distance resolution in pixels. Default: 1</param>
    /// <param name="thetaResolution">Angle resolution in radians. Default: π/180</param>
    /// <param name="threshold">Minimum votes to be considered a line. Default: 100</param>
    /// <returns>List of detected lines sorted by votes (descending)</returns>
    public static List<HoughLine> DetectLines(
        byte[,] edges,
        float rhoResolution = 1,
        float thetaResolution = MathF.PI / 180,
        int threshold = 100)
    {
        int height = edges.GetLength(0);
        int width = edges.GetLength(1);

        // Calculate accumulator dimensions
        float diagonal = MathF.Sqrt(width * width + height * height);
        int rhoMax = (int)Math.Ceiling(diagonal / rhoResolution);
        int thetaMax = (int)Math.Ceiling(MathF.PI / thetaResolution);

        // Create accumulator array
        var accumulator = new int[2 * rhoMax, thetaMax];

        // Precompute sin and cos values
        float[] sinTable = new float[thetaMax];
        float[] cosTable = new float[thetaMax];
        for (int t = 0; t < thetaMax; t++)
        {
            float theta = t * thetaResolution;
            sinTable[t] = MathF.Sin(theta);
            cosTable[t] = MathF.Cos(theta);
        }

        // Vote for each edge pixel
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (edges[y, x] > 0)
                {
                    for (int t = 0; t < thetaMax; t++)
                    {
                        float rho = x * cosTable[t] + y * sinTable[t];
                        int rhoIndex = (int)(rho / rhoResolution) + rhoMax;

                        if (rhoIndex >= 0 && rhoIndex < 2 * rhoMax)
                        {
                            accumulator[rhoIndex, t]++;
                        }
                    }
                }
            }
        }

        // Find peaks above threshold
        var lines = new List<HoughLine>();

        for (int r = 0; r < 2 * rhoMax; r++)
        {
            for (int t = 0; t < thetaMax; t++)
            {
                if (accumulator[r, t] >= threshold)
                {
                    // Check if it's a local maximum (3x3 neighborhood)
                    bool isMax = true;
                    int votes = accumulator[r, t];

                    for (int dr = -1; dr <= 1 && isMax; dr++)
                    {
                        for (int dt = -1; dt <= 1 && isMax; dt++)
                        {
                            if (dr == 0 && dt == 0) continue;

                            int nr = r + dr;
                            int nt = (t + dt + thetaMax) % thetaMax;

                            if (nr >= 0 && nr < 2 * rhoMax)
                            {
                                if (accumulator[nr, nt] > votes)
                                {
                                    isMax = false;
                                }
                            }
                        }
                    }

                    if (isMax)
                    {
                        lines.Add(new HoughLine
                        {
                            Rho = (r - rhoMax) * rhoResolution,
                            Theta = t * thetaResolution,
                            Votes = votes
                        });
                    }
                }
            }
        }

        return lines.OrderByDescending(l => l.Votes).ToList();
    }

    /// <summary>
    /// Detects line segments using the Probabilistic Hough Transform.
    /// </summary>
    public static List<(PointF Start, PointF End)> DetectLineSegments(
        byte[,] edges,
        int threshold = 50,
        float minLineLength = 50,
        float maxLineGap = 10)
    {
        int height = edges.GetLength(0);
        int width = edges.GetLength(1);

        // Collect edge points
        var edgePoints = new List<(int X, int Y)>();
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (edges[y, x] > 0)
                {
                    edgePoints.Add((x, y));
                }
            }
        }

        var segments = new List<(PointF Start, PointF End)>();
        var used = new bool[height, width];
        var random = new Random(42); // Deterministic for reproducibility

        // Shuffle edge points
        edgePoints = edgePoints.OrderBy(_ => random.Next()).ToList();

        foreach (var (px, py) in edgePoints)
        {
            if (used[py, px]) continue;

            // Try different angles
            float bestLength = 0;
            PointF bestStart = PointF.Empty;
            PointF bestEnd = PointF.Empty;

            for (float angle = 0; angle < MathF.PI; angle += MathF.PI / 180)
            {
                float cos = MathF.Cos(angle);
                float sin = MathF.Sin(angle);

                // Trace line in both directions
                var points = TraceLine(edges, used, px, py, cos, sin, maxLineGap, width, height);
                points.AddRange(TraceLine(edges, used, px, py, -cos, -sin, maxLineGap, width, height));

                if (points.Count >= threshold)
                {
                    // Find extent of line segment
                    float minT = 0, maxT = 0;

                    foreach (var (qx, qy) in points)
                    {
                        float t = (qx - px) * cos + (qy - py) * sin;
                        minT = Math.Min(minT, t);
                        maxT = Math.Max(maxT, t);
                    }

                    float length = maxT - minT;

                    if (length >= minLineLength && length > bestLength)
                    {
                        bestLength = length;
                        bestStart = new PointF(px + minT * cos, py + minT * sin);
                        bestEnd = new PointF(px + maxT * cos, py + maxT * sin);
                    }
                }
            }

            if (bestLength >= minLineLength)
            {
                segments.Add((bestStart, bestEnd));

                // Mark points as used
                float dx = bestEnd.X - bestStart.X;
                float dy = bestEnd.Y - bestStart.Y;
                float length = MathF.Sqrt(dx * dx + dy * dy);

                for (float t = 0; t <= length; t += 1)
                {
                    int x = (int)(bestStart.X + t * dx / length);
                    int y = (int)(bestStart.Y + t * dy / length);

                    if (x >= 0 && x < width && y >= 0 && y < height)
                    {
                        used[y, x] = true;
                    }
                }
            }
        }

        return segments;
    }

    /// <summary>
    /// Traces a line from a starting point in a given direction.
    /// </summary>
    private static List<(int X, int Y)> TraceLine(
        byte[,] edges, bool[,] used,
        int startX, int startY,
        float dx, float dy,
        float maxGap,
        int width, int height)
    {
        var points = new List<(int X, int Y)>();
        float gap = 0;

        for (float t = 1; gap <= maxGap; t += 1)
        {
            int x = (int)(startX + t * dx);
            int y = (int)(startY + t * dy);

            if (x < 0 || x >= width || y < 0 || y >= height)
                break;

            if (edges[y, x] > 0 && !used[y, x])
            {
                points.Add((x, y));
                gap = 0;
            }
            else
            {
                gap += 1;
            }
        }

        return points;
    }

    /// <summary>
    /// Finds the vanishing points from detected lines to estimate perspective.
    /// </summary>
    public static PointF? FindVanishingPoint(List<HoughLine> lines, float angleToleranceDegrees = 10)
    {
        if (lines.Count < 2)
            return null;

        // Group lines by angle
        var groups = new List<List<HoughLine>>();

        foreach (var line in lines)
        {
            bool added = false;
            float angleDeg = line.AngleDegrees;

            foreach (var group in groups)
            {
                float groupAngle = group[0].AngleDegrees;
                float diff = Math.Abs(angleDeg - groupAngle);
                diff = Math.Min(diff, 180 - diff);

                if (diff < angleToleranceDegrees)
                {
                    group.Add(line);
                    added = true;
                    break;
                }
            }

            if (!added)
            {
                groups.Add(new List<HoughLine> { line });
            }
        }

        // Find intersection of two largest groups (excluding near-parallel lines)
        groups = groups.OrderByDescending(g => g.Sum(l => l.Votes)).ToList();

        for (int i = 0; i < groups.Count; i++)
        {
            for (int j = i + 1; j < groups.Count; j++)
            {
                float angle1 = groups[i][0].AngleDegrees;
                float angle2 = groups[j][0].AngleDegrees;
                float angleDiff = Math.Abs(angle1 - angle2);
                angleDiff = Math.Min(angleDiff, 180 - angleDiff);

                // Lines should be at least 30 degrees apart
                if (angleDiff > 30)
                {
                    var vp = FindIntersection(groups[i][0], groups[j][0]);
                    if (vp.HasValue)
                    {
                        return vp.Value;
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Finds the intersection point of two lines.
    /// </summary>
    private static PointF? FindIntersection(HoughLine line1, HoughLine line2)
    {
        float cos1 = MathF.Cos(line1.Theta);
        float sin1 = MathF.Sin(line1.Theta);
        float cos2 = MathF.Cos(line2.Theta);
        float sin2 = MathF.Sin(line2.Theta);

        float det = cos1 * sin2 - sin1 * cos2;

        if (Math.Abs(det) < 0.0001f)
            return null; // Lines are parallel

        float x = (line1.Rho * sin2 - line2.Rho * sin1) / det;
        float y = (line2.Rho * cos1 - line1.Rho * cos2) / det;

        return new PointF(x, y);
    }
}
