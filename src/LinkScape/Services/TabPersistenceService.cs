using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

public static class TabPersistenceService
{
    private const string DbPath = "tabs.db";

    public static void EnsureDatabase()
    {
        using var conn = new SqliteConnection($"Data Source={DbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS KeyValue(
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL
            )";
        cmd.ExecuteNonQuery();
    }

    public static void SaveTabs(string key, object tabs)
    {
        var json = JsonSerializer.Serialize(tabs);
        using var conn = new SqliteConnection($"Data Source={DbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO KeyValue(Key, Value) VALUES($k, $v) ON CONFLICT(Key) DO UPDATE SET Value = $v";
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", json);
        cmd.ExecuteNonQuery();
    }

    public static T? LoadTabs<T>(string key)
    {
        using var conn = new SqliteConnection($"Data Source={DbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Value FROM KeyValue WHERE Key = $k";
        cmd.Parameters.AddWithValue("$k", key);
        var val = cmd.ExecuteScalar() as string;
        return val is null ? default : JsonSerializer.Deserialize<T>(val);
    }

    // Update timestamp, optionally update URL, and optionally increment visited count for a tab with the given id.
    // newUrl: optional new url to write. urlChanged: indicates whether the URL actually changed (used to decide increment).
    public static void UpdateTabVisit(string key, string tabId, bool incrementVisitCount = true, string? newUrl = null, bool urlChanged = false)
    {
        using var conn = new SqliteConnection($"Data Source={DbPath}");
        conn.Open();

        using var selectCmd = conn.CreateCommand();
        selectCmd.CommandText = "SELECT Value FROM KeyValue WHERE Key = $k";
        selectCmd.Parameters.AddWithValue("$k", key);
        var existingJson = selectCmd.ExecuteScalar() as string;
        if (string.IsNullOrWhiteSpace(existingJson))
        {
            return;
        }

        var node = JsonNode.Parse(existingJson);
        if (node is not JsonArray arr)
        {
            return;
        }

        var updated = false;
        foreach (var item in arr)
        {
            if (item is JsonObject obj &&
                obj.TryGetPropertyValue("Id", out var idNode) &&
                idNode?.ToString() == tabId)
            {
                // Update DateTime
                obj["DateTime"] = DateTime.Now;

                // Update URL if provided (and optionally only when urlChanged is true)
                if (!string.IsNullOrWhiteSpace(newUrl) && urlChanged)
                {
                    obj["Url"] = newUrl;
                }
                else if (!string.IsNullOrWhiteSpace(newUrl) && !obj.TryGetPropertyValue("Url", out _))
                {
                    // If URL field missing, set it
                    obj["Url"] = newUrl;
                }

                // Update/Increment VisitedCount only when requested and when URL changed (or caller wants increment regardless)
                if (incrementVisitCount)
                {
                    if (urlChanged)
                    {
                        if (obj.TryGetPropertyValue("VisitedCount", out var vcNode) &&
                            int.TryParse(vcNode?.ToString(), out var vc))
                        {
                            obj["VisitedCount"] = vc + 1;
                        }
                        else
                        {
                            obj["VisitedCount"] = 1;
                        }
                    }
                    // if urlChanged == false and caller still requested incrementVisitCount,
                    // we leave count unchanged (preserve existing behavior when increment depends on url change).
                }

                updated = true;
                break;
            }
        }

        if (!updated)
        {
            return;
        }

        var newJson = node.ToJsonString(new JsonSerializerOptions { WriteIndented = false });

        using var updateCmd = conn.CreateCommand();
        updateCmd.CommandText = "UPDATE KeyValue SET Value = $v WHERE Key = $k";
        updateCmd.Parameters.AddWithValue("$v", newJson);
        updateCmd.Parameters.AddWithValue("$k", key);
        updateCmd.ExecuteNonQuery();
    }

    // Helper: replace or append a single tab object (by Id). Ensures DateTime is set.
    // Accepts a JsonObject representing the tab (useful if you have the tab instance as JSON).
    public static void SaveOrReplaceTabJson(string key, JsonObject tabObject)
    {
        if (tabObject is null)
            return;

        // Ensure DateTime set
        tabObject["DateTime"] = DateTime.Now;

        using var conn = new SqliteConnection($"Data Source={DbPath}");
        conn.Open();

        using var selectCmd = conn.CreateCommand();
        selectCmd.CommandText = "SELECT Value FROM KeyValue WHERE Key = $k";
        selectCmd.Parameters.AddWithValue("$k", key);
        var existingJson = selectCmd.ExecuteScalar() as string;

        JsonArray arr;
        if (string.IsNullOrWhiteSpace(existingJson))
        {
            arr = new JsonArray();
        }
        else
        {
            var node = JsonNode.Parse(existingJson);
            arr = node as JsonArray ?? new JsonArray();
        }

        // Replace if exists, otherwise append
        var id = tabObject["Id"]?.ToString();
        if (!string.IsNullOrWhiteSpace(id))
        {
            var replaced = false;
            for (var i = 0; i < arr.Count; i++)
            {
                if (arr[i] is JsonObject obj &&
                    obj.TryGetPropertyValue("Id", out var idNode) &&
                    idNode?.ToString() == id)
                {
                    // If replacing, preserve/merge VisitedCount if desired.
                    if (obj.TryGetPropertyValue("VisitedCount", out var vcNode) &&
                        int.TryParse(vcNode?.ToString(), out var existingVc) &&
                        !tabObject.TryGetPropertyValue("VisitedCount", out _))
                    {
                        tabObject["VisitedCount"] = existingVc;
                    }

                    arr[i] = tabObject;
                    replaced = true;
                    break;
                }
            }

            if (!replaced)
            {
                // If no visited count present set to 1 by default on new tab
                if (!tabObject.TryGetPropertyValue("VisitedCount", out _))
                {
                    tabObject["VisitedCount"] = 1;
                }
                arr.Add(tabObject);
            }
        }
        else
        {
            // No id — just append
            arr.Add(tabObject);
        }

        var newJson = arr.ToJsonString(new JsonSerializerOptions { WriteIndented = false });

        using var updateCmd = conn.CreateCommand();
        updateCmd.CommandText = "INSERT INTO KeyValue(Key, Value) VALUES($k, $v) ON CONFLICT(Key) DO UPDATE SET Value = $v";
        updateCmd.Parameters.AddWithValue("$k", key);
        updateCmd.Parameters.AddWithValue("$v", newJson);
        updateCmd.ExecuteNonQuery();
    }
}