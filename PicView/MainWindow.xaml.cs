using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace PicView;

public partial class MainWindow : Window
{
    private static readonly string[] SupportedExtensions = 
    { 
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tiff", ".tif", ".ico" 
    };

    private List<string> _imageFiles = new();
    private int _currentIndex = -1;
    private double _currentZoom = 1.0;
    private Point _lastMousePosition;
    private bool _isDragging;
    
    // Image position (top-left corner in canvas coordinates)
    private double _imageX;
    private double _imageY;

    public MainWindow()
    {
        InitializeComponent();
        UpdateUI();
        
        // Handle window resize to fit image
        ImageCanvas.SizeChanged += (s, e) => 
        {
            if (MainImage.Source != null)
                FitImageToWindow();
        };
    }

    public void LoadImage(string filePath)
    {
        if (!File.Exists(filePath))
        {
            MessageBox.Show($"File not found: {filePath}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var directory = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(directory)) return;

        // Get all image files in the directory
        _imageFiles = Directory.GetFiles(directory)
            .Where(f => SupportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Find the index of the requested file
        _currentIndex = _imageFiles.FindIndex(f => 
            string.Equals(f, filePath, StringComparison.OrdinalIgnoreCase));

        if (_currentIndex == -1)
        {
            _imageFiles.Clear();
            MessageBox.Show("Could not load the image.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        DisplayCurrentImage();
    }

    private void DisplayCurrentImage()
    {
        if (_currentIndex < 0 || _currentIndex >= _imageFiles.Count)
        {
            MainImage.Source = null;
            UpdateUI();
            return;
        }

        try
        {
            var filePath = _imageFiles[_currentIndex];
            
            // Load image with caching for better performance
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(filePath);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();

            MainImage.Source = bitmap;
            
            // Update window title
            Title = $"PicView - {Path.GetFileName(filePath)}";
            
            UpdateUI();
            
            // Fit to window after layout
            Dispatcher.BeginInvoke(new Action(FitImageToWindow), 
                System.Windows.Threading.DispatcherPriority.Loaded);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading image: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UpdateUI()
    {
        bool hasImage = MainImage.Source != null;
        DropHint.Visibility = hasImage ? Visibility.Collapsed : Visibility.Visible;

        if (hasImage && _currentIndex >= 0)
        {
            var filePath = _imageFiles[_currentIndex];
            var fileInfo = new FileInfo(filePath);
            var bitmap = MainImage.Source as BitmapSource;

            FileNameText.Text = Path.GetFileName(filePath);
            ImageCountText.Text = $"{_currentIndex + 1} / {_imageFiles.Count}";
            
            if (bitmap != null)
            {
                var sizeText = FormatFileSize(fileInfo.Length);
                ImageInfoText.Text = $"{bitmap.PixelWidth} × {bitmap.PixelHeight} px | {sizeText} | {fileInfo.LastWriteTime:g}";
            }
        }
        else
        {
            FileNameText.Text = "";
            ImageCountText.Text = "";
            ImageInfoText.Text = "";
            Title = "PicView";
        }

        ZoomText.Text = $"{_currentZoom:P0}";
    }

    private static string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:0.##} {sizes[order]}";
    }

    private void FitImageToWindow()
    {
        if (MainImage.Source is not BitmapSource bitmap) return;

        var containerWidth = ImageCanvas.ActualWidth;
        var containerHeight = ImageCanvas.ActualHeight;

        if (containerWidth <= 0 || containerHeight <= 0)
        {
            Dispatcher.BeginInvoke(new Action(FitImageToWindow), 
                System.Windows.Threading.DispatcherPriority.Background);
            return;
        }

        var scaleX = containerWidth / bitmap.PixelWidth;
        var scaleY = containerHeight / bitmap.PixelHeight;
        
        _currentZoom = Math.Min(scaleX, scaleY);
        
        // Don't upscale small images beyond 100%
        if (_currentZoom > 1.0) _currentZoom = 1.0;

        ApplyZoom();
        CenterImage();
        UpdateUI();
    }

    private void ApplyZoom()
    {
        if (MainImage.Source is not BitmapSource bitmap) return;
        
        MainImage.Width = bitmap.PixelWidth * _currentZoom;
        MainImage.Height = bitmap.PixelHeight * _currentZoom;
    }

    private void CenterImage()
    {
        if (MainImage.Source is not BitmapSource bitmap) return;

        var containerWidth = ImageCanvas.ActualWidth;
        var containerHeight = ImageCanvas.ActualHeight;
        var scaledWidth = bitmap.PixelWidth * _currentZoom;
        var scaledHeight = bitmap.PixelHeight * _currentZoom;

        _imageX = (containerWidth - scaledWidth) / 2;
        _imageY = (containerHeight - scaledHeight) / 2;

        Canvas.SetLeft(MainImage, _imageX);
        Canvas.SetTop(MainImage, _imageY);
    }

    private void SetZoom(double newZoom, Point? mousePositionInCanvas = null)
    {
        if (MainImage.Source is not BitmapSource bitmap) return;

        newZoom = Math.Clamp(newZoom, 0.01, 50.0);
        
        if (Math.Abs(_currentZoom - newZoom) < 0.001) return;

        if (mousePositionInCanvas.HasValue)
        {
            // Zoom toward mouse position
            var mousePos = mousePositionInCanvas.Value;
            
            // Calculate position relative to image before zoom
            var relX = (mousePos.X - _imageX) / _currentZoom;
            var relY = (mousePos.Y - _imageY) / _currentZoom;
            
            _currentZoom = newZoom;
            
            // Calculate new image position to keep the point under mouse stationary
            _imageX = mousePos.X - relX * _currentZoom;
            _imageY = mousePos.Y - relY * _currentZoom;
        }
        else
        {
            // Zoom toward center of canvas
            var centerX = ImageCanvas.ActualWidth / 2;
            var centerY = ImageCanvas.ActualHeight / 2;
            
            var relX = (centerX - _imageX) / _currentZoom;
            var relY = (centerY - _imageY) / _currentZoom;
            
            _currentZoom = newZoom;
            
            _imageX = centerX - relX * _currentZoom;
            _imageY = centerY - relY * _currentZoom;
        }

        ApplyZoom();
        ClampPan();
        Canvas.SetLeft(MainImage, _imageX);
        Canvas.SetTop(MainImage, _imageY);
        UpdateUI();
    }

    private void ClampPan()
    {
        if (MainImage.Source is not BitmapSource bitmap) return;

        var containerWidth = ImageCanvas.ActualWidth;
        var containerHeight = ImageCanvas.ActualHeight;
        var scaledWidth = bitmap.PixelWidth * _currentZoom;
        var scaledHeight = bitmap.PixelHeight * _currentZoom;

        // If image is smaller than container, center it
        if (scaledWidth <= containerWidth)
        {
            _imageX = (containerWidth - scaledWidth) / 2;
        }
        else
        {
            // Don't allow panning past edges
            var minX = containerWidth - scaledWidth;
            var maxX = 0.0;
            _imageX = Math.Clamp(_imageX, minX, maxX);
        }

        if (scaledHeight <= containerHeight)
        {
            _imageY = (containerHeight - scaledHeight) / 2;
        }
        else
        {
            var minY = containerHeight - scaledHeight;
            var maxY = 0.0;
            _imageY = Math.Clamp(_imageY, minY, maxY);
        }
    }

    private void NavigateNext()
    {
        if (_imageFiles.Count == 0) return;
        _currentIndex = (_currentIndex + 1) % _imageFiles.Count;
        DisplayCurrentImage();
    }

    private void NavigatePrevious()
    {
        if (_imageFiles.Count == 0) return;
        _currentIndex = (_currentIndex - 1 + _imageFiles.Count) % _imageFiles.Count;
        DisplayCurrentImage();
    }

    private void OpenFile()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Image files|*.jpg;*.jpeg;*.png;*.gif;*.bmp;*.webp;*.tiff;*.tif;*.ico|All files|*.*",
            Title = "Open Image"
        };

        if (dialog.ShowDialog() == true)
        {
            LoadImage(dialog.FileName);
        }
    }

    private void DeleteCurrentFile()
    {
        if (_currentIndex < 0 || _currentIndex >= _imageFiles.Count) return;

        var filePath = _imageFiles[_currentIndex];
        var directory = Path.GetDirectoryName(filePath);
        var fileNameWithoutExt = Path.GetFileNameWithoutExtension(filePath);
        
        // Find corresponding video file using wildcard
        string? videoFile = null;
        if (!string.IsNullOrEmpty(directory))
        {
            videoFile = Directory.GetFiles(directory, fileNameWithoutExt + ".*")
                .FirstOrDefault(f => !f.Equals(filePath, StringComparison.OrdinalIgnoreCase) 
                    && !SupportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()));
        }


        try
        {
            // Delete video file first if it exists
            if (videoFile != null)
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                    videoFile,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            }

            // Delete the image file
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                filePath,
                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);

            _imageFiles.RemoveAt(_currentIndex);

            if (_imageFiles.Count == 0)
            {
                _currentIndex = -1;
                MainImage.Source = null;
            }
            else
            {
                _currentIndex = Math.Min(_currentIndex, _imageFiles.Count - 1);
                DisplayCurrentImage();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error deleting file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RenameCurrentFile()
    {
        if (_currentIndex < 0 || _currentIndex >= _imageFiles.Count) return;

        var filePath = _imageFiles[_currentIndex];
        var directory = Path.GetDirectoryName(filePath);
        var currentName = Path.GetFileNameWithoutExtension(filePath);
        var imageExtension = Path.GetExtension(filePath);

        if (string.IsNullOrEmpty(directory)) return;

        // Find corresponding video file
        string? videoFile = Directory.GetFiles(directory, currentName + ".*")
            .FirstOrDefault(f => !f.Equals(filePath, StringComparison.OrdinalIgnoreCase)
                && !SupportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()));

        // Show input dialog
        var newName = Microsoft.VisualBasic.Interaction.InputBox(
            "Enter new filename:",
            "Rename",
            currentName);

        // User cancelled or entered empty string
        if (string.IsNullOrWhiteSpace(newName) || newName == currentName) return;

        // Validate filename
        if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            MessageBox.Show("Invalid filename. Please avoid special characters.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var newImagePath = Path.Combine(directory, newName + imageExtension);

        // Check if target already exists
        if (File.Exists(newImagePath))
        {
            MessageBox.Show($"A file named '{newName}{imageExtension}' already exists.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        try
        {
            // Rename video file first if it exists
            if (videoFile != null)
            {
                var videoExtension = Path.GetExtension(videoFile);
                var newVideoPath = Path.Combine(directory, newName + videoExtension);

                if (File.Exists(newVideoPath))
                {
                    MessageBox.Show($"A file named '{newName}{videoExtension}' already exists.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                File.Move(videoFile, newVideoPath);
            }

            // Rename the image file
            File.Move(filePath, newImagePath);

            // Update the file list and re-sort
            _imageFiles[_currentIndex] = newImagePath;


            // Refresh display
            UpdateUI();
            Title = $"PicView - {Path.GetFileName(newImagePath)}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error renaming file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    #region Event Handlers

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Right:
            case Key.Space:
                NavigateNext();
                break;
            case Key.Left:
            case Key.Back:
                NavigatePrevious();
                break;
            case Key.Home:
                if (_imageFiles.Count > 0)
                {
                    _currentIndex = 0;
                    DisplayCurrentImage();
                }
                break;
            case Key.End:
                if (_imageFiles.Count > 0)
                {
                    _currentIndex = _imageFiles.Count - 1;
                    DisplayCurrentImage();
                }
                break;
            case Key.Add:
            case Key.OemPlus:
                SetZoom(_currentZoom * 1.25);
                break;
            case Key.Subtract:
            case Key.OemMinus:
                SetZoom(_currentZoom / 1.25);
                break;
            case Key.D1:
            case Key.NumPad1:
                _currentZoom = 1.0;
                ApplyZoom();
                CenterImage();
                UpdateUI();
                break;
            case Key.F:
                FitImageToWindow();
                break;
            case Key.O:
                if (Keyboard.Modifiers == ModifierKeys.Control || Keyboard.Modifiers == ModifierKeys.None)
                    OpenFile();
                break;
            case Key.Delete:
                DeleteCurrentFile();
                break;
            case Key.F2:
                RenameCurrentFile();
                break;
            case Key.Escape:
                FitImageToWindow();
                break;
            case Key.F11:
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                break;
        }
    }

    private void MainImage_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        var mousePos = e.GetPosition(ImageCanvas);
        var zoomFactor = e.Delta > 0 ? 1.15 : 1 / 1.15;
        SetZoom(_currentZoom * zoomFactor, mousePos);
    }

    private void MainImage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (MainImage.Source == null) return;

        _isDragging = true;
        _lastMousePosition = e.GetPosition(ImageCanvas);
        MainImage.CaptureMouse();
        MainImage.Cursor = Cursors.Hand;
    }

    private void MainImage_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isDragging = false;
        MainImage.ReleaseMouseCapture();
        MainImage.Cursor = Cursors.Arrow;
    }

    private void MainImage_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging) return;

        var currentPosition = e.GetPosition(ImageCanvas);
        var deltaX = currentPosition.X - _lastMousePosition.X;
        var deltaY = currentPosition.Y - _lastMousePosition.Y;
        _lastMousePosition = currentPosition;

        _imageX += deltaX;
        _imageY += deltaY;
        ClampPan();
        
        Canvas.SetLeft(MainImage, _imageX);
        Canvas.SetTop(MainImage, _imageY);
    }

    private void Previous_Click(object sender, RoutedEventArgs e) => NavigatePrevious();
    private void Next_Click(object sender, RoutedEventArgs e) => NavigateNext();
    
    private void ZoomIn_Click(object sender, RoutedEventArgs e) => SetZoom(_currentZoom * 1.25);
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => SetZoom(_currentZoom / 1.25);
    
    private void FitToWindow_Click(object sender, RoutedEventArgs e) => FitImageToWindow();
    
    private void ActualSize_Click(object sender, RoutedEventArgs e)
    {
        _currentZoom = 1.0;
        ApplyZoom();
        CenterImage();
        UpdateUI();
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files?.Length > 0)
            {
                var firstImage = files.FirstOrDefault(f => 
                    SupportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()));
                
                if (firstImage != null)
                {
                    LoadImage(firstImage);
                }
            }
        }
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) 
            ? DragDropEffects.Copy 
            : DragDropEffects.None;
        e.Handled = true;
    }

    #endregion
}
