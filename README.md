# DocumentScanner

A cross-platform document detection and perspective correction library for .NET MAUI using SkiaSharp. This library provides OpenCV-like functionality without requiring OpenCV or EmguCV dependencies.

## Features

- **Document Detection**: Automatically detect documents (letters, invoices, receipts, etc.) in photos
- **Corner Detection**: Get precise corner points of the detected document
- **Perspective Correction**: Transform skewed documents to a top-down view
- **Angle Information**: Get rotation angle and skew information
- **Mask Generation**: Get a binary mask of the document region
- **Image Enhancement**: Optional contrast enhancement, sharpening, and binarization
- **Cross-Platform**: Works on iOS, Android, macOS, and Windows via .NET MAUI

## Installation

### NuGet Package (when published)
```bash
dotnet add package DocumentScanner
```

### Manual Installation
1. Clone or download this repository
2. Add a reference to the `DocumentScanner` project in your solution

## Dependencies

- **SkiaSharp** (2.88.7) - Cross-platform 2D graphics library
- **.NET 8.0** or later

## Quick Start

### Basic Usage

```csharp
using DocumentScanner;
using DocumentScanner.Core;

// Create a scanner instance
var scanner = new Scanner();

// Load your image as byte array
byte[] imageBytes = File.ReadAllBytes("photo.jpg");

// Scan the document (detect + correct perspective)
var result = scanner.Scan(imageBytes);

if (result.Success)
{
    // Save the corrected document
    File.WriteAllBytes("scanned.png", result.CorrectedImage!);
    
    // Access detection information
    var corners = result.DetectionResult!.Corners;
    var angle = result.DetectionResult.AngleInfo;
    
    Console.WriteLine($"Document angle: {angle!.RotationAngle}°");
    Console.WriteLine($"Confidence: {result.DetectionResult.Confidence:P0}");
}
```

### Detection Only (Preview Mode)

```csharp
var scanner = new Scanner();
byte[] imageBytes = File.ReadAllBytes("photo.jpg");

// Only detect, don't correct yet
var detection = scanner.Detect(imageBytes);

if (detection.Success)
{
    // Get corner points for UI overlay
    var corners = detection.Corners!.ToArray();
    
    Console.WriteLine($"Top-Left: {corners[0]}");
    Console.WriteLine($"Top-Right: {corners[1]}");
    Console.WriteLine($"Bottom-Right: {corners[2]}");
    Console.WriteLine($"Bottom-Left: {corners[3]}");
    
    // Create visualization with detected corners
    byte[] preview = scanner.CreateDetectionVisualization(imageBytes, detection);
    File.WriteAllBytes("preview.png", preview);
}
```

### Manual Corner Correction

```csharp
var scanner = new Scanner();
byte[] imageBytes = File.ReadAllBytes("photo.jpg");

// User-adjusted corners (e.g., from UI)
var corners = new PointF[]
{
    new PointF(100, 50),   // Top-Left
    new PointF(900, 80),   // Top-Right
    new PointF(920, 1200), // Bottom-Right
    new PointF(80, 1180)   // Bottom-Left
};

// Correct perspective with manual corners
byte[] corrected = scanner.CorrectPerspective(imageBytes, corners);
File.WriteAllBytes("corrected.png", corrected);
```

### Custom Options

```csharp
// Detection options
var detectionOptions = new DetectionOptions
{
    MinAreaRatio = 0.1f,        // Minimum document size (10% of image)
    MaxAreaRatio = 0.95f,       // Maximum document size (95% of image)
    ProcessingScale = 0.5f,     // Scale for faster processing
    BlurKernelSize = 5,         // Gaussian blur kernel size
    CannyLowThreshold = 50,     // Edge detection low threshold
    CannyHighThreshold = 150,   // Edge detection high threshold
    UseAdaptiveThreshold = true // Use adaptive thresholding
};

// Processing options
var processingOptions = new ProcessingOptions
{
    TargetAspectRatio = 0.707f, // A4 portrait aspect ratio
    MaxWidth = 2000,            // Limit output width
    MaxHeight = 3000,           // Limit output height
    OutputFormat = SKEncodedImageFormat.Jpeg,
    JpegQuality = 90,
    ApplyWhiteBalance = true,   // Auto white balance
    EnhanceContrast = true,     // Enhance contrast
    Sharpen = true,             // Sharpen output
    ConvertToGrayscale = false, // Keep colors
    ApplyBinarization = false,  // Don't convert to B&W
    Padding = 10                // 10px padding around document
};

var scanner = new Scanner(detectionOptions, processingOptions);
var result = scanner.Scan(imageBytes);
```

### Async Operations

```csharp
var scanner = new Scanner();

// Async scanning
var result = await scanner.ScanAsync(imageBytes);

// With cancellation
var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
var result = await scanner.ScanAsync(imageBytes, cancellationToken: cts.Token);
```

## API Reference

### Main Classes

#### `Scanner`
The main facade class for document scanning operations.

