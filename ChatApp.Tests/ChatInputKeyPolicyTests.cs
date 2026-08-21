using Avalonia.Input;
using ChatApp.UI.Views;

namespace ChatApp.Tests;

public sealed class ChatInputKeyPolicyTests
{
    [Fact]
    public void PlainEnterIsRecognized()
    {
        Assert.True(ChatInputKeyPolicy.IsPlainEnter(Key.Enter, KeyModifiers.None));
    }

    [Fact]
    public void ShiftEnterRemainsARegularMultilineInput()
    {
        Assert.False(ChatInputKeyPolicy.IsPlainEnter(Key.Enter, KeyModifiers.Shift));
    }

    [Fact]
    public void PlainEnterDuringImePreeditIsReservedForCandidateConfirmation()
    {
        Assert.True(ChatInputKeyPolicy.ShouldDeferEnterToIme(
            Key.Enter, KeyModifiers.None, hasActivePreedit: true));
        Assert.False(ChatInputKeyPolicy.ShouldDeferEnterToIme(
            Key.Enter, KeyModifiers.None, hasActivePreedit: false));
        Assert.False(ChatInputKeyPolicy.ShouldDeferEnterToIme(
            Key.Enter, KeyModifiers.Shift, hasActivePreedit: true));
    }

    [Fact]
    public void PlainEnterLineBreakIsRemovedForSending()
    {
        Assert.True(ChatInputKeyPolicy.TryRemoveLineBreakBeforeCaret(
            "你好\n", 3, out var text, out var caret));
        Assert.Equal("你好", text);
        Assert.Equal(2, caret);
    }

    [Fact]
    public void PlainEnterReplacesSelectionWithoutKeepingLineBreak()
    {
        Assert.True(ChatInputKeyPolicy.TryRemoveLineBreakBeforeCaret(
            "你\n吗", 2, out var text, out var caret));
        Assert.Equal("你吗", text);
        Assert.Equal(1, caret);
    }

    [Fact]
    public void ImeChineseCandidateCommitIsNotTreatedAsSend()
    {
        Assert.False(ChatInputKeyPolicy.TryRemoveLineBreakBeforeCaret(
            "你", 1, out _, out _));
    }

    [Fact]
    public void UnchangedTextIsNotTreatedAsSend()
    {
        Assert.False(ChatInputKeyPolicy.TryRemoveLineBreakBeforeCaret(
            "你好", 2, out _, out _));
    }
}
