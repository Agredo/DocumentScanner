# Document Visualization

The DocumentScanner library includes powerful visualization capabilities to display detected documents with overlays, corner markers, and confidence scores.

## Basic Usage

```csharp
using DocumentScanner;
using DocumentScanner.Visualization;

var scanner = new Scanner();
byte[] imageBytes = File.ReadAllBytes("photo.jpg");

// Detect document
var result = scanner.Detect(imageBytes);

if (result.Success)
{
    // Create visualization
    var visualizedBytes = scanner.CreateVisualization(imageBytes, result);
    File.WriteAllBytes("detected_document.jpg", visualizedBytes);
}
```

## Customization Options

You can customize the visualization appearance:

```csharp
var options = new VisualizationOptions
{
    // Document overlay
    FillColor = new SKColor(255, 0, 0, 80),     // Red with transparency
    BorderColor = SKColors.Blue,
    BorderWidth = 10f,
    
    // Corner markers
    CornerColor = SKColors.Yellow,
    CornerBorderColor = SKColors.Black,
    CornerRadius = 25f,
    CornerBorderWidth = 5f,
    
    // Labels
    ShowCornerLabels = true,
    LabelFontSize = 50f,
    LabelColor = SKColors.White,
    
    // Confidence score
    ShowConfidence = true,
    ConfidenceFontSize = 60f,
    ConfidenceTextColor = SKColors.White,
    ConfidenceBackgroundColor = new SKColor(0, 0, 0, 200),
    
    // Output format
    OutputFormat = SKEncodedImageFormat.Png,
    OutputQuality = 100
};

var visualizedBytes = scanner.CreateVisualization(imageBytes, result, options);
```

## Visualization Elements

The visualization includes:

### 1. **Document Overlay**
- Semi-transparent colored fill over the detected document area
- Customizable color and transparency
- Border around the document with adjustable width and color

### 2. **Corner Markers**
- Circles marking the four detected corners (TL, TR, BR, BL)
- White border around each marker for better visibility
- Customizable size and colors

### 3. **Corner Labels**
- "TL" (Top-Left), "TR" (Top-Right), "BR" (Bottom-Right), "BL" (Bottom-Left)
- Positioned above each corner
- Can be disabled if not needed
- Customizable font size and color

### 4. **Confidence Score**
- Displays detection confidence as percentage
- Shown in top-left corner with rounded background
- Can be disabled if not needed
- Customizable font size, colors, and background

## Output Formats

Supported output formats:
- `SKEncodedImageFormat.Jpeg` - Default (quality 0-100)
- `SKEncodedImageFormat.Png` - Lossless
- `SKEncodedImageFormat.Webp` - Modern format

```csharp
var options = new VisualizationOptions
{
    OutputFormat = SKEncodedImageFormat.Png,
    OutputQuality = 100  // Only for JPEG/WebP
};
```

## Getting SKBitmap Output

If you need the visualization as `SKBitmap` instead of byte array:

```csharp
using var bitmap = ImageProcessor.LoadImage(imageBytes);
var visualizedBitmap = scanner.CreateVisualizationBitmap(bitmap, result);

// Use visualizedBitmap for further processing...
visualizedBitmap.Dispose();
```

## Examples

### Minimal Overlay (Clean Look)

```csharp
var cleanOptions = new VisualizationOptions
{
    FillColor = new SKColor(0, 255, 0, 30),  // Very transparent
    BorderWidth = 4f,
    CornerRadius = 15f,
    ShowCornerLabels = false,
    ShowConfidence = false
};
```

### High Contrast (Maximum Visibility)

```csharp
var highContrastOptions = new VisualizationOptions
{
    FillColor = new SKColor(255, 255, 0, 100),  // Yellow
    BorderColor = SKColors.Red,
    BorderWidth = 12f,
    CornerColor = SKColors.Orange,
    CornerRadius = 30f,
    LabelFontSize = 60f
};
```

### Professional Look (Blue Theme)

```csharp
var professionalOptions = new VisualizationOptions
{
    FillColor = new SKColor(0, 120, 255, 50),   // Blue
    BorderColor = new SKColor(0, 120, 255),
    BorderWidth = 6f,
    CornerColor = new SKColor(0, 120, 255),
    CornerBorderColor = SKColors.White,
    LabelColor = SKColors.White,
    ConfidenceTextColor = new SKColor(0, 120, 255),
    ConfidenceBackgroundColor = new SKColor(255, 255, 255, 220)
};
```

## Integration with MAUI

The visualization works seamlessly in .NET MAUI apps:

```csharp
// Detect and visualize
var result = await scanner.DetectAsync(imageBytes);

if (result.Success)
{
    var previewBytes = scanner.CreateVisualization(imageBytes, result);
    
    // Display in MAUI Image control
    MyImageControl.Source = ImageSource.FromStream(
        () => new MemoryStream(previewBytes)
    );
}
```

## Performance Tips

1. **Reuse VisualizationOptions**: Create once and reuse for multiple visualizations
2. **Choose appropriate quality**: JPEG 85-95 is usually sufficient and much smaller than PNG
3. **Dispose SKBitmaps**: Always dispose SKBitmap instances when done
4. **Use async methods**: For UI responsiveness in mobile apps

```csharp
// Good: Reuse options
var options = new VisualizationOptions();
foreach (var image in images)
{
    var viz = scanner.CreateVisualization(image, result, options);
}

// Good: Async with cancellation
var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
var result = await scanner.DetectAsync(imageBytes, cancellationToken: cts.Token);
```

## API Reference

### VisualizationOptions Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `FillColor` | SKColor | Green (60 alpha) | Document overlay fill color |
| `BorderColor` | SKColor | LimeGreen | Document border color |
| `BorderWidth` | float | 8 | Document border width in pixels |
| `CornerColor` | SKColor | Red | Corner marker color |
| `CornerBorderColor` | SKColor | White | Corner marker border color |
| `CornerRadius` | float | 20 | Corner marker radius in pixels |
| `CornerBorderWidth` | float | 4 | Corner marker border width |
| `ShowCornerLabels` | bool | true | Show TL/TR/BR/BL labels |
| `LabelFontSize` | float | 40 | Corner label font size |
| `LabelColor` | SKColor | White | Corner label color |
| `ShowConfidence` | bool | true | Show confidence score |
| `ConfidenceFontSize` | float | 50 | Confidence text font size |
| `ConfidenceTextColor` | SKColor | White | Confidence text color |
| `ConfidenceBackgroundColor` | SKColor | Black (180 alpha) | Confidence background color |
| `OutputFormat` | SKEncodedImageFormat | JPEG | Output image format |
| `OutputQuality` | int | 95 | Output quality (0-100) |

### Scanner Methods

```csharp
// Create visualization from byte array
byte[] CreateVisualization(
    byte[] imageBytes, 
    DetectionResult result, 
    VisualizationOptions? options = null)

// Create visualization from SKBitmap
SKBitmap CreateVisualizationBitmap(
    SKBitmap bitmap, 
    DetectionResult result, 
    VisualizationOptions? options = null)
```

### DocumentVisualizer Static Methods

```csharp
// Direct visualization without Scanner instance
byte[] DocumentVisualizer.CreateVisualization(
    byte[] imageBytes, 
    DetectionResult result, 
    VisualizationOptions? options = null)

SKBitmap DocumentVisualizer.CreateVisualizationBitmap(
    SKBitmap bitmap, 
    DetectionResult result, 
    VisualizationOptions? options = null)
```
