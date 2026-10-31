using System.IO;
using System.Windows.Media.Imaging;

namespace KotobaSenpai.App.Support;

/// <summary>One bounded cache shared by both mascots; decoding and file access stay off the dispatcher.</summary>
internal sealed class MascotFrames
{
    internal static MascotFrames Shared { get; } = new();
    internal const int Capacity = 40;
    private readonly object _gate = new();
    private readonly Dictionary<string, BitmapSource> _frames = [];
    private readonly LinkedList<string> _recent = [];
    private readonly Dictionary<string, Task<BitmapSource>> _pending = [];
    internal int Count { get { lock (_gate) return _frames.Count; } }

    public Task<BitmapSource> GetAsync(string relativePath)
    {
        lock (_gate)
        {
            if (_frames.TryGetValue(relativePath, out var image))
            {
                _recent.Remove(relativePath);
                _recent.AddLast(relativePath);
                return Task.FromResult(image);
            }
            if (_pending.TryGetValue(relativePath, out var pending)) return pending;
            var task = Task.Run(() => Decode(relativePath));
            _pending.Add(relativePath, task);
            return task;
        }
    }

    private BitmapSource Decode(string path)
    {
        try
        {
            using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Assets", "Support", path));
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            lock (_gate)
            {
                if (_frames.Count == Capacity)
                {
                    _frames.Remove(_recent.First!.Value);
                    _recent.RemoveFirst();
                }
                _frames.Add(path, image);
                _recent.AddLast(path);
            }
            return image;
        }
        finally { lock (_gate) _pending.Remove(path); }
    }
}
