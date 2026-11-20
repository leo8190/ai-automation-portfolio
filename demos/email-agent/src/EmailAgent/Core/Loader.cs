using System.Text;
using System.Text.Json;

namespace EmailAgent.Core;

public static class InboxLoader
{
    public static IReadOnlyList<IncomingEmail> Load(string source)
    {
        var path = Path.GetFullPath(source);
        if (File.Exists(path))
            return path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                ? LoadJson(path)
                : [LoadEml(path)];

        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException($"Inbox not found: {source}");

        var emails = new List<IncomingEmail>();
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
        {
            if (file.EndsWith(".eml", StringComparison.OrdinalIgnoreCase))
                emails.Add(LoadEml(file));
            else if (file.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                emails.AddRange(LoadJson(file));
        }
        return emails;
    }

    public static IncomingEmail LoadEml(string path)
    {
        var raw = File.ReadAllText(path);
        var split = raw.Split(["\r\n\r\n", "\n\n"], 2, StringSplitOptions.None);
        var headers = ParseHeaders(split[0]);
        var body = split.Length > 1 ? split[1].Trim() : "";
        var (name, addr) = ParseAddress(headers.GetValueOrDefault("From") ?? "");
        var id = (headers.GetValueOrDefault("Message-ID") ?? Path.GetFileNameWithoutExtension(path)).Trim().Trim('<', '>');
        return new IncomingEmail(
            id,
            string.IsNullOrWhiteSpace(name) ? addr.Split('@')[0] : name,
            addr.ToLowerInvariant(),
            headers.GetValueOrDefault("To") ?? "",
            headers.GetValueOrDefault("Subject") ?? "",
            headers.GetValueOrDefault("Date") ?? "",
            body);
    }

    public static IReadOnlyList<IncomingEmail> LoadJson(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var list = new List<IncomingEmail>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var (name, addr) = ParseAddress(item.GetProperty("from").GetString() ?? "");
            var id = item.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? addr : addr;
            list.Add(new IncomingEmail(
                id,
                string.IsNullOrWhiteSpace(name) ? addr.Split('@')[0] : name,
                addr.ToLowerInvariant(),
                item.TryGetProperty("to", out var toEl) ? toEl.GetString() ?? "" : "",
                item.TryGetProperty("subject", out var subEl) ? subEl.GetString() ?? "" : "",
                item.TryGetProperty("date", out var dateEl) ? dateEl.GetString() ?? "" : "",
                (item.TryGetProperty("body", out var bodyEl) ? bodyEl.GetString() ?? "" : "").Trim()));
        }
        return list;
    }

    internal static (string Name, string Address) ParseAddress(string raw)
    {
        raw = raw.Trim();
        var lt = raw.IndexOf('<');
        var gt = raw.IndexOf('>');
        if (lt >= 0 && gt > lt)
        {
            var name = raw[..lt].Trim().Trim('"');
            var addr = raw[(lt + 1)..gt].Trim();
            return (name, addr);
        }
        return ("", raw);
    }

    static Dictionary<string, string> ParseHeaders(string block)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? current = null;
        var value = new StringBuilder();
        foreach (var line in block.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            if (line.Length > 0 && (line[0] == ' ' || line[0] == '\t') && current is not null)
            {
                value.Append(' ').Append(line.Trim());
                continue;
            }
            if (current is not null) map[current] = value.ToString();
            var colon = line.IndexOf(':');
            if (colon <= 0)
            {
                current = null;
                value.Clear();
                continue;
            }
            current = line[..colon].Trim();
            value.Clear().Append(line[(colon + 1)..].Trim());
        }
        if (current is not null) map[current] = value.ToString();
        return map;
    }
}
