using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia.Platform.Storage;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

namespace IconPull;

internal static class SupportedFileTypes
{
    public static readonly string[] Extensions = [".exe", ".dll", ".ico", ".cpl", ".scr", ".ocx", ".icl", ".mun"];

    public static FilePickerFileType PickerFilter { get; } = new("Icon sources")
    {
        Patterns = Array.ConvertAll(Extensions, ext => "*" + ext)
    };

    public static bool IsPeExtension(string ext) =>
        ext is ".exe" or ".dll" or ".cpl" or ".scr" or ".ocx" or ".icl" or ".mun";

    public static bool IsSupported(string path) =>
        Array.Exists(Extensions, ext => string.Equals(ext, Path.GetExtension(path), StringComparison.OrdinalIgnoreCase));
}

public partial class MainWindow : Window
{
    private const int MaxLogLength = 50_000;

    private string? _filePath;
    private bool _isExtracting = false;
    private int _previewVersion;
    private int _extractVersion;

    public MainWindow()
    {
        InitializeComponent();
        Title = $"IconPull v{GetAppVersion()}";

        string defaultOut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "IconPull Output");
        OutputEdit.Text = defaultOut;

        DropZoneBorder.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        DropZoneBorder.AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        DropZoneBorder.AddHandler(DragDrop.DropEvent, OnDrop);
        Opened += (_, _) => FitToWorkArea();
        ScalingChanged += (_, _) => FitToWorkArea();
        UpdateStatus();
    }

    private static string GetAppVersion()
    {
        string version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "1.0.0";
        int plus = version.IndexOf('+');
        return plus >= 0 ? version[..plus] : version;
    }

    private void FitToWorkArea()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null) return;

        double scale = screen.Scaling;
        double maxWidth = Math.Max(0, screen.WorkingArea.Width / scale - 24);
        double maxHeight = Math.Max(0, screen.WorkingArea.Height / scale - 24);

        if (Width > maxWidth) Width = maxWidth;
        if (Height > maxHeight) Height = maxHeight;
    }

    private static bool HasFileFormat(IDataTransfer dataTransfer)
    {
        foreach (var format in dataTransfer.Formats)
        {
            if (format == DataFormat.File)
            {
                return true;
            }
        }

        return false;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (HasFileFormat(e.DataTransfer))
        {
            var files = e.DataTransfer.TryGetFiles();
            if (files is not null)
            {
                bool accepted = false;
                foreach (var f in files)
                {
                    if (f is Avalonia.Platform.Storage.IStorageFile && SupportedFileTypes.IsSupported(f.Path.LocalPath))
                    {
                        SetFile(f.Path.LocalPath);
                        accepted = true;
                        break;
                    }
                }

                if (!accepted)
                {
                    StatusText.Text = "Unsupported file type.";
                }
            }
            ResetDropZoneStyle();
        }
    }

    private async void OnChooseFileClicked(object? sender, RoutedEventArgs e)
    {
        try
        {
            var result = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                AllowMultiple = false,
                Title = "Choose file",
                FileTypeFilter = [SupportedFileTypes.PickerFilter]
            });

            if (result.Count > 0)
            {
                SetFile(result[0].Path.LocalPath);
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = "Could not choose a file.";
            Log($"File picker failed: {ex.Message}");
        }
    }

    private void SetFile(string path)
    {
        if (!SupportedFileTypes.IsSupported(path)) return;

        if (!File.Exists(path))
        {
            StatusText.Text = "File not found.";
            Log($"File not found: {path}");
            return;
        }

        _filePath = path;
        int previewVersion = ++_previewVersion;
        string fileName = Path.GetFileName(path);
        FilePathText.Text = fileName;
        ToolTip.SetTip(FilePathText, path);
        ClearPreview();
        FileDisplay.IsVisible = true;
        EmptyState.IsVisible = false;
        UpdateStatus();
        _ = LoadDropZonePreviewAsync(path, previewVersion);
    }

    private void OnSizeChanged(object? sender, SelectionChangedEventArgs e) => RefreshPreview();

    private void OnScaleupChanged(object? sender, RoutedEventArgs e) => RefreshPreview();

    private void RefreshPreview()
    {
        if (_filePath is not { } path) return;

        int previewVersion = ++_previewVersion;
        _ = LoadDropZonePreviewAsync(path, previewVersion);
    }

    private async Task LoadDropZonePreviewAsync(string path, int previewVersion)
    {
        if (!int.TryParse((SizeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString(), out int size))
        {
            return;
        }

        bool allowScale = ScaleupCheck.IsChecked == true;

        try
        {
            var bmp = await Task.Run(() => WindowsIconExtractor.ExtractShellIcon(path, size, allowScale));
            if (_filePath == path && _previewVersion == previewVersion)
            {
                SetPreview(bmp);
            }
            else
            {
                bmp.Dispose();
            }
        }
        catch (Exception ex)
        {
            if (_filePath == path && _previewVersion == previewVersion &&
                string.Equals(Path.GetExtension(path), ".ico", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var bmp = new Avalonia.Media.Imaging.Bitmap(path);
                    if (_filePath == path && _previewVersion == previewVersion)
                    {
                        SetPreview(bmp);
                    }
                    else
                    {
                        bmp.Dispose();
                    }
                }
                catch (Exception fallbackEx)
                {
                    Log($"Preview failed: {fallbackEx.Message}");
                }
            }
            else if (_filePath == path && _previewVersion == previewVersion)
            {
                Log($"Preview failed: {ex.Message}");
            }
        }
    }

    private void OnClearClicked(object? sender, RoutedEventArgs e)
    {
        _filePath = null;
        _previewVersion++;
        _extractVersion++;
        FilePathText.Text = "";
        FileDisplay.IsVisible = false;
        EmptyState.IsVisible = true;
        ClearPreview();
        UpdateStatus();
    }
    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (HasFileFormat(e.DataTransfer) && HasSupportedDropFile(e))
        {
            e.DragEffects = DragDropEffects.Copy;
            DropZoneBorder.Background = new SolidColorBrush(Color.Parse("#eff6ff"));
            DropZoneBorder.BorderBrush = new SolidColorBrush(Color.Parse("#60a5fa"));
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    private static bool HasSupportedDropFile(DragEventArgs e)
    {
        var files = e.DataTransfer.TryGetFiles();
        if (files is null) return false;

        foreach (var file in files)
        {
            if (file is Avalonia.Platform.Storage.IStorageFile storageFile &&
                SupportedFileTypes.IsSupported(storageFile.Path.LocalPath))
            {
                return true;
            }
        }

        return false;
    }

    private void OnDragLeave(object? sender, DragEventArgs e) => ResetDropZoneStyle();

    private void ResetDropZoneStyle()
    {
        DropZoneBorder.Background = new SolidColorBrush(Color.Parse("#f8fafc"));
        DropZoneBorder.BorderBrush = new SolidColorBrush(Color.Parse("#c5d0e0"));
    }

    private async void OnBrowseClicked(object? sender, RoutedEventArgs e)
    {
        try
        {
            var result = await StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions());
            if (result.Count > 0) OutputEdit.Text = result[0].Path.LocalPath;
        }
        catch (Exception ex)
        {
            StatusText.Text = "Could not choose an output folder.";
            Log($"Folder picker failed: {ex.Message}");
        }
    }

    private void OnOpenFolderClicked(object? sender, RoutedEventArgs e)
    {
        string folder = OutputEdit.Text ?? "";
        if (!Directory.Exists(folder))
        {
            StatusText.Text = "Output folder does not exist.";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusText.Text = "Could not open folder.";
            Log($"Open folder failed: {ex.Message}");
        }
    }

    private void OnClearLogClicked(object? sender, RoutedEventArgs e) => LogTextBox.Text = "";

    private void UpdateStatus()
    {
        StatusText.Text = _filePath is null
            ? "Ready. Drop a file or click Choose file."
            : Path.GetFileName(_filePath);
        ExtractButton.IsEnabled = !_isExtracting && _filePath is not null;
    }

    private void ShowPreview(string path)
    {
        try
        {
            SetPreview(new Avalonia.Media.Imaging.Bitmap(path));
        }
        catch (Exception ex)
        {
            Log($"Output preview failed: {ex.Message}");
        }
    }

    private void SetPreview(Avalonia.Media.Imaging.Bitmap bitmap)
    {
        ClearPreview();
        DropZonePreviewImage.Source = bitmap;
    }

    private void ClearPreview()
    {
        if (DropZonePreviewImage.Source is IDisposable disposable)
        {
            disposable.Dispose();
        }

        DropZonePreviewImage.Source = null;
    }

    private void Log(string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            LogTextBox.Text += message + Environment.NewLine;
            if (LogTextBox.Text.Length > MaxLogLength)
            {
                LogTextBox.Text = LogTextBox.Text[^MaxLogLength..];
            }

            LogTextBox.CaretIndex = LogTextBox.Text.Length;
        });
    }

    private async void OnExtractClicked(object? sender, RoutedEventArgs e)
    {
        if (_isExtracting || _filePath is null) return;

        string path = _filePath;
        string outDir = OutputEdit.Text ?? "";
        if (string.IsNullOrWhiteSpace(outDir))
        {
            StatusText.Text = "Choose an output folder.";
            return;
        }

        if (!int.TryParse((SizeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString(), out int size))
        {
            StatusText.Text = "Select a valid icon size.";
            return;
        }

        bool doShell = ShellCheck.IsChecked == true;
        bool doRawIco = RawIcoCheck.IsChecked == true;
        bool doRawPng = RawPngCheck.IsChecked == true;
        bool allowScale = ScaleupCheck.IsChecked == true;

        if (!doShell && !doRawIco && !doRawPng)
        {
            StatusText.Text = "Select at least one export format.";
            return;
        }

        int extractVersion = ++_extractVersion;
        _isExtracting = true;
        ExtractButton.IsEnabled = false;
        StatusText.Text = "Extracting...";
        Log($"\nOutput folder: {outDir}");
        Log($"\n{path}");

        try
        {
            Directory.CreateDirectory(outDir);

            var result = await Task.Run(() =>
            {
                string baseName = GetSafeBaseName(path);
                string? previewPath = null;
                int attempted = 0;
                int succeeded = 0;

                if (doShell)
                {
                    attempted++;
                    string outShell = Path.Combine(outDir, $"{baseName}.shell-{size}.png");
                    try
                    {
                        using var bmp = WindowsIconExtractor.ExtractShellIcon(path, size, allowScale);
                        bmp.Save(outShell);
                        succeeded++;
                        Log($"  Shell PNG: {Path.GetFileName(outShell)}");
                        previewPath ??= outShell;
                    }
                    catch (Exception ex) { Log($"  Shell PNG failed: {ex.Message}"); }
                }

                string ext = Path.GetExtension(path).ToLowerInvariant();
                bool isPe = SupportedFileTypes.IsPeExtension(ext);
                byte[]? icoBytes = null;

                if (isPe && (doRawIco || doRawPng))
                {
                    try
                    {
                        icoBytes = WindowsIconExtractor.ExtractRawIcoFromPE(path);
                    }
                    catch (Exception ex)
                    {
                        if (doRawIco)
                        {
                            attempted++;
                            Log($"  Raw ICO failed: {ex.Message}");
                        }

                        if (doRawPng)
                        {
                            attempted++;
                            Log($"  Raw best PNG failed: {ex.Message}");
                        }
                    }
                }
                else if (ext == ".ico" && (doRawIco || doRawPng))
                {
                    try
                    {
                        icoBytes = File.ReadAllBytes(path);
                    }
                    catch (Exception ex)
                    {
                        if (doRawIco)
                        {
                            attempted++;
                            Log($"  Raw ICO failed: {ex.Message}");
                        }

                        if (doRawPng)
                        {
                            attempted++;
                            Log($"  ICO best PNG failed: {ex.Message}");
                        }
                    }
                }

                if (icoBytes is not null)
                {
                    if (doRawIco)
                    {
                        attempted++;
                        string outIco = Path.Combine(outDir, $"{baseName}.raw.ico");
                        try
                        {
                            File.WriteAllBytes(outIco, icoBytes);
                            succeeded++;
                            Log($"  Raw ICO: {Path.GetFileName(outIco)}");
                        }
                        catch (Exception ex) { Log($"  Raw ICO failed: {ex.Message}"); }
                    }

                    if (doRawPng)
                    {
                        attempted++;
                        string outPng = Path.Combine(outDir, $"{baseName}.raw-best.png");
                        try
                        {
                            SaveBestPngFromIcoBytes(icoBytes, outPng);
                            succeeded++;
                            Log($"  Raw best PNG: {Path.GetFileName(outPng)}");
                            previewPath ??= outPng;
                        }
                        catch (Exception ex) { Log($"  Raw best PNG failed: {ex.Message}"); }
                    }
                }

                return (attempted, succeeded, previewPath);
            });

            if (_extractVersion == extractVersion && _filePath == path)
            {
                string summary = $"{result.succeeded}/{result.attempted} exports succeeded.";
                StatusText.Text = summary;
                Log($"\n{summary}");

                if (result.previewPath is not null)
                {
                    ShowPreview(result.previewPath);
                }
            }
        }
        catch (Exception ex)
        {
            if (_extractVersion == extractVersion && _filePath == path)
            {
                StatusText.Text = "Extraction failed.";
                Log($"\nExtraction failed: {ex.Message}");
            }
        }
        finally
        {
            _isExtracting = false;
            ExtractButton.IsEnabled = _filePath is not null;
        }
    }

    private string GetSafeBaseName(string path)
    {
        string stem = Path.GetFileNameWithoutExtension(path);
        foreach (char c in Path.GetInvalidFileNameChars()) stem = stem.Replace(c, '_');
        using var sha = SHA1.Create();
        byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(path));
        string digest = BitConverter.ToString(hash).Replace("-", "").Substring(0, 8).ToLower();
        return $"{stem}_{digest}";
    }

    private void SaveBestPngFromIcoBytes(byte[] icoBytes, string outPath)
    {
        using var ms = new MemoryStream(WindowsIconExtractor.ExtractBestIcoFrame(icoBytes));
        using var bmp = new Avalonia.Media.Imaging.Bitmap(ms);
        bmp.Save(outPath);
    }
}

