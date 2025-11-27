namespace DocumentScanner.ImageProcessing;

/// <summary>
/// Implements various thresholding algorithms for image binarization.
/// </summary>
public static class Thresholder
{
    /// <summary>
    /// Applies simple binary thresholding.
    /// </summary>
    public static byte[,] BinaryThreshold(byte[,] image, int threshold)
    {
        int height = image.GetLength(0);
        int width = image.GetLength(1);
        var result = new byte[height, width];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                result[y, x] = image[y, x] > threshold ? (byte)255 : (byte)0;
            }
        }

        return result;
    }

    /// <summary>
    /// Calculates the optimal threshold using Otsu's method.
    /// </summary>
    public static int OtsuThreshold(byte[,] image)
    {
        int height = image.GetLength(0);
        int width = image.GetLength(1);
        int totalPixels = width * height;

        // Calculate histogram
        int[] histogram = new int[256];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                histogram[image[y, x]]++;
            }
        }

        // Calculate total sum of pixel values
        float sum = 0;
        for (int i = 0; i < 256; i++)
        {
            sum += i * histogram[i];
        }

        float sumB = 0;
        int wB = 0;
        int wF;

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
    /// Applies adaptive thresholding using the mean of local neighborhood.
    /// </summary>
    public static byte[,] AdaptiveThresholdMean(byte[,] image, int blockSize, int c = 5)
    {
        if (blockSize % 2 == 0)
            blockSize++;

        int height = image.GetLength(0);
        int width = image.GetLength(1);
        var result = new byte[height, width];
        int radius = blockSize / 2;

        // Use integral image for efficient mean calculation
        var integral = ComputeIntegralImage(image);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int x1 = Math.Max(0, x - radius);
                int y1 = Math.Max(0, y - radius);
                int x2 = Math.Min(width - 1, x + radius);
                int y2 = Math.Min(height - 1, y + radius);

                int count = (x2 - x1 + 1) * (y2 - y1 + 1);
                long sum = GetIntegralSum(integral, x1, y1, x2, y2);
                float mean = (float)sum / count;

                result[y, x] = image[y, x] > mean - c ? (byte)255 : (byte)0;
            }
        }

        return result;
    }

    /// <summary>
    /// Applies adaptive thresholding using Gaussian-weighted local mean.
    /// </summary>
    public static byte[,] AdaptiveThresholdGaussian(byte[,] image, int blockSize, int c = 5)
    {
        if (blockSize % 2 == 0)
            blockSize++;

        int height = image.GetLength(0);
        int width = image.GetLength(1);
        var result = new byte[height, width];

        // Apply Gaussian blur to get local weighted mean
        var blurred = ImageProcessor.GaussianBlur(image, blockSize);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                result[y, x] = image[y, x] > blurred[y, x] - c ? (byte)255 : (byte)0;
            }
        }

        return result;
    }

    /// <summary>
    /// Applies Sauvola's adaptive thresholding - particularly good for documents.
    /// </summary>
    public static byte[,] SauvolaThreshold(byte[,] image, int windowSize = 15, float k = 0.5f, float r = 128f)
    {
        if (windowSize % 2 == 0)
            windowSize++;

        int height = image.GetLength(0);
        int width = image.GetLength(1);
        var result = new byte[height, width];
        int radius = windowSize / 2;

        // Compute integral images for mean and variance calculation
        var integral = ComputeIntegralImage(image);
        var integralSq = ComputeIntegralImageSquared(image);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int x1 = Math.Max(0, x - radius);
                int y1 = Math.Max(0, y - radius);
                int x2 = Math.Min(width - 1, x + radius);
                int y2 = Math.Min(height - 1, y + radius);

                int count = (x2 - x1 + 1) * (y2 - y1 + 1);

                long sum = GetIntegralSum(integral, x1, y1, x2, y2);
                long sumSq = GetIntegralSum(integralSq, x1, y1, x2, y2);

                float mean = (float)sum / count;
                float variance = (float)sumSq / count - mean * mean;
                float stdDev = MathF.Sqrt(Math.Max(0, variance));

                // Sauvola's formula: T = mean * (1 + k * (stdDev / r - 1))
                float threshold = mean * (1 + k * (stdDev / r - 1));

                result[y, x] = image[y, x] > threshold ? (byte)255 : (byte)0;
            }
        }

        return result;
    }

    /// <summary>
    /// Applies Niblack's adaptive thresholding.
    /// </summary>
    public static byte[,] NiblackThreshold(byte[,] image, int windowSize = 15, float k = -0.2f)
    {
        if (windowSize % 2 == 0)
            windowSize++;

        int height = image.GetLength(0);
        int width = image.GetLength(1);
        var result = new byte[height, width];
        int radius = windowSize / 2;

        var integral = ComputeIntegralImage(image);
        var integralSq = ComputeIntegralImageSquared(image);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int x1 = Math.Max(0, x - radius);
                int y1 = Math.Max(0, y - radius);
                int x2 = Math.Min(width - 1, x + radius);
                int y2 = Math.Min(height - 1, y + radius);

                int count = (x2 - x1 + 1) * (y2 - y1 + 1);

                long sum = GetIntegralSum(integral, x1, y1, x2, y2);
                long sumSq = GetIntegralSum(integralSq, x1, y1, x2, y2);

                float mean = (float)sum / count;
                float variance = (float)sumSq / count - mean * mean;
                float stdDev = MathF.Sqrt(Math.Max(0, variance));

                // Niblack's formula: T = mean + k * stdDev
                float threshold = mean + k * stdDev;

                result[y, x] = image[y, x] > threshold ? (byte)255 : (byte)0;
            }
        }

        return result;
    }

    /// <summary>
    /// Computes the integral image (summed area table).
    /// </summary>
    private static long[,] ComputeIntegralImage(byte[,] image)
    {
        int height = image.GetLength(0);
        int width = image.GetLength(1);
        var integral = new long[height + 1, width + 1];

        for (int y = 1; y <= height; y++)
        {
            for (int x = 1; x <= width; x++)
            {
                integral[y, x] = image[y - 1, x - 1]
                    + integral[y - 1, x]
                    + integral[y, x - 1]
                    - integral[y - 1, x - 1];
            }
        }

        return integral;
    }

    /// <summary>
    /// Computes the integral image of squared values.
    /// </summary>
    private static long[,] ComputeIntegralImageSquared(byte[,] image)
    {
        int height = image.GetLength(0);
        int width = image.GetLength(1);
        var integral = new long[height + 1, width + 1];

        for (int y = 1; y <= height; y++)
        {
            for (int x = 1; x <= width; x++)
            {
                long val = image[y - 1, x - 1];
                integral[y, x] = val * val
                    + integral[y - 1, x]
                    + integral[y, x - 1]
                    - integral[y - 1, x - 1];
            }
        }

        return integral;
    }

    /// <summary>
    /// Gets the sum of a rectangular region from an integral image.
    /// </summary>
    private static long GetIntegralSum(long[,] integral, int x1, int y1, int x2, int y2)
    {
        return integral[y2 + 1, x2 + 1]
            - integral[y1, x2 + 1]
            - integral[y2 + 1, x1]
            + integral[y1, x1];
    }
}
