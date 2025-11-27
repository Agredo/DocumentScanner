using DocumentScanner.Core;
using SkiaSharp;

namespace DocumentScanner.ImageProcessing;

/// <summary>
/// Implements perspective transformation (homography) operations.
/// </summary>
public static class PerspectiveTransform
{
    /// <summary>
    /// Represents a 3x3 transformation matrix.
    /// </summary>
    public readonly struct Matrix3x3
    {
        private readonly float[,] _data;

        public float this[int row, int col] => _data[row, col];

        public Matrix3x3(float[,] data)
        {
            if (data.GetLength(0) != 3 || data.GetLength(1) != 3)
                throw new ArgumentException("Matrix must be 3x3");
            _data = (float[,])data.Clone();
        }

        public static Matrix3x3 Identity => new(new float[,] {
            { 1, 0, 0 },
            { 0, 1, 0 },
            { 0, 0, 1 }
        });

        /// <summary>
        /// Multiplies two 3x3 matrices.
        /// </summary>
        public static Matrix3x3 operator *(Matrix3x3 a, Matrix3x3 b)
        {
            var result = new float[3, 3];

            for (int i = 0; i < 3; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    result[i, j] = 0;
                    for (int k = 0; k < 3; k++)
                    {
                        result[i, j] += a[i, k] * b[k, j];
                    }
                }
            }

            return new Matrix3x3(result);
        }

        /// <summary>
        /// Transforms a point using this matrix.
        /// </summary>
        public PointF Transform(PointF point)
        {
            float x = point.X;
            float y = point.Y;

            float w = _data[2, 0] * x + _data[2, 1] * y + _data[2, 2];

            if (Math.Abs(w) < 0.0001f)
                return point;

            float newX = (_data[0, 0] * x + _data[0, 1] * y + _data[0, 2]) / w;
            float newY = (_data[1, 0] * x + _data[1, 1] * y + _data[1, 2]) / w;

            return new PointF(newX, newY);
        }

        /// <summary>
        /// Computes the inverse of this matrix.
        /// </summary>
        public Matrix3x3 Inverse()
        {
            var inv = new float[3, 3];

            // Calculate cofactors
            inv[0, 0] = _data[1, 1] * _data[2, 2] - _data[1, 2] * _data[2, 1];
            inv[0, 1] = _data[0, 2] * _data[2, 1] - _data[0, 1] * _data[2, 2];
            inv[0, 2] = _data[0, 1] * _data[1, 2] - _data[0, 2] * _data[1, 1];
            inv[1, 0] = _data[1, 2] * _data[2, 0] - _data[1, 0] * _data[2, 2];
            inv[1, 1] = _data[0, 0] * _data[2, 2] - _data[0, 2] * _data[2, 0];
            inv[1, 2] = _data[0, 2] * _data[1, 0] - _data[0, 0] * _data[1, 2];
            inv[2, 0] = _data[1, 0] * _data[2, 1] - _data[1, 1] * _data[2, 0];
            inv[2, 1] = _data[0, 1] * _data[2, 0] - _data[0, 0] * _data[2, 1];
            inv[2, 2] = _data[0, 0] * _data[1, 1] - _data[0, 1] * _data[1, 0];

            // Calculate determinant
            float det = _data[0, 0] * inv[0, 0] + _data[0, 1] * inv[1, 0] + _data[0, 2] * inv[2, 0];

            if (Math.Abs(det) < 0.0001f)
                throw new InvalidOperationException("Matrix is not invertible");

            // Divide by determinant
            for (int i = 0; i < 3; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    inv[i, j] /= det;
                }
            }

