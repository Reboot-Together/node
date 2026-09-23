namespace AsterismApp;

public static class PdfReaderRules
{
    public static bool IsPdf(string? path) =>
        !string.IsNullOrWhiteSpace(path)
        && string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase);

    public static Uri FileUri(string path)
    {
        if (!IsPdf(path)) throw new ArgumentException("PDF 파일만 열 수 있습니다.", nameof(path));
        return new Uri(Path.GetFullPath(path));
    }
}
