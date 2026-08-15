using System.Runtime.CompilerServices;

// Allows the platform test project to access internal types (MeikiOcrEngine, etc.) for end-to-end golden tests.
[assembly: InternalsVisibleTo("KotobaSenpai.Platform.Windows.Tests")]