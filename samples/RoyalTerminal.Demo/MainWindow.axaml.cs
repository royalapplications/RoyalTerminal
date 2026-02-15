// Licensed under the MIT License.
// RoyalTerminal.Demo — Main window view activation.

using Avalonia.Markup.Xaml;
using RoyalTerminal.Demo.Services;
using RoyalTerminal.Demo.ViewModels;
using ReactiveUI;
using ReactiveUI.Avalonia;

namespace RoyalTerminal.Demo;

public partial class MainWindow : ReactiveWindow<MainWindowViewModel>
{
    public MainWindow()
    {
        InitializeComponent();

        ViewModel = new MainWindowViewModel();

        this.WhenActivated(disposables =>
        {
            var controller = new MainWindowController(this, ViewModel!);
            disposables.Add(controller.Activate());
        });
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
