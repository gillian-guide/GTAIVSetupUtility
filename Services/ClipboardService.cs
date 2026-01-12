using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace GTAIVSetupUtility.Services;

/// <summary>
///   A clipboard service to copy to clipboard and retrieve from clipboard.
/// </summary>
public class ClipboardService
{
    /// <summary>
    /// Sets the clipboard to given text string.
    /// <example>
    /// Example:
    /// <code>
    /// SetClipboardTextAsync("Hello World!");
    /// </code>
    /// sets the clipboard to "Hello World!".
    /// </example>
    /// </summary>
    /// <param name="text">text to be put into the clipboard.</param>
    public static async Task SetClipboardTextAsync(string? text)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
            desktop.MainWindow?.Clipboard is not { } provider)
            throw new NullReferenceException("Missing Clipboard instance.");

        await provider.SetTextAsync(text);
    }

    /// <summary>
    /// Gets the text from the clipboard.
    /// <example>
    /// Example:
    /// <code>
    /// string text = GetClipboardTextAsync();
    /// </code>
    /// retrieves text from the clipboard and saves it to the text variable.
    /// </example>
    /// </summary>
    public static async Task<string?> GetClipboardTextAsync()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
            desktop.MainWindow?.Clipboard is not { } provider)
            throw new NullReferenceException("Missing Clipboard instance.");

        return await provider.GetTextAsync();
    }
}