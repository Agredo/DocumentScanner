namespace DocumentScanner.ImageProcessing;

/// <summary>
/// Implements edge detection algorithms including Sobel and Canny.
/// </summary>
public static class EdgeDetector
{
    /// <summary>
    /// Sobel operator kernels for edge detection.
    /// </summary>
    private static readonly int[,] SobelX = {
        { -1, 0, 1 },
        { -2, 0, 2 },
        { -1, 0, 1 }
    };

    private static readonly int[,] SobelY = {
        { -1, -2, -1 },
        {  0,  0,  0 },
        {  1,  2,  1 }
    };

    /// <summary>
    /// Applies Sobel edge detection to a grayscale image.
    /// Returns the gradient magnitude and optionally the gradient direction.
    /// </summary>
    public static (byte[,] Magnitude, float[,]? Direction) Sobel(byte[,] image, bool computeDirection = false)
    {
        int height = image.GetLength(0);
        int width = image.GetLength(1);

        var magnitude = new byte[height, width];
        var direction = computeDirection ? new float[height, width] : null;

        // We'll also store the actual gradient values for sub-pixel accuracy
        var gx = new float[height, width];
        var gy = new float[height, width];

        // Apply Sobel operators
        for (int y = 1; y < height - 1; y++)
        {
            for (int x = 1; x < width - 1; x++)
            {
                float sumX = 0;
                float sumY = 0;

                for (int ky = -1; ky <= 1; ky++)
                {
                    for (int kx = -1; kx <= 1; kx++)
                    {
                        byte pixel = image[y + ky, x + kx];
                        sumX += pixel * SobelX[ky + 1, kx + 1];
                        sumY += pixel * SobelY[ky + 1, kx + 1];
                    }
                }

                gx[y, x] = sumX;
                gy[y, x] = sumY;

                float mag = MathF.Sqrt(sumX * sumX + sumY * sumY);
                magnitude[y, x] = (byte)Math.Clamp(mag, 0, 255);

                if (direction != null)
                {
                    direction[y, x] = MathF.Atan2(sumY, sumX);
                }
            }
        }

        return (magnitude, direction);
    }

    /// <summary>
    /// Applies Canny edge detection to a grayscale image.
    /// </summary>
    public static byte[,] Canny(byte[,] image, int lowThreshold = 50, int highThreshold = 150)
    {
        int height = image.GetLength(0);
        int width = image.GetLength(1);

        // Step 1: Apply Gaussian blur (usually already done before calling this)
        // We'll skip this step assuming the image is pre-blurred

        // Step 2: Calculate gradients using Sobel
        var (sobelMag, sobelDir) = Sobel(image, computeDirection: true);

        // Step 3: Non-maximum suppression
        var nms = NonMaximumSuppression(sobelMag, sobelDir!);

        // Step 4: Double thresholding
        var thresholded = DoubleThreshold(nms, lowThreshold, highThreshold);

        // Step 5: Edge tracking by hysteresis
        var edges = HysteresisTracking(thresholded);

        return edges;
    }

    /// <summary>
    /// DEBUG version of Canny that exposes intermediate steps.
    /// </summary>
    public static (byte[,] Edges, byte[,] Nms, byte[,] Thresholded) CannyDebug(
        byte[,] image, int lowThreshold = 50, int highThreshold = 150)
    {
        int height = image.GetLength(0);
        int width = image.GetLength(1);

        // Step 2: Calculate gradients using Sobel
        var (sobelMag, sobelDir) = Sobel(image, computeDirection: true);

        // Step 3: Non-maximum suppression
        var nms = NonMaximumSuppression(sobelMag, sobelDir!);

        // Step 4: Double thresholding
        var thresholded = DoubleThreshold(nms, lowThreshold, highThreshold);

        // Step 5: Edge tracking by hysteresis
        var edges = HysteresisTracking(thresholded);

        return (edges, nms, thresholded);
    }

