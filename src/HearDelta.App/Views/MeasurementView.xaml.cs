using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace HearDelta.App.Views;

public partial class MeasurementView : UserControl
{
    public MeasurementView() => InitializeComponent();

    private void NumericAnswerTextBox_OnLoaded(object sender, RoutedEventArgs e) =>
        FocusNumericAnswer(sender as TextBox);

    private void NumericAnswerTextBox_OnStateChanged(object sender, DependencyPropertyChangedEventArgs e) =>
        FocusNumericAnswer(sender as TextBox);

    private static void FocusNumericAnswer(TextBox? textBox)
    {
        if (textBox is not { IsEnabled: true, IsVisible: true })
            return;

        _ = textBox.Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            () =>
            {
                if (textBox is not { IsEnabled: true, IsVisible: true })
                    return;

                textBox.Focus();
                textBox.SelectAll();
            });
    }
}
