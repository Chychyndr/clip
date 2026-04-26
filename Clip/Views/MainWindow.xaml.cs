using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Clip.Services;
using Clip.ViewModels;

namespace Clip.Views;

public sealed partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        Root.DataContext = ViewModel;
        ClipTheme.ApplyMica(this);
        NativeWindowService.Resize(this, 1360, 900);
        var appWindow = NativeWindowService.GetAppWindow(this);
        appWindow.Closing += OnAppWindowClosing;
    }

    public MainViewModel ViewModel { get; }
    public bool AllowClose { get; set; }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (AllowClose || !ViewModel.Settings.HideToTrayOnClose)
        {
            return;
        }

        args.Cancel = true;
        NativeWindowService.Hide(this);
    }
}
