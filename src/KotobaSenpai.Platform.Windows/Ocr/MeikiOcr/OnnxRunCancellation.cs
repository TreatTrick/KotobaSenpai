using Microsoft.ML.OnnxRuntime;

namespace KotobaSenpai.Platform.Windows.Ocr.MeikiOcr;

/// <summary>Connects managed cancellation to ONNX termination and preserves cancellation error semantics.</summary>
internal static class OnnxRunCancellation
{
    public static T Execute<T>(CancellationToken cancellationToken, Func<RunOptions, T> run)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var options = new RunOptions();
        using var registration = cancellationToken.Register(() => options.Terminate = true);
        try
        {
            var result = run(options);
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        catch (OnnxRuntimeException exception) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException("OCR inference was cancelled.", exception, cancellationToken);
        }
    }
}
