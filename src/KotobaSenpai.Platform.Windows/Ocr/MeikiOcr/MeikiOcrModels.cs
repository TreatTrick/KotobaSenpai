namespace KotobaSenpai.Platform.Windows.Ocr.MeikiOcr;

/// <summary>A single recognized character and its pixel box in the captured frame.</summary>
internal sealed record MeikiChar(char Char, int X1, int Y1, int X2, int Y2, float Conf);

/// <summary>A single recognized text line.</summary>
internal sealed record MeikiLine(string Text, IReadOnlyList<MeikiChar> Chars);

/// <summary>A detected text box in frame pixel coordinates.</summary>
internal readonly record struct MeikiBox(int X1, int Y1, int X2, int Y2);

/// <summary>Crop geometry needed to map recognition-relative character boxes back to frame pixels.</summary>
internal sealed record CropMeta(int X1, int Y1, int X2, int Y2, int EffectiveW, int EffectiveH);
