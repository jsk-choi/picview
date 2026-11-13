using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace PicView;

public enum SortMode
{
    NameAscending,
    DateModifiedDescending
}

/// <summary>
/// Comparer that uses Windows Explorer's natural sort order (StrCmpLogicalW)
/// </summary>
public class NaturalFileNameComparer : IComparer<string>
{
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int StrCmpLogicalW(string psz1, string psz2);

    public int Compare(string? x, string? y)
    {
        if (x == null && y == null) return 0;
        if (x == null) return -1;
        if (y == null) return 1;
        
        // Compare by filename only (not full path) to match Explorer behavior
        return StrCmpLogicalW(Path.GetFileName(x), Path.GetFileName(y));
    }
}

public partial class MainWindow : Window
{
    private static readonly string[] SupportedExtensions =
    {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tiff", ".tif", ".ico"
    };

    // Extensions treated as a companion video, in order of preference when several exist
    private static readonly string[] VideoExtensions =
    {
        ".mp4", ".mov", ".m4v", ".mkv", ".avi", ".wmv", ".webm", ".3gp", ".mts", ".m2ts"
    };

    private const string DefaultHint = "Drop an image here or press O to open";

    private List<string> _imageFiles = new();
    private int _currentIndex = -1;

    // Cached when the image loads so zooming doesn't hit the disk (and can't fail if the file vanishes)
    private string _currentInfoText = "";
    private string? _loadError;
    private double _currentZoom = 1.0;
    private Point _lastMousePosition;
    private bool _isDragging;

    // Image position (top-left corner in canvas coordinates)
    private double _imageX;
    private double _imageY;

    // Sorting
    private SortMode _currentSortMode = SortMode.NameAscending;

