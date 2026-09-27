namespace Finance.Cli;

public static class FileHelper
{
    public static string GetResultsPathForFile(string fileName, string extension)
    {
        var resultsDir = Path.Combine(AppContext.BaseDirectory, "Results");
        Directory.CreateDirectory(resultsDir);

        var filePath = Path.Combine(resultsDir, $"{fileName}.{extension}");
        return filePath;
    }

    public static string GetInputPathForFile(string fileName, string bankFolder, string extension)
    {
        var inputDir = Path.Combine(AppContext.BaseDirectory, $"Files/{bankFolder}");
        Directory.CreateDirectory(inputDir);

        var filePath = Path.Combine(inputDir, $"{fileName}.{extension}");
        return filePath;
    }
}