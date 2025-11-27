# DocumentScanner Debug Tool

Dieses Konsolenprogramm dient zur Diagnose von Problemen mit der Document Detection, insbesondere bei der Contour Detection.

## Verwendung

1. **Bildpfad anpassen:**
   ```csharp
   string imagePath = @"C:\Users\chris\OneDrive\Bilder\Camera Roll\IMG_20250108_024734.jpg";
   ```

2. **Programm ausführen:**
   ```bash
   dotnet run --project TestConsoleApp
   ```

3. **Ausgabe analysieren:**
   - Console zeigt detaillierte Statistiken
   - Debug-Bilder werden im aktuellen Verzeichnis gespeichert

## Was wird getestet?

### 1. Verschiedene Konfigurationen
- **Default**: Standard-Einstellungen
- **Lower Canny Thresholds**: Für mehr Edge-Sensitivität
- **Very Low Thresholds**: Maximale Sensitivität
- **No Adaptive + Low Threshold**: Ohne Sauvola-Thresholding
- **Larger Scale**: Weniger Downsampling (mehr Details)

### 2. Detaillierte Analyse
Für jede Konfiguration:
- Helligkeit/Kontraststatistiken
- Edge-Pixel-Count nach Canny
- Edge-Pixel-Count nach Morphological Closing
- Anzahl gefundener Konturen
- Top 10 Konturen mit Fläche, Perimeter, Bounding Box
- Polygon-Approximation (potenzielle Dokument-Kandidaten)

### 3. Debug-Bilder
Gespeichert als PNG-Dateien:
- `01_grayscale.png` - Eingabe (Graustufen)
- `02_enhanced.png` - Nach Contrast Enhancement (CLAHE)
- `03_blurred.png` - Nach Gaussian Blur
- `04_thresholded.png` - Nach Adaptive Thresholding (Sauvola)
- `05_edges.png` - Canny Edge Detection
- `06_edges_closed.png` - Nach Morphological Closing
- `07_contours.png` - Gefundene Konturen (visualisiert)

## Ausgabe interpretieren

### Edge-Pixel-Prozentsatz
- **< 0.1%**: Zu wenig Kanten ? Thresholds zu hoch
- **0.1% - 5%**: Gut für Dokumente
- **> 30%**: Zu viel Noise ? Höhere Thresholds oder mehr Blur

### Contour-Statistiken
- **Anzahl**: 10-100 Konturen ist normal
- **Fläche**: Dokument sollte 20-80% der Bildfläche sein
- **Approximation**: 4 Vertices = Viereck (ideal für Dokumente)

### Diagnose-Meldungen
Bei "No contours found" gibt das Programm automatisch mögliche Ursachen aus:
- **Very few edge pixels**: Canny-Thresholds zu hoch
- **Too many edge pixels**: Zu viel Noise im Bild
- **Reasonable edge pixel count**: Problem in Contour-Tracing-Algorithmus

## Beispiel-Ausgabe

```
=== DocumentScanner Debug Tool ===

Loading image: C:\Users\chris\OneDrive\Bilder\Camera Roll\IMG_20250108_024734.jpg

Image loaded: 4000x3000 pixels

=== Testing: Default ===
  Success: False
  Error: No contours found in image

=== Testing: Lower Canny Thresholds ===
  Success: True
  Confidence: 0.85
  Corners: TL=(120.5, 89.3), TR=(3845.2, 102.7)
           BL=(95.8, 2856.4), BR=(3868.9, 2870.1)
  Rotation: -0.3°

=== DETAILED ANALYSIS (Default Settings) ===
Processing size: 2000x1500 (scale: 0.5)
Grayscale conversion: OK
  Saved: 01_grayscale.png
Brightness: min=45, max=248, avg=178.3, contrast=203
Gaussian blur: kernel size 5
  Saved: 03_blurred.png
Canny edge detection: low=50, high=150
  Edge pixels found: 1234 (0.08%)
  Saved: 05_edges.png
  After morphological close: 2456 pixels (0.16%)
  Saved: 06_edges_closed.png

*** NO CONTOURS FOUND ***
Possible causes:
  1. Edge detection thresholds too high
  2. Not enough contrast in the image
  3. Edges broken/disconnected
  4. Bug in contour tracing algorithm

  -> DIAGNOSIS: Very few edge pixels detected!
     Try lower Canny thresholds or disable adaptive thresholding.
```

## Tipps zur Fehlerbehebung

### 1. Keine Kanten sichtbar in `05_edges.png`
? Canny-Thresholds zu hoch oder zu wenig Kontrast

**Lösung:**
```csharp
options.CannyLowThreshold = 10;
options.CannyHighThreshold = 30;
options.EnhanceContrast = true;
```

### 2. Kanten fragmentiert in `06_edges_closed.png`
? Morphological Closing zu schwach

**Lösung:**
```csharp
// In DocumentDetector.cs, Zeile ~84:
edges = EdgeDetector.MorphologicalClose(edges, 5); // statt 3
```

### 3. Zu viel Noise in `05_edges.png`
? Mehr Blur oder höhere Thresholds

**Lösung:**
```csharp
options.BlurKernelSize = 7; // statt 5
options.CannyLowThreshold = 50;
options.CannyHighThreshold = 150;
```

### 4. Dokumentkanten erkennbar, aber keine Konturen
? Möglicher Bug im Contour-Tracing

**Lösung:**
- Prüfe ob die Kanten zusammenhängend sind
- Versuche alternative Methode `FindContoursSimple()`
- Erhöhe `ProcessingScale` für mehr Details

## Weitere Anpassungen

### Andere Bilder testen
```csharp
string[] testImages = {
    @"C:\path\to\image1.jpg",
    @"C:\path\to\image2.jpg",
    @"C:\path\to\image3.jpg"
};

foreach (var imagePath in testImages)
{
    Console.WriteLine($"\n=== Testing: {Path.GetFileName(imagePath)} ===");
    // ...
}
```

### Alternative Contour-Methode verwenden
```csharp
// In DetailedAnalysis(), ersetze:
var contours = ContourDetector.FindContours(edges);

// Mit:
var contours = ContourDetector.FindContoursSimple(edges);
Console.WriteLine("Using SIMPLE contour detection method");
```

### Zusätzliche Ausgaben
```csharp
// Edge-Richtungen visualisieren
var (magnitude, direction) = EdgeDetector.Sobel(blurred, computeDirection: true);
SaveDebugImage(magnitude, "debug_gradient_magnitude.png");

// Thresholding ohne Canny
var simpleThreshold = Thresholder.OtsuThreshold(blurred);
SaveDebugImage(simpleThreshold, "debug_otsu_threshold.png");
```

## Systemanforderungen

- .NET 10.0 (Windows)
- SkiaSharp 3.119.1
- Mindestens 4 GB RAM für große Bilder

## Bekannte Einschränkungen

- Nur Windows-Platform wird unterstützt (net10.0-windows10.0.19041)
- Große Bilder (> 20 MP) können langsam sein
- Debug-Bilder werden im aktuellen Arbeitsverzeichnis gespeichert
