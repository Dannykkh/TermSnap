using System.Windows;
using System.Windows.Input;
using TermSnap.Models;

namespace TermSnap.Views;

public partial class TemplateSelectionDialog : Window
{
    public PortForwardingTemplate? SelectedTemplate { get; private set; }

    public TemplateSelectionDialog(PortForwardingTemplate[] templates)
    {
        InitializeComponent();
        TemplateList.ItemsSource = templates;
    }

    private void Template_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is PortForwardingTemplate template)
        {
            SelectedTemplate = template;
            DialogResult = true;
            Close();
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
