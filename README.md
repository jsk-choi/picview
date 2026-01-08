# PicView

A lightweight Windows photo viewer that replicates the classic Windows PicView experience with modern enhancements.

## Features

- **Click to Open**: Double-click any image or pass it as a command-line argument
- **Directory Loading**: Automatically loads all images in the same directory
- **Smooth Zooming**: Mouse wheel zoom centered on cursor position
- **Pan/Drag**: Click and drag to move the image when zoomed in
- **Keyboard Navigation**: Full keyboard support for navigation and zoom
- **Drag & Drop**: Drop images directly onto the window
- **Delete to Recycle Bin**: Delete images with the Del key (sends to Recycle Bin)
- **Dark Theme**: Modern dark UI that doesn't distract from your images

## Keyboard Shortcuts

| Key | Action |
|-----|--------|
| `←` / `Backspace` | Previous image |
| `→` / `Space` | Next image |
| `Home` | First image |
| `End` | Last image |
| `+` / `=` | Zoom in |
| `-` | Zoom out |
| `1` | Actual size (100%) |
| `F` | Fit to window |
| `O` | Open file dialog |
| `Delete` | Delete current image |
| `Escape` | Reset view / Close |
| `F11` | Toggle maximize |

## Mouse Controls

- **Scroll Wheel**: Zoom in/out (centered on cursor)
- **Left Click + Drag**: Pan the image when zoomed
- **Drop Files**: Drop an image to open it

## Building

Requires .NET 8 SDK.

```bash
cd PicView
dotnet build -c Release
```

The executable will be at: `PicView\bin\Release\net8.0-windows\PicView.exe`

## Usage

### From Command Line
```bash
PicView.exe "C:\path\to\image.jpg"
```

### Setting as Default Viewer

**Option 1: Windows Settings**
1. Right-click any image file
2. Select "Open with" → "Choose another app"
3. Browse to `PicView.exe`
4. Check "Always use this app"

**Option 2: Run Registration Script (Admin)**
1. Build the project in Release mode
2. Right-click `RegisterFileAssociations.bat` → "Run as administrator"
3. The app will appear in "Open with" menus

## Supported Formats

- JPEG (.jpg, .jpeg)
- PNG (.png)
- GIF (.gif)
- BMP (.bmp)
- WebP (.webp)
- TIFF (.tiff, .tif)
- ICO (.ico)

## Publishing as Single Executable

To create a single .exe file:

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

The single executable will be at: `PicView\bin\Release\net8.0-windows\win-x64\publish\PicView.exe`
