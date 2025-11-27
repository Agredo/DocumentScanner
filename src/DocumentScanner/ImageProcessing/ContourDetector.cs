using DocumentScanner.Core;

namespace DocumentScanner.ImageProcessing;

/// <summary>
/// Implements contour detection and polygon operations.
/// </summary>
public static class ContourDetector
{
    /// <summary>
    /// Represents a detected contour as a list of points.
    /// </summary>
    public class Contour
    {
        public List<PointF> Points { get; set; } = new();

        /// <summary>
        /// Calculates the perimeter (arc length) of the contour.
        /// </summary>
        public float Perimeter
        {
            get
            {
                if (Points.Count < 2) return 0;

                float perimeter = 0;
                for (int i = 0; i < Points.Count; i++)
                {
                    int next = (i + 1) % Points.Count;
                    perimeter += Points[i].DistanceTo(Points[next]);
                }
                return perimeter;
            }
        }

        /// <summary>
        /// Calculates the area of the contour using the Shoelace formula.
        /// </summary>
        public float Area
        {
            get
            {
                if (Points.Count < 3) return 0;

                float area = 0;
                for (int i = 0; i < Points.Count; i++)
                {
                    int j = (i + 1) % Points.Count;
                    area += Points[i].X * Points[j].Y;
                    area -= Points[j].X * Points[i].Y;
                }
                return Math.Abs(area) / 2;
            }
        }

        /// <summary>
        /// Gets the bounding rectangle of the contour.
        /// </summary>
        public (float X, float Y, float Width, float Height) BoundingRect
        {
            get
            {
                if (Points.Count == 0) return (0, 0, 0, 0);

                float minX = Points.Min(p => p.X);
                float maxX = Points.Max(p => p.X);
                float minY = Points.Min(p => p.Y);
                float maxY = Points.Max(p => p.Y);

                return (minX, minY, maxX - minX, maxY - minY);
            }
        }

        /// <summary>
        /// Checks if the contour is convex.
        /// </summary>
        public bool IsConvex
        {
            get
            {
                if (Points.Count < 3) return false;

                bool? sign = null;

                for (int i = 0; i < Points.Count; i++)
                {
                    var p1 = Points[i];
                    var p2 = Points[(i + 1) % Points.Count];
                    var p3 = Points[(i + 2) % Points.Count];

                    float cross = (p2.X - p1.X) * (p3.Y - p2.Y) - (p2.Y - p1.Y) * (p3.X - p2.X);

                    if (Math.Abs(cross) > 0.0001f)
                    {
                        bool currentSign = cross > 0;
                        if (sign == null)
                        {
                            sign = currentSign;
                        }
                        else if (sign != currentSign)
                        {
                            return false;
                        }
                    }
                }

                return true;
            }
        }
    }

