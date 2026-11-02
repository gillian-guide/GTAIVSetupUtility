using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using GTAIVSetupUtility.Modules.DlssIv;
using GTAIVSetupUtility.ViewModels;

namespace GTAIVSetupUtility.Views;

public partial class MainWindow : Window
{
    private MainWindowViewModel? _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel != null)
            _viewModel.DlssIv.PropertyChanged -= OnDlssIvPropertyChanged;

        _viewModel = DataContext as MainWindowViewModel;

        if (_viewModel != null)
            _viewModel.DlssIv.PropertyChanged += OnDlssIvPropertyChanged;
    }

    private void OnDlssIvPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DlssIvViewModel.IsVisible))
            return;

        // SizeToContent is measured before the game folder (and therefore the DLSS-IV panel) is
        // known. Re-measure once the panel appears so its extra rows are included.
        Dispatcher.UIThread.Post(() =>
        {
            InvalidateMeasure();
            InvalidateArrange();
        }, DispatcherPriority.Loaded);
    }
}
