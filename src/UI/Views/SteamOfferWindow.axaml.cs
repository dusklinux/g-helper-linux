using Avalonia.Controls;
using Avalonia.Interactivity;
using GHelper.Linux.Helpers;
using GHelper.Linux.I18n;
using GHelper.Linux.Platform.Linux;

namespace GHelper.Linux.UI.Views;

/// <summary>
/// One-time startup offer to add G-Helper to the Steam library as a
/// non-Steam app (so it is reachable from game mode on handhelds). The
/// shortcut can always be added/removed later from the Updates window's
/// system integration panel.
/// </summary>
public partial class SteamOfferWindow : Window
{
    public SteamOfferWindow()
    {
        InitializeComponent();
        labelText.Text = Labels.Get("steam_offer_text");
        labelAdd.Text = Labels.Get("steam_offer_add");
        labelNo.Text = Labels.Get("steam_offer_no");
    }

    private void ButtonAdd_Click(object? sender, RoutedEventArgs e)
    {
        buttonAdd.IsEnabled = false;
        buttonNo.IsEnabled = false;
        Task.Run(() =>
        {
            bool ok = SteamShortcuts.Add(out string error);
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                App.System?.ShowNotification("G-Helper",
                    ok ? Labels.Get("steam_added") : Labels.Get("steam_failed"),
                    ok ? "dialog-information" : "dialog-warning");
                if (!ok)
                    Logger.WriteLine($"SteamOffer: add failed: {error}");
                Close();
            });
        });
    }

    private void ButtonNo_Click(object? sender, RoutedEventArgs e) => Close();
}
