public static class SettingsService
{
    private static readonly string DbConnectionString = LinkScapeCachePaths.GetDatabaseConnectionString("settings.db");
    public static event Action<string, string?>? SettingChanged;
     
    public static void EnsureDatabase()
    {
        using var conn = new SqliteConnection(DbConnectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS Settings(
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL
            )";
        cmd.ExecuteNonQuery();

        EnsureDefaultSettings();
    }

    public static string? GetValue(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        using var conn = new SqliteConnection(DbConnectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Value FROM Settings WHERE Key = $key";
        cmd.Parameters.AddWithValue("$key", key);

        return cmd.ExecuteScalar() as string;
    }

    public static string GetValueOrDefault(string key, string defaultValue)
    {
        return GetValue(key) ?? defaultValue;
    }

    public static void SetValue(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        using var conn = new SqliteConnection(DbConnectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO Settings(Key, Value)
            VALUES($key, $value)
            ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;";
        cmd.Parameters.AddWithValue("$key", key);
        cmd.Parameters.AddWithValue("$value", value ?? string.Empty);
        cmd.ExecuteNonQuery();

        SettingChanged?.Invoke(key, value ?? string.Empty);
    }

    public static bool RemoveValue(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        using var conn = new SqliteConnection(DbConnectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Settings WHERE Key = $key";
        cmd.Parameters.AddWithValue("$key", key);

        var removed = cmd.ExecuteNonQuery() > 0;

        if (removed)
        {
            SettingChanged?.Invoke(key, null);
        }

        return removed;
    }

    public static Dictionary<string, string> Dump()
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        using var conn = new SqliteConnection(DbConnectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Key, Value FROM Settings ORDER BY Key";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var key = reader.GetString(0);
            var value = reader.GetString(1);
            values[key] = value;
        }

        return values;
    }

    private static void EnsureDefaultSettings()
    {
        const string key = "Developer";
        const string value = "John M. Doyle";

        if (string.IsNullOrWhiteSpace(GetValueOrDefault(key, string.Empty)))
        {
            SetValue(key, value);
        }
    }
}