    /// <summary>
    /// Finds contours in a binary edge image using the Suzuki-Abe algorithm (simplified).
    /// </summary>
    public static List<Contour> FindContours(byte[,] binaryImage)
    {
        int height = binaryImage.GetLength(0);
        int width = binaryImage.GetLength(1);

        // Create a copy to mark visited pixels
        var labels = new int[height, width];
        var contours = new List<Contour>();
        int currentLabel = 1;

        // Moore neighborhood (8-connected) in clockwise order
        // Starting from East (right) and going clockwise
        int[] dx = { 1, 1, 0, -1, -1, -1, 0, 1 };
        int[] dy = { 0, 1, 1, 1, 0, -1, -1, -1 };

        // Scan for contour start points - need border pixels
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                // Found an unlabeled foreground pixel
                if (binaryImage[y, x] > 0 && labels[y, x] == 0)
                {
                    // Check if it's a border pixel (has at least one background neighbor)
                    bool isBorder = false;
                    
                    // Check 4-connected neighbors for background
                    if (x == 0 || x == width - 1 || y == 0 || y == height - 1)
                    {
                        isBorder = true;
                    }
                    else
                    {
                        // Check if any neighbor is background
                        for (int dir = 0; dir < 8; dir++)
                        {
                            int nx = x + dx[dir];
                            int ny = y + dy[dir];
                            if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                            {
                                if (binaryImage[ny, nx] == 0)
                                {
                                    isBorder = true;
                                    break;
                                }
                            }
                            else
                            {
                                isBorder = true;
                                break;
                            }
                        }
                    }
                    
                    if (isBorder)
                    {
                        var contour = TraceContour(binaryImage, labels, x, y, currentLabel, dx, dy);
                        if (contour.Points.Count >= 3)
                        {
                            contours.Add(contour);
                        }
                        currentLabel++;
                    }
                    else
                    {
                        // Mark isolated interior pixels
                        labels[y, x] = currentLabel;
                    }
                }
            }
        }

        return contours;
    }

    /// <summary>
    /// Traces a single contour starting from the given point using Moore-Neighbor tracing.
    /// </summary>
    private static Contour TraceContour(byte[,] image, int[,] labels, int startX, int startY, int label,
        int[] dx, int[] dy)
    {
        var contour = new Contour();
        int height = image.GetLength(0);
        int width = image.GetLength(1);

        int x = startX;
        int y = startY;
        
        // Find the initial search direction by looking for a background pixel
        // Start searching from the left (West) direction
        int backtrackDir = 6; // West
        
        // Find first background pixel in clockwise order starting from West
        for (int i = 0; i < 8; i++)
        {
            int checkDir = (backtrackDir + i) % 8;
            int nx = x + dx[checkDir];
            int ny = y + dy[checkDir];
            
            bool isBackground = false;
            if (nx < 0 || nx >= width || ny < 0 || ny >= height)
            {
                isBackground = true;
            }
            else if (image[ny, nx] == 0)
            {
                isBackground = true;
            }
            
            if (isBackground)
            {
                backtrackDir = checkDir;
                break;
            }
        }

        int firstMoveDir = -1;
        int currentDir = backtrackDir;

        do
        {
            // Add current point to contour
            contour.Points.Add(new PointF(x, y));
            labels[y, x] = label;

            // Search for next boundary pixel in Moore neighborhood
            // Start from 90 degrees counterclockwise from the direction we came from
            int searchStartDir = (currentDir + 6) % 8; // +6 is equivalent to -2 in mod 8
            
            bool found = false;
            int newDir = searchStartDir;
            
            for (int i = 0; i < 8; i++)
            {
                int checkDir = (searchStartDir + i) % 8;
                int nx = x + dx[checkDir];
                int ny = y + dy[checkDir];

                // Check bounds
                if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                {
                    if (image[ny, nx] > 0)
                    {
                        // Found next foreground pixel
                        x = nx;
                        y = ny;
                        newDir = checkDir;
                        found = true;
                        break;
                    }
                }
            }

            if (!found)
            {
                // Isolated pixel or end of contour
                break;
            }

            // Remember the first move direction
            if (firstMoveDir == -1)
            {
                firstMoveDir = newDir;
            }

            currentDir = newDir;

            // Termination condition: back at start with same entry direction
            if (x == startX && y == startY && contour.Points.Count > 2)
            {
                // We've completed the loop
                break;
            }

            // Safety check to prevent infinite loops
            if (contour.Points.Count > width * height)
            {
                break;
            }

        } while (true);

        return contour;
    }

    /// <summary>
    /// Approximates a contour with a polygon using the Douglas-Peucker algorithm.
    /// </summary>
    public static Contour ApproximatePolygon(Contour contour, float epsilon)
    {
        if (contour.Points.Count < 3)
            return contour;

        var points = contour.Points;

        // For closed contours, we need special handling
        var simplified = DouglasPeuckerClosed(points, epsilon);

        return new Contour { Points = simplified };
    }

    /// <summary>
    /// Douglas-Peucker algorithm for closed polygons.
    /// </summary>
    private static List<PointF> DouglasPeuckerClosed(List<PointF> points, float epsilon)
    {
        if (points.Count < 4)
            return new List<PointF>(points);

        // Find the two points with maximum distance
        float maxDist = 0;
        int index1 = 0, index2 = 0;

        for (int i = 0; i < points.Count; i++)
        {
            for (int j = i + 1; j < points.Count; j++)
            {
                float dist = points[i].DistanceTo(points[j]);
                if (dist > maxDist)
                {
                    maxDist = dist;
                    index1 = i;
                    index2 = j;
                }
            }
        }

        // Split the contour at these two points and simplify each half
        var firstHalf = new List<PointF>();
        var secondHalf = new List<PointF>();

        for (int i = index1; i != index2; i = (i + 1) % points.Count)
        {
            firstHalf.Add(points[i]);
        }
        firstHalf.Add(points[index2]);

        for (int i = index2; i != index1; i = (i + 1) % points.Count)
        {
            secondHalf.Add(points[i]);
        }
        secondHalf.Add(points[index1]);

        // Simplify both halves
        var simplifiedFirst = DouglasPeucker(firstHalf, epsilon);
        var simplifiedSecond = DouglasPeucker(secondHalf, epsilon);

        // Combine results (remove duplicate endpoint)
        var result = new List<PointF>(simplifiedFirst);
        if (simplifiedSecond.Count > 1)
        {
            result.AddRange(simplifiedSecond.Skip(1).Take(simplifiedSecond.Count - 2));
        }

        return result;
    }

    /// <summary>
    /// Standard Douglas-Peucker algorithm for open polylines.
    /// </summary>
    private static List<PointF> DouglasPeucker(List<PointF> points, float epsilon)
    {
        if (points.Count < 3)
            return new List<PointF>(points);

        // Find the point with maximum distance from the line between first and last
        float maxDist = 0;
        int maxIndex = 0;

        var start = points[0];
        var end = points[^1];

        for (int i = 1; i < points.Count - 1; i++)
        {
            float dist = PerpendicularDistance(points[i], start, end);
            if (dist > maxDist)
            {
                maxDist = dist;
                maxIndex = i;
            }
        }

        // If max distance is greater than epsilon, recursively simplify
        if (maxDist > epsilon)
        {
            var firstPart = DouglasPeucker(points.Take(maxIndex + 1).ToList(), epsilon);
            var secondPart = DouglasPeucker(points.Skip(maxIndex).ToList(), epsilon);

            // Combine results (remove duplicate point)
            var result = new List<PointF>(firstPart);
            result.AddRange(secondPart.Skip(1));
            return result;
        }
        else
        {
            // Just keep the endpoints
            return new List<PointF> { start, end };
        }
    }

    /// <summary>
    /// Calculates the perpendicular distance from a point to a line.
    /// </summary>
    private static float PerpendicularDistance(PointF point, PointF lineStart, PointF lineEnd)
    {
        float dx = lineEnd.X - lineStart.X;
        float dy = lineEnd.Y - lineStart.Y;

        float lineLengthSquared = dx * dx + dy * dy;

        if (lineLengthSquared < 0.0001f)
            return point.DistanceTo(lineStart);

        // Calculate perpendicular distance
        float numerator = Math.Abs(dy * point.X - dx * point.Y + lineEnd.X * lineStart.Y - lineEnd.Y * lineStart.X);
        return numerator / MathF.Sqrt(lineLengthSquared);
    }

    /// <summary>
    /// Computes the convex hull of a set of points using Graham scan.
    /// </summary>
    public static List<PointF> ConvexHull(List<PointF> points)
    {
        if (points.Count < 3)
            return new List<PointF>(points);

        // Find the point with lowest y-coordinate (and leftmost if tie)
        var pivot = points.OrderBy(p => p.Y).ThenBy(p => p.X).First();

        // Sort points by polar angle with pivot
        var sortedPoints = points
            .Where(p => !p.Equals(pivot))
            .OrderBy(p => Math.Atan2(p.Y - pivot.Y, p.X - pivot.X))
            .ThenBy(p => p.DistanceTo(pivot))
            .ToList();

        var hull = new Stack<PointF>();
        hull.Push(pivot);

        foreach (var point in sortedPoints)
        {
            while (hull.Count > 1)
            {
                var top = hull.Pop();
                var nextToTop = hull.Peek();

                // Cross product to determine turn direction
                float cross = CrossProduct(nextToTop, top, point);

                if (cross > 0)
                {
                    hull.Push(top);
                    break;
                }
            }
            hull.Push(point);
        }

        return hull.Reverse().ToList();
    }

    /// <summary>
    /// Calculates the cross product of vectors (p2-p1) and (p3-p2).
    /// Positive = counter-clockwise turn, Negative = clockwise turn.
    /// </summary>
    private static float CrossProduct(PointF p1, PointF p2, PointF p3)
    {
        return (p2.X - p1.X) * (p3.Y - p2.Y) - (p2.Y - p1.Y) * (p3.X - p2.X);
    }

    /// <summary>
    /// Finds the minimum area bounding rectangle for a contour.
    /// </summary>
    public static (PointF Center, float Width, float Height, float Angle) MinAreaRect(List<PointF> points)
    {
        if (points.Count < 3)
            return (PointF.Empty, 0, 0, 0);

        var hull = ConvexHull(points);
        if (hull.Count < 3)
            return (PointF.Empty, 0, 0, 0);

        float minArea = float.MaxValue;
        PointF bestCenter = PointF.Empty;
        float bestWidth = 0, bestHeight = 0, bestAngle = 0;

        // Rotating calipers approach
        for (int i = 0; i < hull.Count; i++)
        {
            var p1 = hull[i];
            var p2 = hull[(i + 1) % hull.Count];

            // Calculate the angle of this edge
            float angle = MathF.Atan2(p2.Y - p1.Y, p2.X - p1.X);

            // Rotate all points to align this edge with x-axis
            var rotated = hull.Select(p => RotatePoint(p, -angle)).ToList();

            // Find bounding box of rotated points
            float minX = rotated.Min(p => p.X);
            float maxX = rotated.Max(p => p.X);
            float minY = rotated.Min(p => p.Y);
            float maxY = rotated.Max(p => p.Y);

            float width = maxX - minX;
            float height = maxY - minY;
            float area = width * height;

            if (area < minArea)
            {
                minArea = area;
                bestWidth = width;
                bestHeight = height;
                bestAngle = angle * 180 / MathF.PI;

                // Calculate center in rotated space, then rotate back
                var centerRotated = new PointF((minX + maxX) / 2, (minY + maxY) / 2);
                bestCenter = RotatePoint(centerRotated, angle);
            }
        }

        return (bestCenter, bestWidth, bestHeight, bestAngle);
    }

    /// <summary>
    /// Rotates a point around the origin by the given angle (radians).
    /// </summary>
    private static PointF RotatePoint(PointF point, float angle)
    {
        float cos = MathF.Cos(angle);
        float sin = MathF.Sin(angle);
        return new PointF(
            point.X * cos - point.Y * sin,
            point.X * sin + point.Y * cos
        );
    }

    /// <summary>
    /// Alternative contour detection using a simpler connected components approach.
    /// Useful for debugging and comparison with the Moore-Neighbor tracing.
    /// </summary>
    public static List<Contour> FindContoursSimple(byte[,] binaryImage)
    {
        int height = binaryImage.GetLength(0);
        int width = binaryImage.GetLength(1);

        var visited = new bool[height, width];
        var contours = new List<Contour>();

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (binaryImage[y, x] > 0 && !visited[y, x])
                {
                    // Check if this is a boundary pixel
                    bool isBoundary = false;
                    
                    // Check 8-neighbors
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            
                            int nx = x + dx;
                            int ny = y + dy;
                            
                            if (nx < 0 || nx >= width || ny < 0 || ny >= height || binaryImage[ny, nx] == 0)
                            {
                                isBoundary = true;
                                break;
                            }
                        }
                        if (isBoundary) break;
                    }
                    
                    if (isBoundary)
                    {
                        var contour = ExtractContourBFS(binaryImage, visited, x, y);
                        if (contour.Points.Count >= 3)
                        {
                            contours.Add(contour);
                        }
                    }
                }
            }
        }

        return contours;
    }

    /// <summary>
    /// Extracts a contour using BFS (breadth-first search) to find all connected boundary pixels.
    /// </summary>
    private static Contour ExtractContourBFS(byte[,] image, bool[,] visited, int startX, int startY)
    {
        var contour = new Contour();
        var queue = new Queue<(int X, int Y)>();
        
        int height = image.GetLength(0);
        int width = image.GetLength(1);
        
        queue.Enqueue((startX, startY));
        visited[startY, startX] = true;
        
        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();
            
            // Check if this is a boundary pixel
            bool isBoundary = false;
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    
                    int nx = x + dx;
                    int ny = y + dy;
                    
                    if (nx < 0 || nx >= width || ny < 0 || ny >= height || image[ny, nx] == 0)
                    {
                        isBoundary = true;
                        break;
                    }
                }
                if (isBoundary) break;
            }
            
            if (isBoundary)
            {
                contour.Points.Add(new PointF(x, y));
                
                // Add unvisited neighbors to queue
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        
                        int nx = x + dx;
                        int ny = y + dy;
                        
                        if (nx >= 0 && nx < width && ny >= 0 && ny < height 
                            && image[ny, nx] > 0 && !visited[ny, nx])
                        {
                            visited[ny, nx] = true;
                            queue.Enqueue((nx, ny));
                        }
                    }
                }
            }
        }
        
        return contour;
    }
}
