using Microsoft.UI.Xaml.Controls;
using Clip.Models;
using Clip.ViewModels;

namespace Clip.Views;

public sealed partial class HistoryView : UserControl
{
    public HistoryView()
    {
        InitializeComponent();
    }

    private DownloadViewModel? ViewModel => DataContext as DownloadViewModel;

    private void OnRedownloadClicked(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is Button { DataContext: DownloadHistoryEntry entry } &&
            ViewModel?.RedownloadCommand.CanExecute(entry) == true)
        {
            ViewModel.RedownloadCommand.Execute(entry);
        }
    }

    private void OnOpenFileClicked(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is Button { DataContext: DownloadHistoryEntry entry } &&
            ViewModel?.OpenFileCommand.CanExecute(entry) == true)
        {
            ViewModel.OpenFileCommand.Execute(entry);
        }
    }

    private void OnOpenFolderClicked(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is Button { DataContext: DownloadHistoryEntry entry } &&
            ViewModel?.OpenFolderCommand.CanExecute(entry) == true)
        {
            ViewModel.OpenFolderCommand.Execute(entry);
        }
    }
}
