using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace AsterismApp;

public sealed class NoteImageService
{
    public async Task<string> SavePngAsync(string vaultPath, RandomAccessStreamReference bitmap)
    {
        var root = Path.GetFullPath(vaultPath);
        var attachmentPath = Path.Combine(root, "attachments");
        Directory.CreateDirectory(attachmentPath);

        var folder = await StorageFolder.GetFolderFromPathAsync(attachmentPath);
        var requestedName = $"image-{DateTime.Now:yyyyMMdd-HHmmssfff}.png";
        var file = await folder.CreateFileAsync(requestedName, CreationCollisionOption.GenerateUniqueName);

        using var input = await bitmap.OpenReadAsync();
        var decoder = await BitmapDecoder.CreateAsync(input);
        var pixelData = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            new BitmapTransform(),
            ExifOrientationMode.RespectExifOrientation,
            ColorManagementMode.ColorManageToSRgb);

        using var output = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
        encoder.SetPixelData(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            decoder.OrientedPixelWidth,
            decoder.OrientedPixelHeight,
            decoder.DpiX,
            decoder.DpiY,
            pixelData.DetachPixelData());
        await encoder.FlushAsync();

        return Path.GetRelativePath(root, file.Path).Replace(Path.DirectorySeparatorChar, '/');
    }
}
