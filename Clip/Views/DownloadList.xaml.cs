using Microsoft.UI.Xaml.Controls;
using Clip.Models;
using Clip.ViewModels;

namespace Clip.Views;

public sealed partial class DownloadList : UserControl
{
    public DownloadList()
    {
        InitializeComponent();
    }

    private DownloadViewModel? ViewModel => DataContext as DownloadViewModel;

    private void OnCancelClicked(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is Button { DataContext: DownloadItem item } && ViewModel?.CancelCommand.CanExecute(item) == true)
        {
            ViewModel.CancelCommand.Execute(item);
        }
    }

    private void OnRetryClicked(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is Button { DataContext: DownloadItem item } && ViewModel?.RetryCommand.CanExecute(item) == true)
        {
            ViewModel.RetryCommand.Execute(item);
        }
    }

    private void OnOpenFileClicked(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is Button { DataContext: DownloadItem item } && ViewModel?.OpenFileCommand.CanExecute(item) == true)
        {
            ViewModel.OpenFileCommand.Execute(item);
        }
    }

    private void OnOpenFolderClicked(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is Button { DataContext: DownloadItem item } && ViewModel?.OpenFolderCommand.CanExecute(item) == true)
        {
            ViewModel.OpenFolderCommand.Execute(item);
        }
    }
}
