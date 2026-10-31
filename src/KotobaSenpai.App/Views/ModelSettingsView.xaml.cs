using System.Windows;
using System.Windows.Controls;
using KotobaSenpai.App.Controls;

namespace KotobaSenpai.App.Views;

public partial class ModelSettingsView : UserControl
{
    private readonly ModelSettingsInteraction _interaction;

    public ModelSettingsView()
    {
        InitializeComponent();
        _interaction = new ModelSettingsInteraction(this, LlmApiKeyPasswordBox);
    }

    private void CopyAgentPrompt_Click(object sender, RoutedEventArgs e) => _interaction.CopyAgentPrompt();
    private void CopyConfigurationError_Click(object sender, RoutedEventArgs e) => _interaction.CopyConfigurationError();
}
