using Avalonia;
using Avalonia.Controls;

namespace GTAIVSetupUtility.Controls;

public partial class TipContent : UserControl
{
    public static readonly StyledProperty<string?> DescriptionProperty =
        AvaloniaProperty.Register<TipContent, string?>(nameof(Description));

    public TipContent()
    {
        InitializeComponent();
        DataContext = this;
    }

    public string? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }
}
