using System.Windows;

namespace HearDelta.App.Services;

public sealed class MessageBoxConfirmationService : IUserConfirmationService, IUnsavedChangesPrompt
{
    public UnsavedChangesDecision Ask(string title, string message) =>
        MessageBox.Show(
            message,
            title,
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question,
            MessageBoxResult.Cancel) switch
        {
            MessageBoxResult.Yes => UnsavedChangesDecision.Save,
            MessageBoxResult.No => UnsavedChangesDecision.Discard,
            _ => UnsavedChangesDecision.Cancel
        };

    public bool Confirm(string title, string message) =>
        MessageBox.Show(
            message,
            title,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No) == MessageBoxResult.Yes;
}
