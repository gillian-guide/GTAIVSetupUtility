using System.Text;

namespace GTAIVSetupUtility.Services;

// i couldn't find a good ini parser for my needs that didn't break zolikapatch's config, so this is a custom one.
// It is line based on purpose: section order, comments (whole-line and the inline "// ..." comments used by
// IV.EFLC.OpenPreCEPatch.ini and FusionFix's ini) and the file's spacing all survive a save.

public sealed class IniEditorService
{
    private const string DefaultValue = "N/A";
    private const string InlineCommentSeparator = " //";

    private sealed class IniEntry
    {
        public string Section = string.Empty;
        public string Key = string.Empty;

        // Everything before the value on the original line, e.g. "BorderlessWindowed = ".
        public string Prefix = string.Empty;

        // The inline "// ..." comment after the value, including the whitespace in front of it.
        public string Suffix = string.Empty;

        public string Value = string.Empty;
    }

    private sealed class IniLine
    {
        public string Raw;
        public IniEntry? Entry;
        public string? Section;
        public bool IsSection;

        public IniLine(string raw)
        {
            Raw = raw;
        }
    }

    private readonly string? _filePath;
    private readonly List<IniLine> _lines = [];
    private readonly List<IniEntry> _entries = [];
    private string _newLine = "\r\n";
    private bool _endsWithNewLine;
    private bool _fileExists;

    public IniEditorService(string? filePath)
    {
        _filePath = filePath;
        LoadFile();
    }

    private void LoadFile()
    {
        if (string.IsNullOrWhiteSpace(_filePath) || !File.Exists(_filePath))
            return;

        _fileExists = true;

        string text = File.ReadAllText(_filePath);
        _newLine = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

        string[] rawLines = text.Split('\n');
        if (rawLines.Length > 0 && rawLines[^1].Length == 0)
        {
            _endsWithNewLine = true;
            rawLines = rawLines[..^1];
        }

        string? currentSection = null;

        foreach (string rawLine in rawLines)
        {
            string line = rawLine.TrimEnd('\r');
            string trimmed = line.Trim();

            if (trimmed.Length == 0 || trimmed.StartsWith(';') || trimmed.StartsWith('#') ||
                trimmed.StartsWith("//", StringComparison.Ordinal))
            {
                _lines.Add(new IniLine(line));
                continue;
            }

            if (trimmed.StartsWith('[') && trimmed.IndexOf(']') is int closingBracket and > 1)
            {
                currentSection = trimmed[1..closingBracket].Trim();
                _lines.Add(new IniLine(line) { IsSection = true, Section = currentSection });
                continue;
            }

            if (currentSection is not null && line.IndexOf('=') is int separatorIndex and >= 0)
            {
                IniEntry entry = ParseEntry(line, currentSection, separatorIndex);
                _entries.Add(entry);
                _lines.Add(new IniLine(line) { Entry = entry, Section = currentSection });
                continue;
            }

            _lines.Add(new IniLine(line));
        }
    }

    private static IniEntry ParseEntry(string line, string section, int separatorIndex)
    {
        string key = line[..separatorIndex].Trim();

        int valueStart = separatorIndex + 1;
        while (valueStart < line.Length && char.IsWhiteSpace(line[valueStart]))
        {
            valueStart++;
        }

        int commentIndex = line.IndexOf(InlineCommentSeparator, valueStart, StringComparison.Ordinal);
        int valueEnd = commentIndex >= 0 ? commentIndex : line.Length;

        while (valueEnd > valueStart && char.IsWhiteSpace(line[valueEnd - 1]))
        {
            valueEnd--;
        }

        return new IniEntry
        {
            Section = section,
            Key = key,
            Prefix = line[..valueStart],
            Suffix = commentIndex >= 0 ? line[commentIndex..] : string.Empty,
            Value = line[valueStart..valueEnd]
        };
    }

    /// <summary>
    /// Reads a value from a specific section of the INI file.
    /// </summary>
    /// <param name="section">The section name (e.g., "MAIN")</param>
    /// <param name="key">The key name</param>
    /// <returns>The value if found, otherwise "N/A"</returns>
    public string ReadValue(string section, string key)
        => FindEntry(section, key)?.Value ?? DefaultValue;

