namespace Klang.Core;

/// <summary>Enkel loggfil i %AppData%\Klang\klang.log, for feilsøking.</summary>
public static class Log
{
    private static readonly object Gate = new();
    public static string FilePath { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Klang", "klang.log");

    public static void Info(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                var fi = new FileInfo(FilePath);
                if (fi.Exists && fi.Length > 1_000_000) File.Delete(FilePath);
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
        }
        catch { /* Logging skal aldri stoppe appen. */ }
    }
}
