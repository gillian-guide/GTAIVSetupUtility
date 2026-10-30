using System.Text.RegularExpressions;

namespace GTAIVSetupUtility.Modules.DlssIv;

/// <summary>
/// Small comment-preserving INI editor. The DLSS-IV config is mostly documentation, so the
/// existing IniEditorService (which rewrites the whole file) would destroy it.
/// Pass a null section for the flat DLSS-IV.cfg.
/// </summary>
internal static partial class IniTextEditor
{
    [GeneratedRegex(@"^\s*\[(?<section>[^\]]+)\]\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex SectionRegex();

    public static string? ReadValue(string text, string? section, string key)
    {
        var lines = SplitLines(text);
        (int start, int end) = FindSection(lines, section);
        if (start < 0)
            return null;

        for (int i = start; i < end; i++)
        {
            if (TryReadKey(lines[i], key, out string value))
                return value;
        }

        return null;
    }

    public static string SetValue(string text, string? section, string key, string value)
    {
        var lines = SplitLines(text);
        string newline = DetectNewline(text);
        (int start, int end) = FindSection(lines, section);

        if (start < 0 && !string.IsNullOrEmpty(section))
        {
            if (lines.Count > 0 && lines[^1].Length != 0)
                lines.Add(string.Empty);
            lines.Add($"[{section}]");
            lines.Add($"{key}={value}");
            return JoinLines(lines, newline);
        }

        for (int i = start; i < end; i++)
        {
            if (!TryReadKey(lines[i], key, out _))
                continue;

            int equals = lines[i].IndexOf('=');
            int valueStart = equals + 1;
            while (valueStart < lines[i].Length && char.IsWhiteSpace(lines[i][valueStart]))
                valueStart++;

            lines[i] = lines[i][..valueStart] + value;
            return JoinLines(lines, newline);
        }

        int insertAt = end;
        if (insertAt > 0 && lines[insertAt - 1].Length == 0 && section != null)
            insertAt--;

        lines.Insert(insertAt, $"{key}={value}");
        return JoinLines(lines, newline);
    }

    public static string RemoveValue(string text, string? section, string key)
    {
        var lines = SplitLines(text);
        string newline = DetectNewline(text);
        (int start, int end) = FindSection(lines, section);
        if (start < 0)
            return text;

        for (int i = end - 1; i >= start; i--)
        {
            if (TryReadKey(lines[i], key, out _))
            {
                lines.RemoveAt(i);
                return JoinLines(lines, newline);
            }
        }

        return text;
    }

    private static (int Start, int End) FindSection(List<string> lines, string? section)
    {
        if (string.IsNullOrEmpty(section))
            return (0, lines.Count);

        int start = -1;
        for (int i = 0; i < lines.Count; i++)
        {
            var match = SectionRegex().Match(lines[i]);
            if (!match.Success)
                continue;

            if (start >= 0)
                return (start, i);

            if (string.Equals(match.Groups["section"].Value.Trim(), section, StringComparison.OrdinalIgnoreCase))
                start = i + 1;
        }

        return start >= 0 ? (start, lines.Count) : (-1, -1);
    }

    private static bool TryReadKey(string line, string key, out string value)
    {
        value = string.Empty;
        string trimmed = line.TrimStart();
        if (trimmed.StartsWith(';') || trimmed.StartsWith('#'))
            return false;

        int equals = line.IndexOf('=');
        if (equals < 0)
            return false;

        if (!line[..equals].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
            return false;

        value = line[(equals + 1)..].Trim();
        return true;
    }

    private static List<string> SplitLines(string text) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').ToList();

    private static string DetectNewline(string text) =>
        text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    private static string JoinLines(List<string> lines, string newline) =>
        string.Join(newline, lines);
}
