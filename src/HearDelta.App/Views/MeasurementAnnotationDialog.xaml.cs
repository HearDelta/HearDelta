using System.Windows;
using HearDelta.App.Services;

namespace HearDelta.App.Views;

public partial class MeasurementAnnotationDialog : Window
{
    public MeasurementAnnotationDialog(string heading, MeasurementAnnotationInput current)
    {
        InitializeComponent();
        HeadingText.Text = heading;
        NameBox.Text = current.Name;
        CommentBox.Text = current.Comment;
        NameBox.SelectAll();
    }

    public MeasurementAnnotationInput Input => new(NameBox.Text, CommentBox.Text);

    private void OnSave(object sender, RoutedEventArgs e) => DialogResult = true;
}

/// <summary>Bearbeitet Name und Kommentar in einem modalen Dialog über dem Hauptfenster.</summary>
public sealed class DialogMeasurementAnnotationEditor : IMeasurementAnnotationEditor
{
    public MeasurementAnnotationInput? Edit(string title, MeasurementAnnotationInput current)
    {
        var dialog = new MeasurementAnnotationDialog(title, current) { Owner = Application.Current?.MainWindow };
        return dialog.ShowDialog() == true ? dialog.Input : null;
    }
}
