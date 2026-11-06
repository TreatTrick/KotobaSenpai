using KotobaSenpai.Core.Geometry;
using KotobaSenpai.Core.TargetWindow;
using KotobaSenpai.Core.Logging;

namespace KotobaSenpai.Core.Ocr;

/// <summary>Confirms OCR text locally before returning a snapshot for cloud analysis.</summary>
public sealed class StableWindowWordRecognizer(IWindowWordRecognizer recognizer, TimeProvider? timeProvider = null,
    ILogger? logger = null) : IWindowWordRecognizer
{
    private static readonly TimeSpan SampleInterval = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan ConfirmationWindow = TimeSpan.FromMilliseconds(600);
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    public async Task<WordRecognitionResult> RecognizeAsync(Guid recognitionId, WindowTarget target,
        CancellationToken cancellationToken = default, ScreenRect? region = null)
    {
        var result = await recognizer.RecognizeAsync(recognitionId, target, cancellationToken, region).ConfigureAwait(false);
        var text = string.Concat(result.Lines.Select(line => line.Text));
        var changedAt = _clock.GetTimestamp();
        var samples = 1;
        while (true)
        {
            await Task.Delay(SampleInterval, _clock, cancellationToken).ConfigureAwait(false);
            result = await recognizer.RecognizeAsync(recognitionId, target, cancellationToken, region).ConfigureAwait(false);
            samples++;
            cancellationToken.ThrowIfCancellationRequested();
            var nextText = string.Concat(result.Lines.Select(line => line.Text));
            if (!string.Equals(text, nextText, StringComparison.Ordinal))
            {
                text = nextText;
                changedAt = _clock.GetTimestamp();
            }
            else if (_clock.GetElapsedTime(changedAt) >= ConfirmationWindow)
            {
                logger?.LogInformation("[OCR] Recognition {0:N}: confirmedSamples={1}, stableForMs={2:0}, characters={3}.",
                    recognitionId, samples, _clock.GetElapsedTime(changedAt).TotalMilliseconds, text.Length);
                return result;
            }
        }
    }
}