    /// <summary>
    /// Applies non-maximum suppression to thin edges.
    /// </summary>
    private static byte[,] NonMaximumSuppression(byte[,] magnitude, float[,] direction)
    {
        int height = magnitude.GetLength(0);
        int width = magnitude.GetLength(1);
        var result = new byte[height, width];

        for (int y = 1; y < height - 1; y++)
        {
            for (int x = 1; x < width - 1; x++)
            {
                float angle = direction[y, x];

                // Convert angle to degrees and normalize to 0-180
                float angleDeg = angle * 180 / MathF.PI;
                if (angleDeg < 0) angleDeg += 180;

                byte q = 0, r = 0;

                // Check neighbors based on gradient direction
                if ((angleDeg >= 0 && angleDeg < 22.5f) || (angleDeg >= 157.5f && angleDeg <= 180))
                {
                    // Horizontal edge (check east-west)
                    q = magnitude[y, x + 1];
                    r = magnitude[y, x - 1];
                }
                else if (angleDeg >= 22.5f && angleDeg < 67.5f)
                {
                    // Diagonal edge (check northeast-southwest)
                    q = magnitude[y - 1, x + 1];
                    r = magnitude[y + 1, x - 1];
                }
                else if (angleDeg >= 67.5f && angleDeg < 112.5f)
                {
                    // Vertical edge (check north-south)
                    q = magnitude[y - 1, x];
                    r = magnitude[y + 1, x];
                }
                else if (angleDeg >= 112.5f && angleDeg < 157.5f)
                {
                    // Diagonal edge (check northwest-southeast)
                    q = magnitude[y - 1, x - 1];
                    r = magnitude[y + 1, x + 1];
                }

                byte mag = magnitude[y, x];

                // Keep the pixel only if it's a local maximum
                if (mag >= q && mag >= r)
                {
                    result[y, x] = mag;
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Applies double thresholding to classify edges.
    /// </summary>
    private static byte[,] DoubleThreshold(byte[,] image, int lowThreshold, int highThreshold)
    {
        int height = image.GetLength(0);
        int width = image.GetLength(1);
        var result = new byte[height, width];

        const byte STRONG = 255;
        const byte WEAK = 75;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                byte value = image[y, x];

                if (value >= highThreshold)
                {
                    result[y, x] = STRONG;
                }
                else if (value >= lowThreshold)
                {
                    result[y, x] = WEAK;
                }
                // else result[y, x] = 0 (already initialized)
            }
        }

        return result;
    }

    /// <summary>
    /// Performs edge tracking by hysteresis to connect weak edges to strong edges.
    /// </summary>
    private static byte[,] HysteresisTracking(byte[,] thresholded)
    {
        int height = thresholded.GetLength(0);
        int width = thresholded.GetLength(1);
        var result = new byte[height, width];

        const byte STRONG = 255;
        const byte WEAK = 75;

        // First pass: copy strong edges
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (thresholded[y, x] == STRONG)
                {
                    result[y, x] = STRONG;
                }
            }
        }

        // BFS to connect weak edges to strong edges
        var queue = new Queue<(int Y, int X)>();

        // Seed the queue with strong edge pixels
        for (int y = 1; y < height - 1; y++)
        {
            for (int x = 1; x < width - 1; x++)
            {
                if (result[y, x] == STRONG)
                {
                    queue.Enqueue((y, x));
                }
            }
        }

        // Process queue
        while (queue.Count > 0)
        {
            var (cy, cx) = queue.Dequeue();

            // Check 8-connected neighbors
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dy == 0 && dx == 0) continue;

                    int ny = cy + dy;
                    int nx = cx + dx;

                    if (ny >= 0 && ny < height && nx >= 0 && nx < width)
                    {
                        // If weak edge and not yet marked as strong
                        if (thresholded[ny, nx] == WEAK && result[ny, nx] != STRONG)
                        {
                            result[ny, nx] = STRONG;
                            queue.Enqueue((ny, nx));
                        }
                    }
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Applies adaptive Canny edge detection with automatic threshold calculation.
    /// </summary>
    public static byte[,] AdaptiveCanny(byte[,] image, float sigma = 0.33f)
    {
        // Calculate median intensity
        var values = new List<byte>();
        int height = image.GetLength(0);
        int width = image.GetLength(1);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                values.Add(image[y, x]);
            }
        }

        values.Sort();
        float median = values[values.Count / 2];

