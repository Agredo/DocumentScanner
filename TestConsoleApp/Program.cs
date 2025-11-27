using DocumentScanner;
using DocumentScanner.Core;
using DocumentScanner.ImageProcessing;
using DocumentScanner.Visualization;
using SkiaSharp;

Console.WriteLine("=== DocumentScanner Debug Tool ===\n");

string imagePath = @"C:\Users\chris\OneDrive\Bilder\Camera Roll\IMG_20250108_024734.jpg";

if (!File.Exists(imagePath))
{
    Console.WriteLine($"ERROR: Image file not found at: {imagePath}");
    return;
}

Console.WriteLine($"Loading image: {imagePath}\n");

try
{
    // Load image
    byte[] imageBytes = File.ReadAllBytes(imagePath);
    using var bitmap = ImageProcessor.LoadImage(imageBytes);
    
    Console.WriteLine($"Image loaded: {bitmap.Width}x{bitmap.Height} pixels\n");
    
    // Create scanner
    var scanner = new Scanner();
    
    // Test with different detection options
    var testConfigs = new[]
    {
        new { Name = "Default", Options = new DetectionOptions() },
        new { Name = "Lower Canny Thresholds", Options = new DetectionOptions { CannyLowThreshold = 20, CannyHighThreshold = 60 } },
        new { Name = "Very Low Thresholds", Options = new DetectionOptions { CannyLowThreshold = 10, CannyHighThreshold = 30 } },
        new { Name = "No Adaptive + Low Threshold", Options = new DetectionOptions { UseAdaptiveThreshold = false, CannyLowThreshold = 20, CannyHighThreshold = 60 } },
        new { Name = "Larger Scale", Options = new DetectionOptions { ProcessingScale = 0.75f, CannyLowThreshold = 20, CannyHighThreshold = 60 } },
    };

    foreach (var config in testConfigs)
    {
        Console.WriteLine($"=== Testing: {config.Name} ===");
        TestDetection(scanner, imageBytes, config.Options);
        Console.WriteLine();
    }
    
    // Detailed edge and contour analysis with default settings
    Console.WriteLine("\n=== DETAILED ANALYSIS (Default Settings) ===");
    DetailedAnalysis(bitmap, new DetectionOptions());
}
catch (Exception ex)
{
    Console.WriteLine($"ERROR: {ex.Message}");
    Console.WriteLine(ex.StackTrace);
}

Console.WriteLine("\nPress any key to exit...");
Console.ReadKey();

static void TestDetection(Scanner scanner, byte[] imageBytes, DetectionOptions options)
{
    var result = scanner.Detect(imageBytes, options);
    
    Console.WriteLine($"  Success: {result.Success}");
    if (result.Success)
    {
        Console.WriteLine($"  Confidence: {result.Confidence:F2}");
        Console.WriteLine($"  Corners: TL={result.Corners.TopLeft}, TR={result.Corners.TopRight}");
        Console.WriteLine($"           BL={result.Corners.BottomLeft}, BR={result.Corners.BottomRight}");
        Console.WriteLine($"  Rotation: {result.AngleInfo.RotationAngle:F1}°");
        
        // Create visualization using the library's visualization API
        var visualizationBytes = scanner.CreateVisualization(imageBytes, result);
        string filename = $"detection_{options.CannyLowThreshold}_{options.CannyHighThreshold}.jpg";
        File.WriteAllBytes(filename, visualizationBytes);
        Console.WriteLine($"  Saved: {filename}");
    }
    else
    {
        Console.WriteLine($"  Error: {result.ErrorMessage}");
    }
}

