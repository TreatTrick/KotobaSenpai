using KotobaSenpai.Tools.PitchAccent;

const string revision = "9ebca4589565c696e7c39e089e2e928ad65f678c";
const string sourceUrl = $"https://raw.githubusercontent.com/mifunetoshiro/kanjium/{revision}/data/source_files/raw/accents.txt";
const string sourceHash = "8BD0DD127DAB32CEEC94CB03AB1BA6B68858EA73421DFA1731AF2F373DEB4F20";

if (args is not (["--output", _] or ["--output", _, "--source", _]))
{
    Console.Error.WriteLine("Usage: PitchAccentIndexBuilder --output <database> [--source <accents.txt>]");
    return 2;
}

try
{
    byte[] bytes;
    var localSource = args.Length == 4;
    if (localSource)
        bytes = await File.ReadAllBytesAsync(args[3]);
    else
    {
        using var client = new HttpClient();
        bytes = await client.GetByteArrayAsync(sourceUrl);
    }
    var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
    if (!localSource && hash != sourceHash)
        throw new InvalidDataException("The pinned Kanjium source failed SHA-256 verification.");

    var lines = System.Text.Encoding.UTF8.GetString(bytes).Split('\n');
    var count = PitchAccentIndex.Build(args[1], lines, hash == sourceHash ? revision : $"sha256:{hash}");
    Console.WriteLine($"Indexed {count:N0} pitch accent entries.");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Pitch accent index build failed: {exception.Message}");
    return 1;
}