internal static class WindowsIconExtractor
{
    [StructLayout(LayoutKind.Sequential)]
    public struct SIZE { public int cx; public int cy; }

    [ComImport, Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage([In] SIZE size, [In] int flags, [Out] out IntPtr phbm);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    public static extern void SHCreateItemFromParsingName(string pszPath, IntPtr pbc, [In] ref Guid riid, [Out, MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    public static extern int GetObject(IntPtr hgdiobj, int cbBuffer, ref BITMAP lpvObject);

    [DllImport("gdi32.dll")]
    public static extern int GetDIBits(IntPtr hdc, IntPtr hbmp, uint uStartScan, uint cScanLines, IntPtr lpvBits, ref BITMAPINFO lpbi, uint uUsage);

    [DllImport("user32.dll")]
    public static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAP { public int bmType, bmWidth, bmHeight, bmWidthBytes; public ushort bmPlanes, bmBitsPixel; public IntPtr bmBits; }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER { public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount; public uint biCompression, biSizeImage; public int biXPelsPerMeter, biYPelsPerMeter; public uint biClrUsed, biClrImportant; }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFO { public BITMAPINFOHEADER bmiHeader; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)] public uint[] bmiColors; }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern IntPtr LoadLibraryEx(string lpFileName, IntPtr hFile, uint dwFlags);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool FreeLibrary(IntPtr hModule);

    delegate bool EnumResNameProc(IntPtr hModule, IntPtr lpszType, IntPtr lpszName, IntPtr lParam);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool EnumResourceNames(IntPtr hModule, IntPtr lpszType, EnumResNameProc lpEnumFunc, IntPtr lParam);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr FindResource(IntPtr hModule, IntPtr lpName, IntPtr lpType);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr LoadResource(IntPtr hModule, IntPtr hResInfo);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr LockResource(IntPtr hResData);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern uint SizeofResource(IntPtr hModule, IntPtr hResInfo);

    private const int SIIGBF_BIGGERSIZEOK = 0x0001;
    private const int SIIGBF_ICONONLY = 0x0004;
    private const int SIIGBF_SCALEUP = 0x0100;
    private const uint LOAD_LIBRARY_AS_DATAFILE = 0x00000002;

    public static Avalonia.Media.Imaging.WriteableBitmap ExtractShellIcon(string path, int size, bool allowScaleup)
    {
        Guid iid = new Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B");
        SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out IShellItemImageFactory factory);

        int flags = SIIGBF_ICONONLY | SIIGBF_BIGGERSIZEOK;
        if (allowScaleup) flags |= SIIGBF_SCALEUP;

        int hr = factory.GetImage(new SIZE { cx = size, cy = size }, flags, out IntPtr hBitmap);
        if (hr < 0 || hBitmap == IntPtr.Zero) throw new Exception($"Shell get image failed: HRESULT 0x{hr:X8}");

        try
        {
            BITMAP bmp = new();
            if (GetObject(hBitmap, Marshal.SizeOf<BITMAP>(), ref bmp) == 0)
            {
                throw new Exception("Failed to read bitmap information.");
            }

            if (bmp.bmWidth <= 0 || bmp.bmHeight <= 0) throw new Exception("Invalid bitmap size.");

            var writeableBmp = new Avalonia.Media.Imaging.WriteableBitmap(
                new Avalonia.PixelSize(bmp.bmWidth, bmp.bmHeight),
                new Avalonia.Vector(96, 96),
                Avalonia.Platform.PixelFormat.Bgra8888,
                Avalonia.Platform.AlphaFormat.Unpremul);

            using (var frameBuffer = writeableBmp.Lock())
            {
                BITMAPINFO bmi = new();
                bmi.bmiHeader.biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>();
                bmi.bmiHeader.biWidth = bmp.bmWidth;
                bmi.bmiHeader.biHeight = -bmp.bmHeight;
                bmi.bmiHeader.biPlanes = 1;
                bmi.bmiHeader.biBitCount = 32;
                bmi.bmiHeader.biCompression = 0;

                IntPtr hdc = GetDC(IntPtr.Zero);
                try
                {
                    if (hdc == IntPtr.Zero)
                    {
                        throw new Exception("Failed to acquire a device context.");
                    }

                    int scanLines = GetDIBits(hdc, hBitmap, 0, (uint)bmp.bmHeight, frameBuffer.Address, ref bmi, 0);
                    if (scanLines != bmp.bmHeight)
                    {
                        throw new Exception("Failed to read bitmap pixels.");
                    }

                    UndoPremultiplication(frameBuffer.Address, bmp.bmWidth, bmp.bmHeight);
                }
                finally
                {
                    ReleaseDC(IntPtr.Zero, hdc);
                }
            }
            return writeableBmp;
        }
        finally
        {
            DeleteObject(hBitmap);
            Marshal.ReleaseComObject(factory);
        }
    }

