using System.Windows;

namespace AutoAddSchedule.UI.Dialogs;

public partial class SupplementDialog : Window
{
    public SupplementDialog()
    {
        InitializeComponent();
    }

    public string SupplementText => InputBox.Text.Trim();

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