| Method | Description |
|--------|-------------|
| `Scan(imageBytes, options?)` | Detect and correct document in one operation |
| `ScanAsync(...)` | Async version of Scan |
| `Detect(imageBytes, options?)` | Only detect document without correction |
| `DetectAsync(...)` | Async version of Detect |
| `CorrectPerspective(imageBytes, corners, options?)` | Correct perspective with specified corners |
| `CreateDetectionVisualization(...)` | Create preview image with detected corners overlay |

#### `DocumentDetector`
Low-level document detection.

| Method | Description |
|--------|-------------|
| `Detect(imageBytes, options?)` | Detect document and return DetectionResult |
| `Detect(bitmap, options?)` | Detect document from SKBitmap |

#### `DocumentProcessor`
Low-level document processing.

| Method | Description |
|--------|-------------|
| `CorrectPerspective(imageBytes, corners, options?)` | Apply perspective transformation |
| `ProcessDetectionResult(imageBytes, result, options?)` | Process a detection result |

### Data Structures

#### `DetectionResult`
Contains the result of document detection.

| Property | Type | Description |
|----------|------|-------------|
| `Success` | bool | Whether detection was successful |
| `Corners` | Quadrilateral? | Detected document corners |
| `Mask` | DocumentMask? | Binary mask of document region |
| `AngleInfo` | DocumentAngleInfo? | Angle and orientation info |
| `Confidence` | float | Detection confidence (0-1) |
| `ErrorMessage` | string? | Error message if failed |

#### `Quadrilateral`
Represents the four corners of a detected document.

| Property | Type | Description |
|----------|------|-------------|
| `TopLeft` | PointF | Top-left corner |
| `TopRight` | PointF | Top-right corner |
| `BottomRight` | PointF | Bottom-right corner |
| `BottomLeft` | PointF | Bottom-left corner |
| `Width` | float | Average width |
| `Height` | float | Average height |
| `Area` | float | Area of quadrilateral |
| `Center` | PointF | Center point |

#### `DocumentAngleInfo`
Contains angle and orientation information.

| Property | Type | Description |
|----------|------|-------------|
| `RotationAngle` | float | Rotation in degrees (-180 to 180) |
| `HorizontalSkew` | float | Horizontal perspective skew |
| `VerticalSkew` | float | Vertical perspective skew |
| `IsUpsideDown` | bool | Whether document appears upside down |
| `Confidence` | float | Angle detection confidence |

## Architecture

```
DocumentScanner/
├── Core/
│   └── DataStructures.cs      # PointF, Quadrilateral, DetectionResult, etc.
├── ImageProcessing/
│   ├── ImageProcessor.cs      # Image loading, grayscale, blur, resize
│   ├── EdgeDetector.cs        # Sobel and Canny edge detection
│   ├── ContourDetector.cs     # Contour finding and polygon approximation
│   ├── Thresholder.cs         # Otsu, adaptive, Sauvola thresholding
│   ├── PerspectiveTransform.cs # Homography and perspective warping
│   └── HoughTransform.cs      # Line detection (optional)
├── DocumentDetector.cs        # Main detection logic
├── DocumentProcessor.cs       # Perspective correction and enhancement
└── Scanner.cs                 # High-level facade
```

## Algorithm Overview

1. **Preprocessing**
   - Convert to grayscale
   - Apply CLAHE contrast enhancement
   - Gaussian blur for noise reduction

2. **Edge Detection**
   - Sauvola adaptive thresholding (for documents)
   - Canny edge detection
   - Morphological closing to connect edges

3. **Contour Detection**
   - Find contours using border following
   - Compute convex hull
   - Approximate polygon using Douglas-Peucker

4. **Quadrilateral Selection**
   - Filter by area constraints
   - Score candidates by:
     - Area coverage
     - Aspect ratio similarity to documents
     - Corner angle regularity
     - Edge parallelism

5. **Perspective Correction**
   - Compute homography matrix (DLT algorithm)
   - Apply inverse perspective warp
   - Optional image enhancements

## Performance Tips

1. **Use ProcessingScale**: Set `DetectionOptions.ProcessingScale` to 0.25-0.5 for faster detection
2. **Limit Output Size**: Set `MaxWidth`/`MaxHeight` in ProcessingOptions
3. **Use Async**: Use `ScanAsync` for UI responsiveness
4. **Reuse Scanner**: Create one `Scanner` instance and reuse it

## Common Issues

### Document Not Detected
- Ensure good lighting and contrast
- Try adjusting `CannyLowThreshold` and `CannyHighThreshold`
- Decrease `MinAreaRatio` if document is small in frame
- Ensure document edges are visible

### Poor Corner Detection
- Increase `ProcessingScale` for better accuracy
- Ensure document has clear edges
- Try `UseAdaptiveThreshold = true`

### Slow Performance
- Decrease `ProcessingScale`
- Set `MaxWidth`/`MaxHeight` limits
- Use async methods

## License

MIT License - see LICENSE file for details.

## Contributing

Contributions are welcome! Please feel free to submit issues and pull requests.
