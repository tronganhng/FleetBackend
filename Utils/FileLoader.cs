using System.Text.Json;

public static class FileLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static T Load<T>(string fileName) where T : new()
    {
        var path = GetPath(fileName);

        if (!File.Exists(path))
        {
            Logger.Log($"[Warning] File not found: {path}");
            return new T();
        }

        try
        {
            var json = File.ReadAllText(path);

            var data = JsonSerializer.Deserialize<T>(json, Options);

            if (data == null)
            {
                Logger.Log($"[Warning] Failed to deserialize '{path}' to {typeof(T).Name}.");
                return new T();
            }

            return data;
        }
        catch (JsonException ex)
        {
            Logger.Log(ex.Message);
            return new T();
        }
        catch (Exception ex)
        {
            Logger.Log(ex.Message);
            return new T();
        }
    }

    private static string GetPath(string fileName)
    {
        if (!fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            fileName += ".json";

        return Path.Combine(AppContext.BaseDirectory, "Data", fileName);
    }
}