using KotobaSenpai.Core.Contracts;
using KotobaSenpai.Core.Models;

namespace KotobaSenpai.Core.Services;

/// <summary>
/// 编排 phrase 分析：先做本地句段划分与 token 引用，再并发调用提供方（每句独立请求，限并发），
/// 校验 group、分配会话 ID、映射几何并保留提供方顺序。任何失败都只产生可重试警告，不抛穿识别流程，
/// 本地词/span 保持可用。
/// </summary>
public sealed class PhraseAnalysisOrchestrator
{
    // ponytail: 并发上限 4——典型 VN 对话框 2~5 句，超过是对 DeepSeek 温和；若遇 429 或需更高吞吐再调。
    private const int MaxConcurrency = 4;

    private readonly ILlmPhraseAnalyzer _analyzer;
    private readonly SentenceSegmenter _segmenter;
    private readonly SentenceTokenBuilder _tokenBuilder;

    public PhraseAnalysisOrchestrator(
        ILlmPhraseAnalyzer analyzer,
        SentenceSegmenter? segmenter = null,
        SentenceTokenBuilder? tokenBuilder = null)
    {
        _analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
        _segmenter = segmenter ?? new SentenceSegmenter();
        _tokenBuilder = tokenBuilder ?? throw new ArgumentNullException(nameof(tokenBuilder));
    }

    public async Task<PhraseAnalysisRun> AnalyzeAsync(
        IReadOnlyList<OcrLine> lines,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lines);

        // 先纯本地构建请求（快、零 I/O），保持 segment 顺序；Task.WhenAll 按输入顺序返回结果，故无需再排序。
        var requests = BuildRequests(lines);
        if (requests.Count == 0)
            return new PhraseAnalysisRun(PhraseAnalysisOutcome.Success, [], null);

        PhraseAnalysisResult[] results;
        try
        {
            results = await RunThrottledAsync(requests, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Failed(PhraseAnalysisOutcome.Cancelled, "Phrase analysis cancelled.", []);
        }

        // 逐句独立评估：失败句记 warning 跳过，其余照常，符合"部分可用"定位。
        var groups = new List<PhraseGroup>();
        var warnings = new List<string>();
        PhraseAnalysisOutcome? firstFailure = null;

        for (int i = 0; i < results.Length; i++)
        {
            var result = results[i];
            if (!result.Succeeded)
            {
                var warning = result.Warning ?? DescribeFailure(result.Outcome);
                if (!warnings.Contains(warning))
                    warnings.Add(warning);
                firstFailure ??= result.Outcome;
                continue;
            }

            var tokenById = requests[i].Tokens.ToDictionary(reference => reference.Id);
            var validation = PhraseGroupValidator.ValidateAndBuild(result.Groups, tokenById);
            warnings.AddRange(validation.Warnings);
            groups.AddRange(validation.ValidGroups.Select(group => group.WithSessionId(Guid.NewGuid())));
        }

        // 只要任一（能处理的）句成功即整体 Success；全失败才取首个失败类别。
        var outcome = groups.Count > 0 || results.Any(result => result.Succeeded)
            ? PhraseAnalysisOutcome.Success
            : firstFailure ?? PhraseAnalysisOutcome.Success;

        return new PhraseAnalysisRun(
            outcome,
            groups.Select(PhraseGeometryMapper.MapGroup).ToArray(),
            warnings.Count > 0 ? string.Join("; ", warnings) : null);
    }

    private IReadOnlyList<PhraseAnalysisRequest> BuildRequests(IReadOnlyList<OcrLine> lines)
    {
        var requests = new List<PhraseAnalysisRequest>();
        foreach (var segment in _segmenter.Segment(lines))
        {
            var segmentTokens = _tokenBuilder.Build(lines, segment);
            if (segmentTokens.References.Count == 0)
                continue;

            requests.Add(new PhraseAnalysisRequest(
                segment.SegmentId,
                string.Concat(segment.LineIndices.Select(i => lines[i].Text)),
                segmentTokens.References,
                segmentTokens.LocalSpans));
        }
        return requests;
    }

    // ponytail: 限流逻辑收进私有方法，主流程平铺可读。
    private async Task<PhraseAnalysisResult[]> RunThrottledAsync(
        IReadOnlyList<PhraseAnalysisRequest> requests,
        CancellationToken cancellationToken)
    {
        using var throttler = new SemaphoreSlim(MaxConcurrency);
        var tasks = requests.Select(async request =>
        {
            await throttler.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await _analyzer.AnalyzeAsync(request, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                throttler.Release();
            }
        });
        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private static PhraseAnalysisRun Failed(
        PhraseAnalysisOutcome outcome,
        string warning,
        IReadOnlyList<string> priorWarnings)
    {
        var all = priorWarnings.ToList();
        all.Add(warning);
        return new PhraseAnalysisRun(outcome, Array.Empty<PhraseGroupView>(), string.Join("; ", all));
    }

    private static string DescribeFailure(PhraseAnalysisOutcome outcome) => outcome switch
    {
        PhraseAnalysisOutcome.NoKey => "Phrase analysis requires provider configuration.",
        PhraseAnalysisOutcome.Timeout => "Phrase analysis provider timed out.",
        PhraseAnalysisOutcome.Refused => "Phrase analysis provider refused the request.",
        PhraseAnalysisOutcome.MalformedJson => "Phrase analysis provider returned malformed JSON.",
        PhraseAnalysisOutcome.TransportError => "Phrase analysis provider transport error.",
        PhraseAnalysisOutcome.InvalidResponse => "Phrase analysis provider returned an invalid response.",
        _ => "Phrase analysis failed.",
    };
}