using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Clip.ViewModels;

namespace Clip.Views;

public sealed partial class ContentView : UserControl
{
    public ContentView()
    {
        InitializeComponent();
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            await viewModel.AcceptDroppedDataAsync(e.DataView);
        }

        e.Handled = true;
    }
}