    /// <summary>
    /// Reads a value by key from any section. ZolikaPatch keeps every option in [Options] while
    /// IV.EFLC.OpenPreCEPatch.ini groups the same options under [STABILITY], [GRAPHICS], etc.
    /// </summary>
    /// <param name="key">The key name</param>
    /// <returns>The value if found, otherwise "N/A"</returns>
    public string ReadValueAnySection(string key)
        => FindEntryByKey(key)?.Value ?? DefaultValue;

    /// <summary>
    /// Edits or creates a key-value pair in the specified section.
    /// </summary>
    public void EditValue(string section, string key, string newValue)
    {
        IniEntry? entry = FindEntry(section, key);
        if (entry is not null)
        {
            entry.Value = newValue;
            return;
        }

        AddEntry(section, key, newValue);
    }

    /// <summary>
    /// Edits a key wherever it already lives. Missing keys are only created when a fallback section
    /// is provided; OpenPreCE options are grouped, so a missing key means the option predates this
    /// version of the mod and adding it to an arbitrary section would not help.
    /// </summary>
    /// <returns>True when an existing key was edited or a new key was created.</returns>
    public bool EditValueAnySection(string key, string newValue, string? fallbackSection = null)
    {
        IniEntry? entry = FindEntryByKey(key);
        if (entry is not null)
        {
            entry.Value = newValue;
            return true;
        }

        if (string.IsNullOrEmpty(fallbackSection))
            return false;

        AddEntry(fallbackSection, key, newValue);
        return true;
    }

    public bool ContainsKey(string key) => FindEntryByKey(key) is not null;

    /// <summary>
    /// Saves the INI data back to the file, touching only lines whose values were edited.
    /// </summary>
    public void SaveFile()
    {
        if (string.IsNullOrWhiteSpace(_filePath))
            return;

        // Never create an empty file for a non-existent config that was only read from.
        if (!_fileExists && _lines.Count == 0)
            return;

        var builder = new StringBuilder();

        for (int i = 0; i < _lines.Count; i++)
        {
            IniLine line = _lines[i];

            builder.Append(line.Entry is null
                ? line.Raw
                : line.Entry.Prefix + line.Entry.Value + line.Entry.Suffix);

            if (i < _lines.Count - 1 || _endsWithNewLine)
            {
                builder.Append(_newLine);
            }
        }

        File.WriteAllText(_filePath, builder.ToString());
    }

    private IniEntry? FindEntry(string section, string key)
    {
        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            IniEntry entry = _entries[i];
            if (string.Equals(entry.Section, section, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return entry;
            }
        }

        return null;
    }

    private IniEntry? FindEntryByKey(string key)
    {
        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            if (string.Equals(_entries[i].Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return _entries[i];
            }
        }

        return null;
    }

    private void AddEntry(string section, string key, string value)
    {
        int sectionIndex = -1;
        for (int i = _lines.Count - 1; i >= 0; i--)
        {
            if (_lines[i].IsSection &&
                string.Equals(_lines[i].Section, section, StringComparison.OrdinalIgnoreCase))
            {
                sectionIndex = i;
                break;
            }
        }

        if (sectionIndex < 0)
        {
            _lines.Add(new IniLine($"[{section}]") { IsSection = true, Section = section });
            sectionIndex = _lines.Count - 1;
        }

        // Keep the new option inside its section: after the last key-value line, before trailing
        // comments and blank lines that separate it from the next section.
        int insertIndex = sectionIndex + 1;
        for (int i = sectionIndex + 1; i < _lines.Count; i++)
        {
            if (_lines[i].IsSection)
                break;

            if (_lines[i].Entry is not null)
                insertIndex = i + 1;
        }

        bool spacedStyle = _entries.Exists(entry =>
            string.Equals(entry.Section, section, StringComparison.OrdinalIgnoreCase) &&
            entry.Prefix.EndsWith("= ", StringComparison.Ordinal));

        IniEntry newEntry = new()
        {
            Section = section,
            Key = key,
            Prefix = spacedStyle ? $"{key} = " : $"{key}=",
            Suffix = string.Empty,
            Value = value
        };

        _entries.Add(newEntry);
        _lines.Insert(insertIndex, new IniLine(string.Empty) { Entry = newEntry, Section = section });
    }
}
