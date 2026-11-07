using System.Globalization;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace KotobaSenpai.Platform.Windows.Ocr.MeikiOcr;

/// <summary>
/// Decodes recognition output into lines: maps character boxes back to frame pixels, applies the punctuation
/// confidence factor, resolves overlaps with per-character NMS, orders characters left to right and fixes the
/// kanji pairs the model reads with swapped halves. A faithful port of the postprocessing in <c>ocr.py</c>.
/// </summary>
internal static class MeikiOcrResultDecoder
{
    private const float XOverlapThreshold = 0.3f;
    private const float Epsilon = 1e-6f;

    // Corresponds to SWAPPED_PAIRS in ocr.py (common kanji pairs the model reads with left/right parts swapped).
    private static readonly IReadOnlyDictionary<string, string> SwappedPairs = new Dictionary<string, string>
    {
        ["儡傀"] = "傀儡", ["談冗"] = "冗談", ["汰淘"] = "淘汰", ["沱滂"] = "滂沱",
        ["攣痙"] = "痙攣", ["酊酩"] = "酩酊", ["麭麺"] = "麺麭", ["哭慟"] = "慟哭",
    };

    /// <summary>Returns one line per detection box that produced accepted characters, in detection order.</summary>
    internal static IReadOnlyList<MeikiLine> Decode(
        IReadOnlyList<Tensor<int>> labelsChunks,
        IReadOnlyList<Tensor<float>> boxesChunks,
        IReadOnlyList<Tensor<float>> scoresChunks,
        IReadOnlyList<int> validIndices,
        IReadOnlyList<CropMeta> metadata,
        int lineCount,
        float recognitionThreshold,
        float punctuationConfFactor)
    {
        var candidatesByIndex = new Dictionary<int, List<Candidate>>();

        int chunkOffset = 0;
        for (int c = 0; c < labelsChunks.Count; c++)
        {
            var labels = labelsChunks[c];
            var boxes = boxesChunks[c];
            var scores = scoresChunks[c];
            int batchLength = labels.Dimensions[0];

            for (int i = 0; i < batchLength; i++)
            {
                int originalIndex = validIndices[chunkOffset + i];
                var meta = metadata[chunkOffset + i];
                int cropWidth = meta.X2 - meta.X1, cropHeight = meta.Y2 - meta.Y1;
                int characters = labels.Dimensions[1];

                if (!candidatesByIndex.TryGetValue(originalIndex, out var candidates))
                {
                    candidates = new List<Candidate>();
                    candidatesByIndex[originalIndex] = candidates;
                }

                for (int j = 0; j < characters; j++)
                {
                    float score = scores[i, j];
                    if (score < recognitionThreshold)
                        continue;

                    char character = (char)labels[i, j];
                    float rx1 = boxes[i, j, 0], ry1 = boxes[i, j, 1], rx2 = boxes[i, j, 2], ry2 = boxes[i, j, 3];

                    // Horizontal mapping (corresponding to the non-vertical branch in ocr.py).
                    int effectiveWidth = meta.EffectiveW;
                    if (rx1 >= effectiveWidth)
                        continue;
                    rx1 = MathF.Min(rx1, effectiveWidth);
                    rx2 = MathF.Min(rx2, effectiveWidth);

                    float cx1 = rx1 / effectiveWidth * cropWidth;
                    float cx2 = rx2 / effectiveWidth * cropWidth;
                    float cy1 = ry1 / MeikiOcrPreprocessor.InputRecHeight * cropHeight;
                    float cy2 = ry2 / MeikiOcrPreprocessor.InputRecHeight * cropHeight;

                    int gx1 = meta.X1 + (int)cx1;
                    int gy1 = meta.Y1 + (int)cy1;
                    int gx2 = meta.X1 + (int)cx2;
                    int gy2 = meta.Y1 + (int)cy2;

                    candidates.Add(new Candidate(character, gx1, gy1, gx2, gy2, score, (gx1, gx2)));
                }
            }
            chunkOffset += batchLength;
        }

        var results = new MeikiLine?[lineCount];
        foreach (var (originalIndex, candidates) in candidatesByIndex)
        {
            if (punctuationConfFactor != 1.0f)
                foreach (var candidate in candidates)
                    if (IsPunctuation(candidate.Char))
                        candidate.Conf *= punctuationConfFactor;

            candidates.Sort((a, b) => b.Conf.CompareTo(a.Conf));

            var accepted = new List<Candidate>();
            var acceptedIntervals = new List<(int Start, int End)>();

            foreach (var candidate in candidates)
            {
                int length = candidate.Interval.End - candidate.Interval.Start + (int)Epsilon;
                bool overlaps = false;
                foreach (var (start, end) in acceptedIntervals)
                {
                    if (candidate.Interval.Start >= end || start >= candidate.Interval.End)
                        continue;
                    int intersectionStart = Math.Max(candidate.Interval.Start, start);
                    int intersectionEnd = Math.Min(candidate.Interval.End, end);
                    int intersectionLength = Math.Max(0, intersectionEnd - intersectionStart);
                    int acceptedLength = end - start + (int)Epsilon;
                    int shortest = Math.Min(length, acceptedLength);
                    if (intersectionLength / (float)shortest > XOverlapThreshold)
                    {
                        overlaps = true;
                        break;
                    }
                }
                if (!overlaps)
                {
                    accepted.Add(candidate);
                    acceptedIntervals.Add(candidate.Interval);
                }
            }

            accepted.Sort((a, b) => a.Interval.Start.CompareTo(b.Interval.Start));
            var text = FixSwappedPairs(new string(accepted.Select(c => c.Char).ToArray()), accepted);
            results[originalIndex] = new MeikiLine(text,
                accepted.Select(c => new MeikiChar(c.Char, c.X1, c.Y1, c.X2, c.Y2, c.Conf)).ToArray());
        }

        return results.Where(line => line is not null).Cast<MeikiLine>().ToArray();
    }

    // Corresponds to _fix_swapped_pairs in ocr.py.
    private static string FixSwappedPairs(string text, List<Candidate> characters)
    {
        foreach (var (wrong, correct) in SwappedPairs)
        {
            int index = text.IndexOf(wrong, StringComparison.Ordinal);
            if (index != -1 && index + 1 < characters.Count)
            {
                text = text[..index] + correct + text[(index + 2)..];
                (characters[index], characters[index + 1]) = (characters[index + 1], characters[index]);
            }
        }
        return text;
    }

    private static bool IsPunctuation(char character)
        => char.GetUnicodeCategory(character) switch
        {
            UnicodeCategory.ConnectorPunctuation or UnicodeCategory.DashPunctuation
                or UnicodeCategory.OpenPunctuation or UnicodeCategory.ClosePunctuation
                or UnicodeCategory.InitialQuotePunctuation or UnicodeCategory.FinalQuotePunctuation
                or UnicodeCategory.OtherPunctuation => true,
            _ => false,
        };

    private sealed class Candidate
    {
        public Candidate(char character, int x1, int y1, int x2, int y2, float conf, (int Start, int End) interval)
        {
            Char = character;
            X1 = x1;
            Y1 = y1;
            X2 = x2;
            Y2 = y2;
            Conf = conf;
            Interval = interval;
        }

        public char Char { get; set; }
        public int X1 { get; }
        public int Y1 { get; }
        public int X2 { get; }
        public int Y2 { get; }
        public float Conf { get; set; }
        public (int Start, int End) Interval { get; }
    }
}
