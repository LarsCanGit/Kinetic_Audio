using System.Windows;
using System.Windows.Input;

namespace KineticAudio;

public partial class TextInputDialog : Window
{
    public string InputText => InputBox.Text;

    public TextInputDialog(string prompt, string initialValue = "")
    {
        InitializeComponent();
        PromptText.Text = prompt;
        InputBox.Text = initialValue;
        Loaded += (_, _) => { InputBox.Focus(); InputBox.SelectAll(); };
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            DialogResult = true;
        else if (e.Key == Key.Escape)
            DialogResult = false;
    }
}
