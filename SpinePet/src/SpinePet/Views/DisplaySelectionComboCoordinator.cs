using System.ComponentModel;
using SpinePet.ViewModels;
using ComboBox = System.Windows.Controls.ComboBox;

namespace SpinePet.Views;

internal sealed class DisplaySelectionComboCoordinator : IDisposable
{
    private readonly ComboBox _comboBox;
    private readonly MainViewModel _viewModel;
    private bool _isDisposed;

    public DisplaySelectionComboCoordinator(
        ComboBox comboBox,
        MainViewModel viewModel)
    {
        _comboBox = comboBox;
        _viewModel = viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        ApplySnapshot();
    }

    public bool IsApplying { get; private set; }

    private void OnViewModelPropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) ||
            e.PropertyName == nameof(MainViewModel.DisplaySelectionOptions) ||
            e.PropertyName == nameof(MainViewModel.SelectedDisplaySelection))
        {
            ApplySnapshot();
        }
    }

    private void ApplySnapshot()
    {
        if (_isDisposed)
        {
            return;
        }

        IsApplying = true;
        try
        {
            IReadOnlyList<string> options =
                _viewModel.DisplaySelectionOptions;
            if (!ReferenceEquals(_comboBox.ItemsSource, options))
            {
                _comboBox.ItemsSource = options;
            }

            string? selectedItem = options.FirstOrDefault(option =>
                string.Equals(
                    option,
                    _viewModel.SelectedDisplaySelection,
                    StringComparison.Ordinal));
            _comboBox.SelectedItem = selectedItem;
        }
        finally
        {
            IsApplying = false;
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }
}
