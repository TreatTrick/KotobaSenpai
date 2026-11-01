using KotobaSenpai.Core.Logging;
using KotobaSenpai.Core.Settings;
using KotobaSenpai.Core.Overlay;
using KotobaSenpai.App.Resources;

namespace KotobaSenpai.App.Guidance;

public sealed record TourStep(GuidanceTarget Target, string TextKey);

public sealed class TourSession
{
    private readonly ISettingsService _settings;
    private readonly ILogger _logger;
    private readonly string _completionKey;
    private readonly TourStep[] _steps;
    private int _index = -1;
    private bool _finished;

    internal TourSession(ISettingsService settings, ILogger logger, string completionKey, params TourStep[] steps)
    {
        _settings = settings;
        _logger = logger;
        _completionKey = completionKey;
        _steps = steps;
        _finished = settings.GetValue(completionKey) == ContextualGuidance.CompletedValue;
    }

    public TourStep? Current => _index < 0 ? null : _steps[_index];
    public event EventHandler? Changed;

    public void Start()
    {
        if (_finished || Current is not null) return;
        _index = 0;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Advance()
    {
        if (Current is null) return;
        if (++_index == _steps.Length)
        {
            _index = -1;
            _finished = true;
            try { _settings.SetValue(_completionKey, ContextualGuidance.CompletedValue); }
            catch (Exception exception) { _logger.LogError(exception, "Failed to save tour completion: {0}.", _completionKey); }
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Stop()
    {
        if (Current is null) return;
        _index = -1;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

public sealed class ContextualGuidance
{
    public const string MainCompletionKey = "MainInterfaceTourCompleted";
    public const string RegionCompletionKey = "RegionSelectorTourCompleted";
    public const string CompletedValue = "true";

    public ContextualGuidance(ISettingsService settings, ILogger logger)
    {
        Main = new(settings, logger, MainCompletionKey,
            new(GuidanceTarget.SelectWindow, ResourceKeys.Guide_SelectWindow),
            new(GuidanceTarget.Recognize, ResourceKeys.Guide_Recognize),
            new(GuidanceTarget.RecognitionRegion, ResourceKeys.Guide_RecognitionRegion),
            new(GuidanceTarget.AutoRecognition, ResourceKeys.Guide_AutoRecognition));
        Region = new(settings, logger, RegionCompletionKey,
            new(GuidanceTarget.RegionTopLeft, ResourceKeys.Guide_RegionTopLeft),
            new(GuidanceTarget.RegionConfirm, ResourceKeys.Guide_RegionConfirm));
    }

    public TourSession Main { get; }
    public TourSession Region { get; }
    public bool IsActive => Main.Current is not null || Region.Current is not null;
}
