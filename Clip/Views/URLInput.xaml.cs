using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Clip.Services;
using Clip.ViewModels;

namespace Clip.Views;

public sealed partial class URLInput : UserControl
{
    public URLInput()
    {
        InitializeComponent();
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Link;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var text = "";
        if (e.DataView.Contains(StandardDataFormats.WebLink))
        {
            text = (await e.DataView.GetWebLinkAsync())?.ToString() ?? "";
        }
        else if (e.DataView.Contains(StandardDataFormats.Text))
        {
            text = await e.DataView.GetTextAsync();
        }

        if (URLDetector.TryExtractFirstUrl(text, out var url))
        {
            viewModel.UrlText = url;
            await viewModel.AnalyzeCurrentUrlAsync();
        }
    }
}
