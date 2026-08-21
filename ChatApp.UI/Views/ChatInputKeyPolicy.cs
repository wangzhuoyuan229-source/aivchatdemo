using Avalonia.Input;

namespace ChatApp.UI.Views;

internal static class ChatInputKeyPolicy
{
    public static bool IsPlainEnter(Key key, KeyModifiers modifiers) =>
        (key == Key.Enter || key == Key.Return) && modifiers == KeyModifiers.None;

    public static bool ShouldDeferEnterToIme(
        Key key,
        KeyModifiers modifiers,
        bool hasActivePreedit) =>
        hasActivePreedit && IsPlainEnter(key, modifiers);

    public static bool TryRemoveLineBreakBeforeCaret(
        string currentText,
        int currentCaretIndex,
        out string textWithoutLineBreak,
        out int caretIndex)
    {
        caretIndex = Math.Clamp(currentCaretIndex, 0, currentText.Length);
        var lineBreakLength = 0;
        if (caretIndex >= 2 && currentText[caretIndex - 2] == '\r' && currentText[caretIndex - 1] == '\n')
            lineBreakLength = 2;
        else if (caretIndex >= 1 && currentText[caretIndex - 1] == '\n')
            lineBreakLength = 1;

        if (lineBreakLength == 0)
        {
            textWithoutLineBreak = currentText;
            return false;
        }

        var lineBreakStart = caretIndex - lineBreakLength;
        textWithoutLineBreak = currentText.Remove(lineBreakStart, lineBreakLength);
        caretIndex = lineBreakStart;
        return true;
    }
}
