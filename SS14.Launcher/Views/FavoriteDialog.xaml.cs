using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SS14.Launcher.Views;

public partial class AddFavoriteDialog : Window
{
    public AddFavoriteDialog()
    {
        InitializeComponent();

        SubmitButton.Content = "Add";

        NameBox.TextChanged += UpdateSubmitValid;
        AddressBox.TextChanged += UpdateSubmitValid;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        NameBox.Focus();
    }

    protected virtual void Submit(object? _1, RoutedEventArgs _2)
    {
        Close((NameBox.Text.Trim(), AddressBox.Text.Trim()));
    }

    private void UpdateSubmitValid(object? _1, TextChangedEventArgs _2)
    {
        var validAddr = DirectConnectDialog.IsAddressValid(AddressBox.Text);
        var valid = validAddr && !string.IsNullOrEmpty(NameBox.Text);

        SubmitButton.IsEnabled = valid;
        TxtInvalid.IsVisible = !validAddr;
    }
}

public class EditAddFavoriteDialog : AddFavoriteDialog
{
    public EditAddFavoriteDialog(string name, string address)
    {
        Title = "Edit Favorite Server";
        SubmitButton.Content = "Done";
        NameBox.Text = name;
        AddressBox.Text = address;
    }

    protected override void Submit(object? _1, RoutedEventArgs _2)
    {
        throw new NotImplementedException();
    }
}
