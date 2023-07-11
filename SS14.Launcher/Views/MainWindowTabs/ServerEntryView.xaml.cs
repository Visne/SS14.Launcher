using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.LogicalTree;
using Microsoft.Toolkit.Mvvm.ComponentModel;

namespace SS14.Launcher.Views.MainWindowTabs;

public partial class ServerEntryView : UserControl
{
    public ServerEntryView()
    {
        InitializeComponent();

        Links.LayoutUpdated += ApplyStyle;
    }

    // Sets the style for the link buttons correctly so that they look fancy
    private void ApplyStyle(object? _1, EventArgs _2)
    {
        for (var i = 0; i < Links.ItemCount; i++)
        {
            var presenter = Links.ItemContainerGenerator.ContainerFromIndex(i);
            presenter.ApplyTemplate();

            if (presenter is not ContentPresenter { Child: ServerInfoLinkControl control }) continue;

            string? style;
            if (Links.ItemCount == 1)
                return;
            else if (i == 0)
                style = "OpenRight";
            else if (i == Links.ItemCount - 1)
                style = "OpenLeft";
            else
                style = "OpenBoth";

            control.GetLogicalChildren().OfType<Button>().FirstOrDefault()?.Classes.Add(style);
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