    // Settings file path
    private static readonly string SettingsFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PicView",
        "settings.json");

    // Clipboard collection for image/video pairs
    private readonly HashSet<string> _clipboardImageSet = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _clipboardEntries = new();

    public MainWindow()
    {
        InitializeComponent();
        
        // Load saved sort mode
        LoadSettings();
        
        UpdateUI();

        // Handle window resize to fit image
        ImageCanvas.SizeChanged += (s, e) =>
        {
            if (MainImage.Source != null)
                FitImageToWindow();
        };
    }

    private void LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                var json = File.ReadAllText(SettingsFilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings != null && Enum.TryParse<SortMode>(settings.SortMode, out var sortMode))
                {
                    _currentSortMode = sortMode;
                }
            }
        }
        catch
        {
            // Use default if settings can't be loaded
            _currentSortMode = SortMode.NameAscending;
        }
    }

    private void SaveSettings()
    {
        try
        {
            var directory = Path.GetDirectoryName(SettingsFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var settings = new AppSettings { SortMode = _currentSortMode.ToString() };
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFilePath, json);
        }
        catch
        {
            // Ignore save errors
        }
    }

    private class AppSettings
    {
        public string SortMode { get; set; } = "NameAscending";
    }

    private static bool IsSupportedImage(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>
    /// Finds a video in the same folder with exactly the same base name (e.g. IMG_1.jpg → IMG_1.mp4).
    /// </summary>
    private static string? FindCompanionVideo(string imagePath)
    {
        var directory = Path.GetDirectoryName(imagePath);
        if (string.IsNullOrEmpty(directory)) return null;

        var baseName = Path.GetFileNameWithoutExtension(imagePath);
        try
        {
            // The wildcard is only a pre-filter: "*" also matches dots, so check the base name exactly
            var candidates = Directory.EnumerateFiles(directory, baseName + ".*")
                .Where(f => string.Equals(Path.GetFileNameWithoutExtension(f), baseName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var ext in VideoExtensions)
            {
                var match = candidates.FirstOrDefault(f =>
                    string.Equals(Path.GetExtension(f), ext, StringComparison.OrdinalIgnoreCase));
                if (match != null) return match;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        return null;
    }

    private static bool TrySetClipboardText(string text)
    {
        try
        {
            if (text.Length == 0)
                Clipboard.Clear();
            else
                Clipboard.SetText(text);
            return true;
        }
        catch (ExternalException)
        {
            // Another app is holding the clipboard open
            return false;
        }
    }

    public void LoadImage(string filePath)
    {
        try
        {
            // Normalize relative paths, 8.3 short names, forward slashes, etc. so the lookup below matches
            filePath = Path.GetFullPath(filePath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            MessageBox.Show($"Invalid path: {filePath}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (!File.Exists(filePath))
        {
            MessageBox.Show($"File not found: {filePath}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (!IsSupportedImage(filePath))
        {
            MessageBox.Show($"Unsupported file type: {Path.GetExtension(filePath)}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var directory = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(directory)) return;

        // Get all image files in the directory
        var files = Directory.GetFiles(directory).Where(IsSupportedImage);

        // Apply current sort mode
        _imageFiles = ApplySort(files).ToList();

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

    private IEnumerable<string> ApplySort(IEnumerable<string> files)
    {
        return _currentSortMode switch
        {
            SortMode.NameAscending => files.OrderBy(f => f, new NaturalFileNameComparer()),
            SortMode.DateModifiedDescending => files.OrderByDescending(f => new FileInfo(f).LastWriteTime),
            _ => files.OrderBy(f => f, new NaturalFileNameComparer())
        };
    }

    private void ResortFiles()
    {
        if (_imageFiles.Count == 0) return;

        // Remember current file
        string? currentFile = _currentIndex >= 0 && _currentIndex < _imageFiles.Count
            ? _imageFiles[_currentIndex]
            : null;

        // Re-sort the list
        _imageFiles = ApplySort(_imageFiles).ToList();

        // Find the current file in the new order
        if (currentFile != null)
        {
            _currentIndex = _imageFiles.FindIndex(f =>
                string.Equals(f, currentFile, StringComparison.OrdinalIgnoreCase));
        }

        // The neighbours have changed
        PreloadAroundCurrent();
        UpdateUI();
    }

    private void RefreshFiles()
    {
        if (_imageFiles.Count == 0) return;

        // Remember current file
        string? currentFile = _currentIndex >= 0 && _currentIndex < _imageFiles.Count
            ? _imageFiles[_currentIndex]
            : null;

        if (currentFile == null) return;

        var directory = Path.GetDirectoryName(currentFile);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return;

        var previousIndex = _currentIndex;

        // Re-read the directory
        var files = Directory.GetFiles(directory).Where(IsSupportedImage);

        _imageFiles = ApplySort(files).ToList();

        // Try to find the same file
        _currentIndex = _imageFiles.FindIndex(f =>
            string.Equals(f, currentFile, StringComparison.OrdinalIgnoreCase));

        // If it was removed, stay near where we were (-1 if the folder is now empty)
        if (_currentIndex == -1)
        {
            _currentIndex = Math.Min(previousIndex, _imageFiles.Count - 1);
        }

        // F5 means "show me what's on disk now": decode everything fresh
        ClearImageCache();
        DisplayCurrentImage();
    }

    private void UpdateSortLabel()
    {
        SortLabel.Text = _currentSortMode switch
        {
            SortMode.NameAscending => "Sort: Name (A-Z)",
            SortMode.DateModifiedDescending => "Sort: Date (Newest)",
            _ => "Sort: Name (A-Z)"
        };
    }

    #region Image cache / preloading

    // Decoded images for the current file and its neighbours, keyed by path.
    private sealed class CacheEntry
    {
        public required Task<LoadedImage> Task { get; init; }
        public required CancellationTokenSource Cancel { get; init; }
    }

    private readonly Dictionary<string, CacheEntry> _imageCache = new(StringComparer.OrdinalIgnoreCase);

    // Limits concurrent decodes so holding an arrow key doesn't start dozens of large decodes at once
    private static readonly SemaphoreSlim DecodeSlots = new(2);

    // Incremented on every navigation, so a slow load can tell it has been superseded
    private int _displayVersion;

    private Task<LoadedImage> GetImageAsync(string path)
    {
        if (_imageCache.TryGetValue(path, out var entry))
        {
            var task = entry.Task;
            bool usable = !task.IsCompleted
                || (task.IsCompletedSuccessfully && task.Result.LastWriteUtc == ImageLoader.SafeLastWriteUtc(path));
            if (usable) return task;

            // Cancelled, or the file changed on disk since it was decoded: load again
            Evict(path);
        }

        var cts = new CancellationTokenSource();
        var token = cts.Token;
        var loadTask = Task.Run(async () =>
        {
            await DecodeSlots.WaitAsync(token);
            try
            {
                token.ThrowIfCancellationRequested();
                return ImageLoader.Load(path);
            }
            finally
            {
                DecodeSlots.Release();
            }
        }, token);

        _imageCache[path] = new CacheEntry { Task = loadTask, Cancel = cts };
        return loadTask;
    }

    private void Evict(string path)
    {
        if (_imageCache.Remove(path, out var entry))
        {
            entry.Cancel.Cancel();
        }
    }

    private void ClearImageCache()
    {
        foreach (var path in _imageCache.Keys.ToList()) Evict(path);
    }

    /// <summary>
    /// Starts decoding the neighbours of the current image, and drops everything else from the cache.
    /// </summary>
    private void PreloadAroundCurrent()
    {
        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (_currentIndex >= 0 && _currentIndex < _imageFiles.Count)
        {
            var count = _imageFiles.Count;
            // Current first, then next (the usual direction), then previous. Navigation wraps around.
            foreach (var offset in new[] { 0, 1, -1 })
            {
                keep.Add(_imageFiles[((_currentIndex + offset) % count + count) % count]);
            }
        }

        foreach (var path in _imageCache.Keys.Where(p => !keep.Contains(p)).ToList()) Evict(path);
        foreach (var path in keep) GetImageAsync(path);
    }

    #endregion

    private async void DisplayCurrentImage()
    {
        var version = ++_displayVersion;
        _loadError = null;
        _currentInfoText = "";

        if (_currentIndex < 0 || _currentIndex >= _imageFiles.Count)
        {
            MainImage.Source = null;
            PreloadAroundCurrent();
            UpdateUI();
            return;
        }

        var filePath = _imageFiles[_currentIndex];
        Title = $"PicView - {Path.GetFileName(filePath)}";

        var loadTask = GetImageAsync(filePath);
        PreloadAroundCurrent();

        if (!loadTask.IsCompleted)
        {
            // Not preloaded. Clear the old image rather than leave it up under the new file's
            // name (so e.g. Del can't act on a file you haven't seen yet).
            MainImage.Source = null;
            _loadError = "Loading…";
            UpdateUI();
        }

        LoadedImage result;
        try
        {
            // Completes synchronously when the image was preloaded, so it appears with no blank frame
            result = await loadTask;
        }
        catch (OperationCanceledException)
        {
            return; // Superseded by a newer navigation, refresh, etc.
        }

        if (version != _displayVersion) return;

        // A load failure is shown in place instead of in a dialog, so you can keep arrowing past bad files
        MainImage.Source = result.Bitmap;
        _loadError = result.Error;
        _currentInfoText = result.InfoText;

        UpdateUI();

        // Size and position the image now, before it is ever rendered, so it doesn't
        // briefly appear at the previous image's size and then jump.
        FitImageToWindow();
    }

    private void UpdateUI()
    {
        bool hasImage = MainImage.Source != null;
        bool hasFile = _currentIndex >= 0 && _currentIndex < _imageFiles.Count;

        DropHint.Text = _loadError ?? DefaultHint;
        DropHint.Visibility = hasImage ? Visibility.Collapsed : Visibility.Visible;

        if (hasFile)
        {
            FileNameText.Text = Path.GetFileName(_imageFiles[_currentIndex]);
            ImageCountText.Text = $"{_currentIndex + 1} / {_imageFiles.Count}";
            ImageInfoText.Text = _currentInfoText;
        }
        else
        {
            FileNameText.Text = "";
            ImageCountText.Text = "";
            ImageInfoText.Text = "";
            Title = "PicView";
        }

        ZoomText.Text = hasImage ? $"{_currentZoom:P0}" : "";
        UpdateSortLabel();
    }

    private void FitImageToWindow()
    {
        if (MainImage.Source is not BitmapSource bitmap) return;

        var containerWidth = ImageCanvas.ActualWidth;
        var containerHeight = ImageCanvas.ActualHeight;

        if (containerWidth <= 0 || containerHeight <= 0)
        {
            // Not laid out yet (e.g. opened from the command line before the window is shown).
            // Keep the image hidden until it can be sized, so it never flashes at the wrong size.
            MainImage.Visibility = Visibility.Hidden;
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
        MainImage.Visibility = Visibility.Visible;
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
        var videoFile = FindCompanionVideo(filePath);

        // Delete the image first: if that fails, nothing has been touched
        try
        {
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                filePath,
                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error deleting file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (videoFile != null)
        {
            try
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                    videoFile,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"The image was deleted, but its video could not be:\n{videoFile}\n\n{ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        _imageFiles.RemoveAt(_currentIndex);
        _currentIndex = Math.Min(_currentIndex, _imageFiles.Count - 1); // -1 when the list is empty
        DisplayCurrentImage();
    }

    private void RenameCurrentFile()
    {
        if (_currentIndex < 0 || _currentIndex >= _imageFiles.Count) return;

        var filePath = _imageFiles[_currentIndex];
        var directory = Path.GetDirectoryName(filePath);
        var currentName = Path.GetFileNameWithoutExtension(filePath);
        var imageExtension = Path.GetExtension(filePath);

        if (string.IsNullOrEmpty(directory)) return;

        var videoFile = FindCompanionVideo(filePath);

        // Show input dialog
        var newName = Microsoft.VisualBasic.Interaction.InputBox(
            "Enter new filename:",
            "Rename",
            currentName);

        // User cancelled, entered nothing, or changed nothing (a change of case alone still counts)
        if (string.IsNullOrWhiteSpace(newName) || newName == currentName) return;

        // Validate filename
        if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            MessageBox.Show("Invalid filename. Please avoid special characters.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var newImagePath = Path.Combine(directory, newName + imageExtension);
        var newVideoPath = videoFile != null
            ? Path.Combine(directory, newName + Path.GetExtension(videoFile))
            : null;

        // Check both targets before touching anything. Renaming to the same name with different
        // case is fine: on Windows the "existing" file is the one being renamed.
        static bool IsTaken(string target, string source) =>
            !string.Equals(target, source, StringComparison.OrdinalIgnoreCase)
            && (File.Exists(target) || Directory.Exists(target));

        if (IsTaken(newImagePath, filePath))
        {
            MessageBox.Show($"A file named '{Path.GetFileName(newImagePath)}' already exists.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (videoFile != null && newVideoPath != null && IsTaken(newVideoPath, videoFile))
        {
            MessageBox.Show($"A file named '{Path.GetFileName(newVideoPath)}' already exists.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        try
        {
            File.Move(filePath, newImagePath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error renaming file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (videoFile != null && newVideoPath != null)
        {
            try
            {
                File.Move(videoFile, newVideoPath);
            }
            catch (Exception videoEx)
            {
                // Put the image back so the pair keeps matching names
                try
                {
                    File.Move(newImagePath, filePath);
                    MessageBox.Show($"Could not rename the video, so nothing was renamed.\n\n{videoEx.Message}",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                catch
                {
                    MessageBox.Show($"The image was renamed, but its video could not be:\n{videoFile}\n\n{videoEx.Message}",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    newVideoPath = null;
                }
            }
        }

        _imageFiles[_currentIndex] = newImagePath;

        // Keep the collected clipboard list pointing at the files' new names
        if (_clipboardImageSet.Remove(filePath))
        {
            _clipboardImageSet.Add(newImagePath);
            for (int i = 0; i < _clipboardEntries.Count; i++)
            {
                if (string.Equals(_clipboardEntries[i], filePath, StringComparison.OrdinalIgnoreCase))
                    _clipboardEntries[i] = newImagePath;
                else if (newVideoPath != null && string.Equals(_clipboardEntries[i], videoFile, StringComparison.OrdinalIgnoreCase))
                    _clipboardEntries[i] = newVideoPath;
            }
            TrySetClipboardText(string.Join(Environment.NewLine, _clipboardEntries));
        }

        // Move the file to its new place in the sort order (it stays the current image)
        ResortFiles();
        Title = $"PicView - {Path.GetFileName(newImagePath)}";
    }

    private void AddToClipboardList()
    {
        if (_currentIndex < 0 || _currentIndex >= _imageFiles.Count) return;

        var imagePath = _imageFiles[_currentIndex];

        // Check if already in clipboard list
        if (_clipboardImageSet.Contains(imagePath)) return;

        var videoFile = FindCompanionVideo(imagePath);

        var newEntries = new List<string>(_clipboardEntries) { imagePath };
        if (videoFile != null) newEntries.Add(videoFile);

        // Only record it once the clipboard actually accepted it, so pressing C again retries
        if (!TrySetClipboardText(string.Join(Environment.NewLine, newEntries)))
        {
            Title = "PicView - Clipboard is in use by another app, press C to try again";
            return;
        }

        _clipboardImageSet.Add(imagePath);
        _clipboardEntries.Clear();
        _clipboardEntries.AddRange(newEntries);

        // Show feedback
        var count = _clipboardImageSet.Count;
        var videoStatus = videoFile != null ? " + video" : "";
        Title = $"PicView - Added to clipboard ({count} image{(count > 1 ? "s" : "")}){videoStatus}";
    }

    private void ClearClipboardList()
    {
        _clipboardImageSet.Clear();
        _clipboardEntries.Clear();

        if (!TrySetClipboardText(""))
        {
            Title = "PicView - List cleared, but the clipboard is in use by another app";
            return;
        }

        // Restore normal title
        if (_currentIndex >= 0 && _currentIndex < _imageFiles.Count)
        {
            Title = $"PicView - {Path.GetFileName(_imageFiles[_currentIndex])}";
        }
        else
        {
            Title = "PicView";
        }
    }

    private void OpenCorrespondingVideo()
    {
        if (_currentIndex < 0 || _currentIndex >= _imageFiles.Count) return;

        var videoFile = FindCompanionVideo(_imageFiles[_currentIndex]);
        if (videoFile == null)
        {
            Title = "PicView - No video for this image";
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = videoFile,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open the video: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
            case Key.C:
                AddToClipboardList();
                break;
            case Key.X:
                ClearClipboardList();
                break;
            case Key.Enter:
                OpenCorrespondingVideo();
                e.Handled = true;
                break;
            case Key.Escape:
                FitImageToWindow();
                break;
            case Key.F11:
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                break;
            case Key.F5:
                RefreshFiles();
                break;
        }
    }

    private void SortLabel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Single-click to toggle sort mode
        _currentSortMode = _currentSortMode == SortMode.NameAscending
            ? SortMode.DateModifiedDescending
            : SortMode.NameAscending;

        // Save the preference
        SaveSettings();

        ResortFiles();
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
                var firstImage = files.FirstOrDefault(IsSupportedImage);

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