        // Calculate thresholds based on median
        int lowThreshold = (int)Math.Max(0, (1.0 - sigma) * median);
        int highThreshold = (int)Math.Min(255, (1.0 + sigma) * median);

        return Canny(image, lowThreshold, highThreshold);
    }

    /// <summary>
    /// Applies adaptive Canny edge detection using Otsu's method for automatic threshold calculation.
    /// More robust for low-contrast images.
    /// </summary>
    public static byte[,] AdaptiveCannyOtsu(byte[,] image)
    {
        int height = image.GetLength(0);
        int width = image.GetLength(1);
        
        // Calculate gradients first
        var (magnitude, _) = Sobel(image, computeDirection: false);
        
        // Calculate Otsu threshold on gradient magnitudes
        int threshold = CalculateOtsuThreshold(magnitude);
        
        // Use threshold to determine Canny parameters
        // Low threshold = 0.5 * otsu, High threshold = 1.5 * otsu
        int lowThreshold = Math.Max(5, threshold / 2);
        int highThreshold = Math.Min(255, (threshold * 3) / 2);
        
        return Canny(image, lowThreshold, highThreshold);
    }
    
    /// <summary>
    /// Calculates optimal threshold using Otsu's method.
    /// </summary>
    private static int CalculateOtsuThreshold(byte[,] image)
    {
        int height = image.GetLength(0);
        int width = image.GetLength(1);
        
        // Build histogram
        int[] histogram = new int[256];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                histogram[image[y, x]]++;
            }
        }
        
        int totalPixels = width * height;
        
        // Calculate total mean
        float sum = 0;
        for (int i = 0; i < 256; i++)
        {
            sum += i * histogram[i];
        }
        
        float sumB = 0;
        int wB = 0;
        int wF = 0;
        
        float maxVariance = 0;
        int threshold = 0;
        
        for (int t = 0; t < 256; t++)
        {
            wB += histogram[t];
            if (wB == 0) continue;
            
            wF = totalPixels - wB;
            if (wF == 0) break;
            
            sumB += t * histogram[t];
            
            float mB = sumB / wB;
            float mF = (sum - sumB) / wF;
            
            float variance = wB * wF * (mB - mF) * (mB - mF);
            
            if (variance > maxVariance)
            {
                maxVariance = variance;
                threshold = t;
            }
        }
        
        return threshold;
    }

    /// <summary>
    /// Dilates the edge image to make edges thicker and more connected.
    /// </summary>
    public static byte[,] Dilate(byte[,] image, int kernelSize = 3)
    {
        int height = image.GetLength(0);
        int width = image.GetLength(1);
        var result = new byte[height, width];
        int radius = kernelSize / 2;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                byte maxVal = 0;

                for (int ky = -radius; ky <= radius; ky++)
                {
                    for (int kx = -radius; kx <= radius; kx++)
                    {
                        int ny = Math.Clamp(y + ky, 0, height - 1);
                        int nx = Math.Clamp(x + kx, 0, width - 1);
                        maxVal = Math.Max(maxVal, image[ny, nx]);
                    }
                }

                result[y, x] = maxVal;
            }
        }

        return result;
    }

    /// <summary>
    /// Applies morphological closing (dilation followed by erosion).
    /// </summary>
    public static byte[,] MorphologicalClose(byte[,] image, int kernelSize = 3)
    {
        var dilated = Dilate(image, kernelSize);
        return Erode(dilated, kernelSize);
    }

    /// <summary>
    /// Erodes the image to make edges thinner.
    /// </summary>
    public static byte[,] Erode(byte[,] image, int kernelSize = 3)
    {
        int height = image.GetLength(0);
        int width = image.GetLength(1);
        var result = new byte[height, width];
        int radius = kernelSize / 2;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                byte minVal = 255;

                for (int ky = -radius; ky <= radius; ky++)
                {
                    for (int kx = -radius; kx <= radius; kx++)
                    {
                        int ny = Math.Clamp(y + ky, 0, height - 1);
                        int nx = Math.Clamp(x + kx, 0, width - 1);
                        minVal = Math.Min(minVal, image[ny, nx]);
                    }
                }

                result[y, x] = minVal;
            }
        }

        return result;
    }
}
