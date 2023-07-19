using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Microsoft.Toolkit.Mvvm.ComponentModel;
using Serilog;
using SS14.Launcher.ViewModels.MainWindowTabs;

namespace SS14.Launcher.Views.MainWindowTabs;

public partial class ServerEntryView : UserControl
{
    public ServerEntryView()
    {
        InitializeComponent();

        Links.LayoutUpdated += ApplyButtonStyle;
        FavoriteButtonIconLabel.LayoutUpdated += UpdateFavoriteButton;
    }

    private void UpdateFavoriteButton(object? _1, EventArgs _2)
    {
        if ((DataContext as ServerEntryViewModel) is not { } context)
        {
            Log.Error("Failed to get DataContext in ServerEntryView.UpdateFavoriteButton()");
            return;
        }

        if (context.ViewedInFavoritesPane)
        {
            FavoriteButton.Classes.Add("OpenRight");
        }
        else
        {
            FavoriteButton.Classes.Remove("OpenRight");
        }

        FavoriteButtonIconLabel.Icon = context.IsFavorite
            ? (IImage)this.FindResource("Icon-star")!
            : (IImage)this.FindResource("Icon-star-outline")!;
    }

    // Sets the style for the link buttons correctly so that they look fancy
    private void ApplyButtonStyle(object? _1, EventArgs _2)
    {
        // Get all link Button controls
        var buttons = Links.GetRealizedContainers()
            .Select(p => (p as ContentPresenter)?.Child?.GetLogicalChildren().OfType<Button?>().FirstOrDefault())
            .OfType<Button>()
            .ToList();

        // Apply class based on position
        for (var i = 0; i < buttons.Count; i++)
        {
            string? style;
            if (Links.ItemCount == 1)
                return;
            else if (i == 0)
                style = "OpenRight";
            else if (i == Links.ItemCount - 1)
                style = "OpenLeft";
            else
                style = "OpenBoth";

            buttons[i].Classes.Add(style);
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (DataContext is ObservableRecipient r)
            r.IsActive = true;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        if (DataContext is ObservableRecipient r)
            r.IsActive = false;
    }
}
