using DocumentScanner.Core;

namespace DocumentScanner.Maui.SampleApp;

/// <summary>
/// Main page demonstrating the DocumentScanner library usage in MAUI.
/// </summary>
public partial class MainPage : ContentPage
{
    // Scanner instance - reused for better performance
    private readonly Scanner scanner;
    
    // Current image data
    private byte[]? currentImageBytes;
    private DetectionResult? currentDetection;
    
    public MainPage()
    {
        InitializeComponent();
        
        // Initialize scanner with optimized options for better edge detection
        // These settings work well for documents with low contrast backgrounds
        var detectionOptions = new DetectionOptions
        {
            ProcessingScale = 0.7f,      // Use 70% scale for better detail (was 0.5f)
            MinAreaRatio = 0.05f,        // Allow smaller documents (was 0.1f)
            MaxAreaRatio = 0.98f,        // Allow documents that fill the image
            UseAdaptiveThreshold = false, // Disable for better edge detection (was true)
            EnhanceContrast = true,      // Improve edge detection
            BlurKernelSize = 3,          // Less blur to preserve edges (was 5)
            CannyLowThreshold = 30,      // Lower threshold for subtle edges (was 50)
            CannyHighThreshold = 100,    // Lower high threshold too (was 150)
            ApproximationEpsilon = 0.015f // More accurate polygon approximation (was 0.02f)
        };
        
        scanner = new Scanner(detectionOptions);
    }

    /// <summary>
    /// Handles the Select Image button click - opens file picker.
    /// </summary>
    private async void OnSelectImageClicked(object sender, EventArgs e)
    {
        try
        {
            // Open file picker for images
            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Select a document image",
                FileTypes = FilePickerFileType.Images
            });

            if (result != null)
            {
                await ProcessSelectedFile(result);
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to select image: {ex.Message}", "OK");
        }
    }

    /// <summary>
    /// Handles the Take Photo button click - opens camera.
    /// </summary>
    private async void OnTakePhotoClicked(object sender, EventArgs e)
    {
        try
        {
            // Check if camera is available
            if (!MediaPicker.Default.IsCaptureSupported)
            {
                await DisplayAlert("Error", "Camera is not available on this device", "OK");
                return;
            }

            // Capture photo
            var photo = await MediaPicker.Default.CapturePhotoAsync(new MediaPickerOptions
            {
                Title = "Take a photo of your document"
            });

            if (photo != null)
            {
                await ProcessSelectedFile(photo);
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to take photo: {ex.Message}", "OK");
        }
    }

    /// <summary>
    /// Processes a selected or captured image file.
    /// </summary>
    private async Task ProcessSelectedFile(FileResult file)
    {
        SetLoading(true);

        try
        {
            // Read image bytes
            using var stream = await file.OpenReadAsync();
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream);
            currentImageBytes = memoryStream.ToArray();

            // Detect document
            currentDetection = await scanner.DetectAsync(currentImageBytes);

            if (currentDetection.Success)
            {
                // Show detection preview with corners highlighted
                var previewBytes = scanner.CreateDetectionVisualization(
                    currentImageBytes, 
                    currentDetection
                );
                
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    ImagePreview.Source = ImageSource.FromStream(() => new MemoryStream(previewBytes));
                    PlaceholderLabel.IsVisible = false;
                    UpdateInfoPanel(currentDetection);
                    ProcessButton.IsEnabled = true;
                });
            }
            else
            {
                // Show original image even if detection failed
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    ImagePreview.Source = ImageSource.FromStream(() => new MemoryStream(currentImageBytes));
                    PlaceholderLabel.IsVisible = false;
                    UpdateInfoPanel(currentDetection);
                    ProcessButton.IsEnabled = false;
                });
                
                await DisplayAlert("Detection", 
                    $"No document detected: {currentDetection.ErrorMessage}", 
                    "OK");
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to process image: {ex.Message}", "OK");
        }
        finally
        {
            SetLoading(false);
        }
    }

    /// <summary>
    /// Handles the Process button click - applies perspective correction.
    /// </summary>
    private async void OnProcessClicked(object sender, EventArgs e)
    {
        if (currentImageBytes == null || currentDetection?.Corners == null)
        {
            await DisplayAlert("Error", "No document detected to process", "OK");
            return;
        }

        SetLoading(true);

        try
        {
            // Configure processing options
            var processingOptions = new ProcessingOptions
            {
                EnhanceContrast = true,    // Improve document readability
                ApplyWhiteBalance = true,  // Correct color cast
                Sharpen = true,            // Sharpen text
                OutputFormat = SkiaSharp.SKEncodedImageFormat.Png,
                Padding = 10               // Add small border
            };

            // Process the document
            var correctedBytes = await Task.Run(() => 
                scanner.Process(currentImageBytes, currentDetection, processingOptions)
            );

            // Display the corrected document
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                ImagePreview.Source = ImageSource.FromStream(() => new MemoryStream(correctedBytes));
                StatusLabel.Text = "✅ Document processed successfully!";
                StatusLabel.TextColor = Colors.Green;
            });

            // Offer to save the result
            bool save = await DisplayAlert(
                "Success", 
                "Document processed successfully! Would you like to save it?", 
                "Save", 
                "Cancel");

            if (save)
            {
                await SaveProcessedDocument(correctedBytes);
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to process document: {ex.Message}", "OK");
        }
        finally
        {
            SetLoading(false);
        }
    }

    /// <summary>
    /// Saves the processed document to the device.
    /// </summary>
    private async Task SaveProcessedDocument(byte[] imageBytes)
    {
        try
        {
            // Generate filename with timestamp
            string fileName = $"scanned_document_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            
            // Save to app's cache directory first
            string filePath = Path.Combine(FileSystem.CacheDirectory, fileName);
            await File.WriteAllBytesAsync(filePath, imageBytes);

            // Share the file (allows user to save to their preferred location)
            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "Save Scanned Document",
                File = new ShareFile(filePath)
            });
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to save document: {ex.Message}", "OK");
        }
    }

    /// <summary>
    /// Updates the info panel with detection results.
    /// </summary>
    private void UpdateInfoPanel(DetectionResult? result)
    {
        InfoPanel.IsVisible = true;

        if (result == null || !result.Success)
        {
            StatusLabel.Text = "❌ No document detected";
            StatusLabel.TextColor = Colors.Red;
            ConfidenceLabel.Text = "";
            AngleLabel.Text = "";
            DimensionsLabel.Text = "";
            return;
        }

        StatusLabel.Text = "✅ Document detected";
        StatusLabel.TextColor = Colors.Green;
        
        ConfidenceLabel.Text = $"Confidence: {result.Confidence:P0}";
        
        if (result.AngleInfo != null)
        {
            AngleLabel.Text = $"Rotation: {result.AngleInfo.RotationAngle:F1}° | " +
                             $"Skew: H={result.AngleInfo.HorizontalSkew:F1}° V={result.AngleInfo.VerticalSkew:F1}°";
        }

        if (result.Corners != null)
        {
            DimensionsLabel.Text = $"Size: {result.Corners.Width:F0} × {result.Corners.Height:F0} px";
        }
    }

    /// <summary>
    /// Sets the loading state of the UI.
    /// </summary>
    private void SetLoading(bool isLoading)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            LoadingIndicator.IsRunning = isLoading;
            LoadingIndicator.IsVisible = isLoading;
            SelectImageButton.IsEnabled = !isLoading;
            TakePhotoButton.IsEnabled = !isLoading;
            ProcessButton.IsEnabled = !isLoading && currentDetection?.Success == true;
        });
    }
}
