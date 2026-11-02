using Avalonia;
using Avalonia.Controls;

namespace GTAIVSetupUtility.Controls;

public partial class TipContent : UserControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<TipContent, string?>(nameof(Title));

    public static readonly StyledProperty<string?> DescriptionProperty =
        AvaloniaProperty.Register<TipContent, string?>(nameof(Description));

    public TipContent()
    {
        InitializeComponent();
        DataContext = this;
    }

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }
}
