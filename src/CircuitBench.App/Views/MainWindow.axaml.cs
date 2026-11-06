using Avalonia.Controls;
using Avalonia.Interactivity;
using CircuitBench.App.ViewModels;

namespace CircuitBench.App.Views;

public partial class MainWindow : Window
{
    private bool _confirmedClose;

    public MainWindow()
    {
        InitializeComponent();
    }

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    private void OnCheckClicked(object? sender, RoutedEventArgs e) => ViewModel?.Editor.RunCheck();

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (ViewModel is { } vm)
        {
            await vm.StartupAsync();
        }
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_confirmedClose || ViewModel is not { } vm)
        {
            return;
        }

        if (vm.IsDirty)
        {
            e.Cancel = true;
            if (!await vm.ConfirmCloseAsync())
            {
                return;
            }

            _confirmedClose = true;
            vm.Shutdown();
            Close();
            return;
        }

        vm.Shutdown();
    }
}
