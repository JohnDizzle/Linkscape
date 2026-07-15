using Microsoft.Data.Sqlite;
using System.IO;

public static class LinkScapeCachePaths
{
    private const string CacheFolderName = "LinkScapeCache";

    public static string CacheDirectory
    {
        get
        {
            var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var basePath = string.IsNullOrWhiteSpace(documentsPath)
                ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
                : documentsPath;
            var cacheDirectory = Path.Combine(basePath, CacheFolderName);

            Directory.CreateDirectory(cacheDirectory);
            return cacheDirectory;
        }
    }

    public static string GetDatabaseConnectionString(string databaseFileName)
    {
        var databasePath = GetDatabasePath(databaseFileName);
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath
        };

        return builder.ToString();
    }

    private static string GetDatabasePath(string databaseFileName)
    {
        var databasePath = Path.Combine(CacheDirectory, databaseFileName);
        TryMigrateExistingDatabase(databaseFileName, databasePath);
        return databasePath;
    }

    private static void TryMigrateExistingDatabase(string databaseFileName, string targetPath)
    {
        if (File.Exists(targetPath))
        {
            return;
        }

        var sourcePath = Path.GetFullPath(databaseFileName);

        if (!File.Exists(sourcePath) || string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            File.Copy(sourcePath, targetPath, overwrite: false);
        }
        catch
        {
        }
    }
}
