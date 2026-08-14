namespace ChatApp.Core.Services;

/// <summary>P2: speech-to-text input.</summary>
public interface ISpeechToTextService
{
    Task<bool> IsAvailableAsync(CancellationToken ct = default);

    /// <summary>Starts capturing audio from the microphone.</summary>
    Task StartCaptureAsync(CancellationToken ct = default);

    /// <summary>Stops capturing and returns the recognized text.</summary>
    Task<string> StopCaptureAsync(CancellationToken ct = default);
}

/// <summary>P2: text-to-speech output.</summary>
public interface ITextToSpeechService
{
    Task<bool> IsAvailableAsync(CancellationToken ct = default);

    Task SpeakAsync(string text, CancellationToken ct = default);

    void Stop();
}

/// <summary>P2: tracks a role's affinity toward the user.</summary>
public interface IAffinityService
{
    Task<int> GetAsync(int roleId, CancellationToken ct = default);

    /// <summary>Adjusts affinity based on the latest user message and reply.</summary>
    Task<int> UpdateAsync(int roleId, string userMessage, string assistantReply, CancellationToken ct = default);

    Task SetAsync(int roleId, int value, CancellationToken ct = default);
}