    private static unsafe void UndoPremultiplication(IntPtr ptr, int width, int height)
    {
        byte* data = (byte*)ptr.ToPointer();
        int len = width * height * 4;
        byte maxAlpha = 0;

        for (int i = 3; i < len; i += 4)
        {
            if (data[i] > maxAlpha) maxAlpha = data[i];
        }

        if (maxAlpha > 0)
        {
            for (int i = 0; i < len; i += 4)
            {
                byte a = data[i + 3];
                if (a > 0 && a < 255)
                {
                    data[i] = (byte)Math.Min(255, (data[i] * 255 + a / 2) / a);
                    data[i + 1] = (byte)Math.Min(255, (data[i + 1] * 255 + a / 2) / a);
                    data[i + 2] = (byte)Math.Min(255, (data[i + 2] * 255 + a / 2) / a);
                }
            }
        }
        else
        {
            for (int i = 3; i < len; i += 4) data[i] = 255;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct GRPICONDIR { public ushort idReserved; public ushort idType; public ushort idCount; }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct GRPICONDIRENTRY { public byte bWidth, bHeight, bColorCount, bReserved; public ushort wPlanes, wBitCount; public uint dwBytesInRes; public ushort nId; }

    public static byte[] ExtractRawIcoFromPE(string path)
    {
        IntPtr hMod = LoadLibraryEx(path, IntPtr.Zero, LOAD_LIBRARY_AS_DATAFILE);
        if (hMod == IntPtr.Zero) throw new Exception("Failed to load library.");

        try
        {
            IntPtr bestGroupRes = IntPtr.Zero;
            uint bestGroupSize = 0;
            long bestScore = -1;

            EnumResourceNames(hMod, (IntPtr)14, (hModule, lpszType, lpszName, lParam) =>
            {
                IntPtr hResInfo = FindResource(hModule, lpszName, (IntPtr)14);
                if (hResInfo != IntPtr.Zero)
                {
                    IntPtr hResData = LoadResource(hModule, hResInfo);
                    if (hResData != IntPtr.Zero)
                    {
                        IntPtr pData = LockResource(hResData);
                        uint size = SizeofResource(hModule, hResInfo);
                        long score = EvaluateIconGroup(pData, size);
                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestGroupRes = pData;
                            bestGroupSize = size;
                        }
                    }
                }
                return true;
            }, IntPtr.Zero);

            if (bestGroupRes == IntPtr.Zero) throw new Exception("No icon groups found.");
            return BuildIcoFromGroup(hMod, bestGroupRes, bestGroupSize);
        }
        finally
        {
            FreeLibrary(hMod);
        }
    }

    private static unsafe long EvaluateIconGroup(IntPtr pData, uint resourceSize)
    {
        uint headerSize = (uint)sizeof(GRPICONDIR);
        if (pData == IntPtr.Zero || resourceSize < headerSize) return -1;

        GRPICONDIR* header = (GRPICONDIR*)pData;
        if (header->idReserved != 0 || header->idType != 1) return -1;

        ulong entriesSize = (ulong)sizeof(GRPICONDIRENTRY) * header->idCount;
        if (header->idCount == 0 || (ulong)resourceSize < headerSize + entriesSize) return -1;

        GRPICONDIRENTRY* entries = (GRPICONDIRENTRY*)(pData + sizeof(GRPICONDIR));
        long bestScore = 0;
        for (int i = 0; i < header->idCount; i++)
        {
            int w = entries[i].bWidth == 0 ? 256 : entries[i].bWidth;
            int h = entries[i].bHeight == 0 ? 256 : entries[i].bHeight;
            long score = (w * h) + entries[i].wBitCount + entries[i].dwBytesInRes;
            if (score > bestScore) bestScore = score;
        }
        return bestScore;
    }

    private static unsafe byte[] BuildIcoFromGroup(IntPtr hMod, IntPtr pGroupData, uint groupSize)
    {
        uint headerSize = (uint)sizeof(GRPICONDIR);
        if (pGroupData == IntPtr.Zero || groupSize < headerSize)
        {
            throw new InvalidDataException("Invalid icon group resource.");
        }

        GRPICONDIR* header = (GRPICONDIR*)pGroupData;
        int count = header->idCount;
        ulong entriesSize = (ulong)sizeof(GRPICONDIRENTRY) * header->idCount;
        if (header->idReserved != 0 || header->idType != 1 || count == 0 ||
            (ulong)groupSize < headerSize + entriesSize)
        {
            throw new InvalidDataException("Invalid icon group resource.");
        }

        GRPICONDIRENTRY* entries = (GRPICONDIRENTRY*)(pGroupData + sizeof(GRPICONDIR));

        using MemoryStream ms = new MemoryStream();
        using BinaryWriter bw = new BinaryWriter(ms);

        bw.Write((ushort)0);
        bw.Write((ushort)1);
        bw.Write((ushort)count);

        uint imageOffset = (uint)(6 + (16 * count));
        List<byte[]> blobs = new();

        for (int i = 0; i < count; i++)
        {
            IntPtr hResInfo = FindResource(hMod, (IntPtr)entries[i].nId, (IntPtr)3);
            if (hResInfo == IntPtr.Zero)
            {
                throw new InvalidDataException($"Missing icon resource {entries[i].nId}.");
            }

            IntPtr hResData = LoadResource(hMod, hResInfo);
            if (hResData == IntPtr.Zero)
            {
                throw new InvalidDataException($"Could not load icon resource {entries[i].nId}.");
            }

            IntPtr pData = LockResource(hResData);
            uint size = SizeofResource(hMod, hResInfo);
            if (pData == IntPtr.Zero || size == 0 || size > int.MaxValue ||
                size > uint.MaxValue - imageOffset)
            {
                throw new InvalidDataException($"Invalid icon resource {entries[i].nId}.");
            }

            byte[] blob = new byte[(int)size];
            Marshal.Copy(pData, blob, 0, (int)size);
            blobs.Add(blob);

            bw.Write(entries[i].bWidth);
            bw.Write(entries[i].bHeight);
            bw.Write(entries[i].bColorCount);
            bw.Write((byte)0);
            bw.Write(entries[i].wPlanes);
            bw.Write(entries[i].wBitCount);
            bw.Write(size);
            bw.Write(imageOffset);
            imageOffset += size;
        }

        foreach (var blob in blobs) bw.Write(blob);
        return ms.ToArray();
    }

    private struct IcoEntry
    {
        public byte Width;
        public byte Height;
        public byte ColorCount;
        public ushort Planes;
        public ushort BitCount;
        public uint Size;
        public uint Offset;
    }

    public static byte[] ExtractBestIcoFrame(byte[] icoBytes)
    {
        using var input = new MemoryStream(icoBytes, writable: false);
        using var reader = new BinaryReader(input);

        if (input.Length < 6 || reader.ReadUInt16() != 0 || reader.ReadUInt16() != 1)
        {
            throw new InvalidDataException("Invalid ICO header.");
        }

        ushort count = reader.ReadUInt16();
        if (count == 0) throw new InvalidDataException("ICO contains no images.");

        IcoEntry best = default;
        long bestArea = -1;
        ushort bestBitCount = 0;
        uint bestSize = 0;

        for (int i = 0; i < count; i++)
        {
            if (input.Length - input.Position < 16)
            {
                throw new InvalidDataException("ICO directory is truncated.");
            }

            IcoEntry entry = new()
            {
                Width = reader.ReadByte(),
                Height = reader.ReadByte(),
                ColorCount = reader.ReadByte()
            };
            reader.ReadByte();
            entry.Planes = reader.ReadUInt16();
            entry.BitCount = reader.ReadUInt16();
            entry.Size = reader.ReadUInt32();
            entry.Offset = reader.ReadUInt32();

            if (entry.Size == 0 || entry.Offset > icoBytes.Length ||
                entry.Size > icoBytes.Length - entry.Offset)
            {
                throw new InvalidDataException("ICO image data is invalid.");
            }

            int width = entry.Width == 0 ? 256 : entry.Width;
            int height = entry.Height == 0 ? 256 : entry.Height;
            long area = (long)width * height;
            if (area > bestArea ||
                area == bestArea && (entry.BitCount > bestBitCount ||
                entry.BitCount == bestBitCount && entry.Size > bestSize))
            {
                best = entry;
                bestArea = area;
                bestBitCount = entry.BitCount;
                bestSize = entry.Size;
            }
        }

        if (best.Size > int.MaxValue - 22)
        {
            throw new InvalidDataException("ICO image is too large.");
        }

        using var output = new MemoryStream(22 + (int)best.Size);
        using var writer = new BinaryWriter(output);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)1);
        writer.Write(best.Width);
        writer.Write(best.Height);
        writer.Write(best.ColorCount);
        writer.Write((byte)0);
        writer.Write(best.Planes);
        writer.Write(best.BitCount);
        writer.Write(best.Size);
        writer.Write((uint)22);
        writer.Write(icoBytes, (int)best.Offset, (int)best.Size);
        return output.ToArray();
    }
}