static void DetailedAnalysis(SKBitmap bitmap, DetectionOptions options)
{
    int originalWidth = bitmap.Width;
    int originalHeight = bitmap.Height;

    // Step 1: Scale down
    int processWidth = (int)(originalWidth * options.ProcessingScale);
    int processHeight = (int)(originalHeight * options.ProcessingScale);
    
    Console.WriteLine($"Processing size: {processWidth}x{processHeight} (scale: {options.ProcessingScale})");
    
    using var scaledBitmap = ImageProcessor.Resize(bitmap, processWidth, processHeight);

    // Step 2: Convert to grayscale
    var grayscale = ImageProcessor.ToGrayscale(scaledBitmap);
    Console.WriteLine($"Grayscale conversion: OK");
    
    // Save grayscale for inspection
    SaveDebugImage(grayscale, "01_grayscale.png");
    
    // Calculate grayscale statistics
    int totalPixels = processWidth * processHeight;
    long sum = 0;
    int minVal = 255, maxVal = 0;
    
    for (int y = 0; y < processHeight; y++)
    {
        for (int x = 0; x < processWidth; x++)
        {
            byte val = grayscale[y, x];
            sum += val;
            if (val < minVal) minVal = val;
            if (val > maxVal) maxVal = val;
        }
    }
    
    double avgBrightness = (double)sum / totalPixels;
    Console.WriteLine($"Brightness: min={minVal}, max={maxVal}, avg={avgBrightness:F1}, contrast={maxVal - minVal}");

    // Step 3: Enhance contrast
    if (options.EnhanceContrast)
    {
        if (options.UseSimpleHistogramEqualization)
        {
            grayscale = ImageProcessor.EnhanceContrast(grayscale);
            Console.WriteLine("Contrast enhancement: APPLIED (Simple Histogram Equalization)");
        }
        else
        {
            grayscale = ImageProcessor.ApplyCLAHE(grayscale, 8, 2.0f);
            Console.WriteLine("Contrast enhancement: APPLIED (CLAHE)");
        }
        SaveDebugImage(grayscale, "02_enhanced.png");
    }
    else
    {
        Console.WriteLine("Contrast enhancement: SKIPPED");
    }

    // Step 4: Apply Gaussian blur to reduce noise
    var blurred = ImageProcessor.GaussianBlur(grayscale, options.BlurKernelSize);
    Console.WriteLine($"Gaussian blur: kernel size {options.BlurKernelSize}");
    SaveDebugImage(blurred, "03_blurred.png");
    
    // DEBUG: Also test WITHOUT blur to see if blur is the problem
    Console.WriteLine("\nDEBUG: Testing edge detection WITHOUT blur...");
    var (sobelNoBurMag, _) = EdgeDetector.Sobel(grayscale, computeDirection: false);
    int nonZeroNoBlur = 0;
    int maxGradNoBlur = 0;
    for (int y = 0; y < processHeight; y++)
    {
        for (int x = 0; x < processWidth; x++)
        {
            byte mag = sobelNoBurMag[y, x];
            if (mag > 0) nonZeroNoBlur++;
            if (mag > maxGradNoBlur) maxGradNoBlur = mag;
        }
    }
    Console.WriteLine($"  Sobel (NO blur): max={maxGradNoBlur}, non-zero pixels={100.0 * nonZeroNoBlur / totalPixels:F1}%");
    SaveDebugImage(sobelNoBurMag, "03b_sobel_no_blur.png");
    
    // Step 5: Edge detection
    byte[,] edges;
    if (options.UseAdaptiveThreshold)
    {
        var thresholded = Thresholder.SauvolaThreshold(blurred, options.AdaptiveBlockSize);
        Console.WriteLine($"Sauvola threshold: block size {options.AdaptiveBlockSize}");
        SaveDebugImage(thresholded, "04_thresholded.png");
        
        // Count thresholded pixels
        int whitePixels = 0;
        for (int y = 0; y < processHeight; y++)
        {
            for (int x = 0; x < processWidth; x++)
            {
                if (thresholded[y, x] > 128) whitePixels++;
            }
        }
        Console.WriteLine($"  After threshold: {whitePixels} white pixels ({100.0 * whitePixels / totalPixels:F1}%)");
        
        edges = EdgeDetector.Canny(thresholded, options.CannyLowThreshold, options.CannyHighThreshold);
    }
    else
    {
        // DEBUG: Check Sobel gradients before Canny
        Console.WriteLine("\nDEBUG: Analyzing Sobel gradients...");
        var (sobelMag, sobelDir) = EdgeDetector.Sobel(blurred, computeDirection: true);
        
        // Count non-zero gradients
        int nonZeroGradients = 0;
        int minGrad = 255, maxGrad = 0;
        long sumGrad = 0;
        for (int y = 0; y < processHeight; y++)
        {
            for (int x = 0; x < processWidth; x++)
            {
                byte mag = sobelMag[y, x];
                if (mag > 0) nonZeroGradients++;
                if (mag < minGrad) minGrad = mag;
                if (mag > maxGrad) maxGrad = mag;
                sumGrad += mag;
            }
        }
        
        double avgGrad = (double)sumGrad / totalPixels;
        Console.WriteLine($"  Sobel gradients: min={minGrad}, max={maxGrad}, avg={avgGrad:F1}");
        Console.WriteLine($"  Non-zero gradient pixels: {nonZeroGradients} ({100.0 * nonZeroGradients / totalPixels:F1}%)");
        SaveDebugImage(sobelMag, "04b_sobel_magnitude.png");
        
        // Check if gradients are too weak
        if (maxGrad < 30)
        {
            Console.WriteLine($"  ⚠️ WARNING: Maximum gradient ({maxGrad}) is very low!");
            Console.WriteLine($"  -> Image may be too uniform or blur is too strong.");
        }
        
        // DEBUG: Use CannyDebug to see intermediate steps
        var (cannyEdges, nms, thresholded) = EdgeDetector.CannyDebug(blurred, options.CannyLowThreshold, options.CannyHighThreshold);
        edges = cannyEdges;
        
        // Count NMS pixels
        int nmsPixels = 0;
        for (int y = 0; y < processHeight; y++)
        {
            for (int x = 0; x < processWidth; x++)
            {
                if (nms[y, x] > 0) nmsPixels++;
            }
        }
        Console.WriteLine($"  After Non-Maximum Suppression: {nmsPixels} pixels ({100.0 * nmsPixels / totalPixels:F2}%)");
        SaveDebugImage(nms, "04c_nms.png");
        
        // Count thresholded pixels
        int strongPixels = 0;
        int weakPixels = 0;
        for (int y = 0; y < processHeight; y++)
        {
            for (int x = 0; x < processWidth; x++)
            {
                if (thresholded[y, x] == 255) strongPixels++;
                else if (thresholded[y, x] == 75) weakPixels++;
            }
        }
        Console.WriteLine($"  After Double Threshold: {strongPixels} strong + {weakPixels} weak = {strongPixels + weakPixels} total");
        SaveDebugImage(thresholded, "04d_thresholded.png");
    }
    
    Console.WriteLine($"Canny edge detection: low={options.CannyLowThreshold}, high={options.CannyHighThreshold}");

    // Count edge pixels
    int edgePixelCount = 0;
    for (int y = 0; y < processHeight; y++)
    {
        for (int x = 0; x < processWidth; x++)
        {
            if (edges[y, x] > 0) edgePixelCount++;
        }
    }
    
    Console.WriteLine($"  Edge pixels found: {edgePixelCount} ({100.0 * edgePixelCount / totalPixels:F2}%)");
    SaveDebugImage(edges, "05_edges.png");

    // Step 6: Morphological closing
    edges = EdgeDetector.MorphologicalClose(edges, 3);
    
    int edgePixelCountAfterClose = 0;
    for (int y = 0; y < processHeight; y++)
    {
        for (int x = 0; x < processWidth; x++)
        {
            if (edges[y, x] > 0) edgePixelCountAfterClose++;
        }
    }
    
    Console.WriteLine($"  After morphological close: {edgePixelCountAfterClose} pixels ({100.0 * edgePixelCountAfterClose / totalPixels:F2}%)");
    SaveDebugImage(edges, "06_edges_closed.png");

    // Step 7: Find contours
    Console.WriteLine("\nSearching for contours...");
    var contours = ContourDetector.FindContoursSimple(edges);
    
    Console.WriteLine($"Contours found: {contours.Count}");
    
    if (contours.Count > 0)
    {
        // Draw contours on image
        var contoursViz = CreateContoursVisualization(edges, contours);
        SaveDebugImage(contoursViz, "07_contours.png");
        
        Console.WriteLine("\nTop 10 contours by area:");
        var sortedContours = contours.OrderByDescending(c => c.Area).Take(10).ToList();
        
        for (int i = 0; i < sortedContours.Count; i++)
        {
            var c = sortedContours[i];
            var bounds = c.BoundingRect;
            float areaRatio = c.Area / (processWidth * processHeight);
            
            Console.WriteLine($"  #{i + 1}: {c.Points.Count} points, area={c.Area:F0} ({areaRatio:P1}), " +
                            $"perimeter={c.Perimeter:F0}, bounds=[{bounds.X:F0},{bounds.Y:F0} {bounds.Width:F0}x{bounds.Height:F0}]");
            
            // Try to approximate to polygon - use bounding box perimeter (BFS gives unordered points)
            float boundingPerimeter = 2 * (bounds.Width + bounds.Height);
            float epsilon = 0.02f * boundingPerimeter;
            var approx = ContourDetector.ApproximatePolygon(c, epsilon);
            Console.WriteLine($"       Approximation: {approx.Points.Count} vertices (epsilon={epsilon:F1})");
            
            if (approx.Points.Count >= 4 && approx.Points.Count <= 8)
            {
                Console.WriteLine($"       -> Potential document candidate!");
            }
        }
    }
    else
    {
        Console.WriteLine("\n*** NO CONTOURS FOUND ***");
        Console.WriteLine("Possible causes:");
        Console.WriteLine("  1. Edge detection thresholds too high");
        Console.WriteLine("  2. Not enough contrast in the image");
        Console.WriteLine("  3. Edges broken/disconnected");
        Console.WriteLine("  4. Bug in contour tracing algorithm");
        
        if (edgePixelCount < totalPixels * 0.001)
        {
            Console.WriteLine("\n  -> DIAGNOSIS: Very few edge pixels detected!");
            Console.WriteLine("     Try lower Canny thresholds or disable adaptive thresholding.");
        }
        else if (edgePixelCount > totalPixels * 0.3)
        {
            Console.WriteLine("\n  -> DIAGNOSIS: Too many edge pixels (noisy image)!");
            Console.WriteLine("     Try higher Canny thresholds or more blur.");
        }
        else
        {
            Console.WriteLine("\n  -> Edge pixel count looks reasonable.");
            Console.WriteLine("     Problem might be in contour tracing or edge connectivity.");
        }
    }
    
    Console.WriteLine($"\nDebug images saved to: {Directory.GetCurrentDirectory()}");
}