            return new Matrix3x3(inv);
        }

        /// <summary>
        /// Converts to SKMatrix for use with SkiaSharp.
        /// </summary>
        public SKMatrix ToSKMatrix()
        {
            return new SKMatrix(
                _data[0, 0], _data[0, 1], _data[0, 2],
                _data[1, 0], _data[1, 1], _data[1, 2],
                _data[2, 0], _data[2, 1], _data[2, 2]
            );
        }
    }

    /// <summary>
    /// Computes the perspective transformation matrix from 4 source points to 4 destination points.
    /// Uses the DLT (Direct Linear Transform) algorithm.
    /// </summary>
    public static Matrix3x3 GetPerspectiveTransform(PointF[] srcPoints, PointF[] dstPoints)
    {
        if (srcPoints.Length != 4 || dstPoints.Length != 4)
            throw new ArgumentException("Exactly 4 points are required");

        // Build the coefficient matrix A for the linear system
        // We solve: A * h = 0, where h contains the 8 unknowns of the homography
        var A = new float[8, 9];

        for (int i = 0; i < 4; i++)
        {
            float x = srcPoints[i].X;
            float y = srcPoints[i].Y;
            float u = dstPoints[i].X;
            float v = dstPoints[i].Y;

            A[i * 2, 0] = x;
            A[i * 2, 1] = y;
            A[i * 2, 2] = 1;
            A[i * 2, 3] = 0;
            A[i * 2, 4] = 0;
            A[i * 2, 5] = 0;
            A[i * 2, 6] = -u * x;
            A[i * 2, 7] = -u * y;
            A[i * 2, 8] = -u;

            A[i * 2 + 1, 0] = 0;
            A[i * 2 + 1, 1] = 0;
            A[i * 2 + 1, 2] = 0;
            A[i * 2 + 1, 3] = x;
            A[i * 2 + 1, 4] = y;
            A[i * 2 + 1, 5] = 1;
            A[i * 2 + 1, 6] = -v * x;
            A[i * 2 + 1, 7] = -v * y;
            A[i * 2 + 1, 8] = -v;
        }

        // Solve using simplified approach - we can set h33 = 1 and solve the 8x8 system
        var h = SolveHomography(srcPoints, dstPoints);

        return new Matrix3x3(new float[,] {
            { h[0], h[1], h[2] },
            { h[3], h[4], h[5] },
            { h[6], h[7], h[8] }
        });
    }

    /// <summary>
    /// Solves for the homography matrix parameters.
    /// </summary>
    private static float[] SolveHomography(PointF[] src, PointF[] dst)
    {
        // Build the 8x8 system by setting h33 = 1
        var A = new float[8, 8];
        var b = new float[8];

        for (int i = 0; i < 4; i++)
        {
            float x = src[i].X;
            float y = src[i].Y;
            float u = dst[i].X;
            float v = dst[i].Y;

            A[i * 2, 0] = x;
            A[i * 2, 1] = y;
            A[i * 2, 2] = 1;
            A[i * 2, 3] = 0;
            A[i * 2, 4] = 0;
            A[i * 2, 5] = 0;
            A[i * 2, 6] = -u * x;
            A[i * 2, 7] = -u * y;
            b[i * 2] = u;

            A[i * 2 + 1, 0] = 0;
            A[i * 2 + 1, 1] = 0;
            A[i * 2 + 1, 2] = 0;
            A[i * 2 + 1, 3] = x;
            A[i * 2 + 1, 4] = y;
            A[i * 2 + 1, 5] = 1;
            A[i * 2 + 1, 6] = -v * x;
            A[i * 2 + 1, 7] = -v * y;
            b[i * 2 + 1] = v;
        }

        // Solve using Gaussian elimination
        var gaussianResult = GaussianElimination(A, b);

        return new float[] { gaussianResult[0], gaussianResult[1], gaussianResult[2], gaussianResult[3], gaussianResult[4], gaussianResult[5], gaussianResult[6], gaussianResult[7], 1 };
    }

    /// <summary>
    /// Solves a linear system using Gaussian elimination with partial pivoting.
    /// </summary>
    private static float[] GaussianElimination(float[,] A, float[] b)
    {
        int n = b.Length;
        var augmented = new float[n, n + 1];

        // Create augmented matrix
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                augmented[i, j] = A[i, j];
            }
            augmented[i, n] = b[i];
        }

        // Forward elimination with partial pivoting
        for (int col = 0; col < n; col++)
        {
            // Find pivot
            int maxRow = col;
            float maxVal = Math.Abs(augmented[col, col]);

            for (int row = col + 1; row < n; row++)
            {
                if (Math.Abs(augmented[row, col]) > maxVal)
                {
                    maxVal = Math.Abs(augmented[row, col]);
                    maxRow = row;
                }
            }

            // Swap rows
            if (maxRow != col)
            {
                for (int j = 0; j <= n; j++)
                {
                    (augmented[col, j], augmented[maxRow, j]) = (augmented[maxRow, j], augmented[col, j]);
                }
            }

            // Eliminate
            for (int row = col + 1; row < n; row++)
            {
                if (Math.Abs(augmented[col, col]) < 0.0001f)
                    continue;

                float factor = augmented[row, col] / augmented[col, col];

                for (int j = col; j <= n; j++)
                {
                    augmented[row, j] -= factor * augmented[col, j];
                }
            }
        }

        // Back substitution
        var x = new float[n];

        for (int i = n - 1; i >= 0; i--)
        {
            x[i] = augmented[i, n];

            for (int j = i + 1; j < n; j++)
            {
                x[i] -= augmented[i, j] * x[j];
            }

            if (Math.Abs(augmented[i, i]) > 0.0001f)
            {
                x[i] /= augmented[i, i];
            }
        }

        return x;
    }

    /// <summary>
    /// Applies a perspective transformation to an image.
    /// </summary>
    public static SKBitmap WarpPerspective(SKBitmap source, Matrix3x3 transform, int outputWidth, int outputHeight)
    {
        var output = new SKBitmap(outputWidth, outputHeight, source.ColorType, source.AlphaType);

        // Get inverse transform to map from destination to source
        var inverseTransform = transform.Inverse();

        var srcPixels = source.GetPixels();
        var dstPixels = output.GetPixels();
        int bytesPerPixel = source.BytesPerPixel;

        unsafe
        {
            byte* srcPtr = (byte*)srcPixels.ToPointer();
            byte* dstPtr = (byte*)dstPixels.ToPointer();

            for (int y = 0; y < outputHeight; y++)
            {
                for (int x = 0; x < outputWidth; x++)
                {
                    // Map destination point to source
                    var srcPoint = inverseTransform.Transform(new PointF(x, y));

                    int dstOffset = (y * outputWidth + x) * bytesPerPixel;

                    // Bilinear interpolation
                    if (srcPoint.X >= 0 && srcPoint.X < source.Width - 1 &&
                        srcPoint.Y >= 0 && srcPoint.Y < source.Height - 1)
                    {
                        int x0 = (int)srcPoint.X;
                        int y0 = (int)srcPoint.Y;
                        int x1 = x0 + 1;
                        int y1 = y0 + 1;

                        float fx = srcPoint.X - x0;
                        float fy = srcPoint.Y - y0;

                        for (int c = 0; c < bytesPerPixel; c++)
                        {
                            int offset00 = (y0 * source.Width + x0) * bytesPerPixel + c;
                            int offset10 = (y0 * source.Width + x1) * bytesPerPixel + c;
                            int offset01 = (y1 * source.Width + x0) * bytesPerPixel + c;
                            int offset11 = (y1 * source.Width + x1) * bytesPerPixel + c;

                            float top = srcPtr[offset00] * (1 - fx) + srcPtr[offset10] * fx;
                            float bottom = srcPtr[offset01] * (1 - fx) + srcPtr[offset11] * fx;
                            float value = top * (1 - fy) + bottom * fy;

                            dstPtr[dstOffset + c] = (byte)Math.Clamp(value, 0, 255);
                        }
                    }
                    else if (srcPoint.X >= 0 && srcPoint.X < source.Width &&
                             srcPoint.Y >= 0 && srcPoint.Y < source.Height)
                    {
                        // Nearest neighbor for edge pixels
                        int srcX = (int)Math.Round(srcPoint.X);
                        int srcY = (int)Math.Round(srcPoint.Y);
                        srcX = Math.Clamp(srcX, 0, source.Width - 1);
                        srcY = Math.Clamp(srcY, 0, source.Height - 1);

                        int srcOffset = (srcY * source.Width + srcX) * bytesPerPixel;

                        for (int c = 0; c < bytesPerPixel; c++)
                        {
                            dstPtr[dstOffset + c] = srcPtr[srcOffset + c];
                        }
                    }
                    else
                    {
                        // Fill with white for out-of-bounds
                        for (int c = 0; c < Math.Min(3, bytesPerPixel); c++)
                        {
                            dstPtr[dstOffset + c] = 255;
                        }
                        if (bytesPerPixel == 4)
                        {
                            dstPtr[dstOffset + 3] = 255; // Alpha
                        }
                    }
                }
            }
        }

        return output;
    }

    /// <summary>
    /// Creates a perspective transform that maps the source quadrilateral to a rectangle.
    /// </summary>
    public static (Matrix3x3 Transform, int Width, int Height) CreateDocumentTransform(
        Quadrilateral sourceCorners, 
        float? targetAspectRatio = null)
    {
        // Calculate the dimensions of the output rectangle
        float width = sourceCorners.Width;
        float height = sourceCorners.Height;

        // Optionally adjust to a specific aspect ratio (e.g., A4 paper = 1:1.414)
        if (targetAspectRatio.HasValue)
        {
            float currentRatio = width / height;

            if (currentRatio > targetAspectRatio.Value)
            {
                // Image is wider than target ratio - adjust height
                height = width / targetAspectRatio.Value;
            }
            else
            {
                // Image is taller than target ratio - adjust width
                width = height * targetAspectRatio.Value;
            }
        }

        int outWidth = (int)Math.Round(width);
        int outHeight = (int)Math.Round(height);

        // Ensure minimum size
        outWidth = Math.Max(outWidth, 100);
        outHeight = Math.Max(outHeight, 100);

        // Define destination rectangle corners
        var dstCorners = new PointF[]
        {
            new(0, 0),              // Top-left
            new(outWidth, 0),       // Top-right
            new(outWidth, outHeight), // Bottom-right
            new(0, outHeight)       // Bottom-left
        };

        var srcCorners = sourceCorners.ToArray();

        var transform = GetPerspectiveTransform(srcCorners, dstCorners);

        return (transform, outWidth, outHeight);
    }
}
