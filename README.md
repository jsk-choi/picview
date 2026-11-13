# PicView

A lightweight Windows photo viewer that replicates the classic Windows PicView experience with modern enhancements.

## Features

- **Click to Open**: Double-click any image or pass it as a command-line argument
- **Directory Loading**: Automatically loads all images in the same directory
- **Correct Orientation**: Phone and camera photos are shown upright, following their EXIF orientation tag
- **Instant Next/Previous**: The images on either side of the current one are decoded in the background, so stepping through a folder doesn't wait on loading
- **Smooth Zooming**: Mouse wheel zoom centered on cursor position
- **Pan/Drag**: Click and drag to move the image when zoomed in
- **Keyboard Navigation**: Full keyboard support for navigation and zoom
- **Drag & Drop**: Drop images directly onto the window
- **Sorting**: Natural name order (same as Explorer) or newest-first by date modified; your choice is remembered
- **Delete to Recycle Bin**: Delete images with the Del key (sends to Recycle Bin)
- **Rename**: Rename the current image in place with F2
- **Companion Videos**: Pairs each image with a video of the same name (e.g. `IMG_0001.jpg` + `IMG_0001.mp4`, as with Live Photos) — play it, and it follows the image on delete, rename, and collect
- **Collect to Clipboard**: Build up a list of file paths on the clipboard as you browse
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
| `F` / `Escape` | Fit to window |
| `O` / `Ctrl+O` | Open file dialog |
| `F2` | Rename current image (and its companion video) |
| `Delete` | Delete current image (and its companion video) to the Recycle Bin — no confirmation |
| `Enter` | Play the companion video in its default app |
| `C` | Collect: add the current image's path (and its video's path) to the clipboard list |
| `X` | Clear the collected list **and the system clipboard** |
| `F5` | Re-scan the folder for added/removed images |
| `F11` | Toggle maximize |

## Mouse Controls

- **Scroll Wheel**: Zoom in/out (centered on cursor)
- **Left Click + Drag**: Pan the image when zoomed
- **◀ / ▶ Buttons**: Previous / next image
- **Sort Label** (top right): Click to switch between Name (A–Z) and Date (Newest)
- **Drop Files**: Drop an image to open it

## Companion Videos

A companion video is a video file in the same folder with exactly the same base name as the image: `IMG_0001.jpg` pairs with `IMG_0001.mp4`, but not with `IMG_0001.edited.mp4` or sidecars like `IMG_0001.xmp`. Recognized extensions, in order of preference when several exist: `.mp4`, `.mov`, `.m4v`, `.mkv`, `.avi`, `.wmv`, `.webm`, `.3gp`, `.mts`, `.m2ts`.

Rename changes both files or neither. Delete removes the image first, then the video.

## Collecting Paths

Each press of `C` appends the current image's full path — plus its companion video's path, if any — to a list, and copies the whole list to the clipboard, one path per line. Pressing `C` again on an image already collected does nothing. `X` empties the list and clears the clipboard. The list only lasts until PicView closes.

## Settings

The sort order is saved to `%LOCALAPPDATA%\PicView\settings.json`.

## Building

Requires .NET 10 SDK.

```bash
cd PicView
dotnet build -c Release
```

The executable will be at: `PicView\bin\Release\net10.0-windows\PicView.exe`

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

WebP needs the WebP Image Extensions from the Microsoft Store if Windows doesn't already include them. Animated GIFs show their first frame only. Files that can't be decoded show an error in place of the image, and you can keep navigating past them.

## Publishing as Single Executable

To create a single .exe file:

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

The single executable will be at: `PicView\bin\Release\net10.0-windows\win-x64\publish\PicView.exe`