static void SaveDebugImage(byte[,] image, string filename)
{
    int height = image.GetLength(0);
    int width = image.GetLength(1);
    
    using var bitmap = new SKBitmap(width, height, SKColorType.Gray8, SKAlphaType.Opaque);
    
    unsafe
    {
        byte* ptr = (byte*)bitmap.GetPixels().ToPointer();
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                ptr[y * width + x] = image[y, x];
            }
        }
    }
    
    using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
    using var stream = File.OpenWrite(filename);
    data.SaveTo(stream);
    
    Console.WriteLine($"  Saved: {filename}");
}

static byte[,] CreateContoursVisualization(byte[,] edges, List<ContourDetector.Contour> contours)
{
    int height = edges.GetLength(0);
    int width = edges.GetLength(1);
    
    var result = new byte[height, width];
    
    // Copy edges as background
    for (int y = 0; y < height; y++)
    {
        for (int x = 0; x < width; x++)
        {
            result[y, x] = (byte)(edges[y, x] / 2); // Dim the edges
        }
    }
    
    // Draw contours in bright white
    foreach (var contour in contours)
    {
        foreach (var point in contour.Points)
        {
            int x = (int)point.X;
            int y = (int)point.Y;
            if (x >= 0 && x < width && y >= 0 && y < height)
            {
                result[y, x] = 255;
            }
        }
    }
    
    return result;
}
