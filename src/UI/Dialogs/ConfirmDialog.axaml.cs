using Avalonia.Controls;
using Avalonia.Interactivity;
using GHelper.Linux.I18n;

namespace GHelper.Linux.UI.Dialogs;

/// <summary>
/// Lightweight Yes/No confirmation dialog. Returns the user's answer
/// asynchronously via <see cref="ShowAsync"/>; the dialog itself just
/// completes the underlying TaskCompletionSource on click.
///
/// Used by the XG Mobile disable flow (mirrors Windows g-helper's "Did
/// you close all applications running on XG Mobile?" prompt) but the
/// API is generic - any future feature that needs a Yes/No prompt can
/// reuse this without touching MessageBox.Avalonia or another NuGet dep.
/// </summary>
public partial class ConfirmDialog : Window
{
    private readonly TaskCompletionSource<bool> _tcs = new();

    public ConfirmDialog()
    {
        InitializeComponent();

        // Default button labels from the i18n catalog. Callers may override
        // via the constructor overload below.
        buttonYes.Content = Labels.Get("confirm_yes");
        buttonNo.Content = Labels.Get("confirm_no");

        // Treat window-close as "No" so the caller never sees an unfinished
        // task if the user dismisses via the window-manager close button.
        Closing += (_, _) =>
        {
            if (!_tcs.Task.IsCompleted)
                _tcs.TrySetResult(false);
        };
    }

    /// <summary>
    /// Show the dialog modally over <paramref name="owner"/> and resolve to
    /// true if the user clicked Yes, false otherwise (No, Esc, or window
    /// close). Caller must be on the UI thread.
    /// </summary>
    public static async Task<bool> ShowAsync(Window owner, string title, string message)
    {
        var dlg = new ConfirmDialog();
        dlg.Title = title;
        dlg.textTitle.Text = title;
        dlg.textMessage.Text = message;

        await dlg.ShowDialog(owner);
        return await dlg._tcs.Task;
    }

    private void ButtonYes_Click(object? sender, RoutedEventArgs e)
    {
        _tcs.TrySetResult(true);
        Close();
    }

    private void ButtonNo_Click(object? sender, RoutedEventArgs e)
    {
        _tcs.TrySetResult(false);
        Close();
    }
}
