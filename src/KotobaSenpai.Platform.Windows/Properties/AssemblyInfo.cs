using System.Runtime.CompilerServices;

// Allows the platform test project to access internal types (MeikiOcrEngine, etc.) for end-to-end golden tests.
[assembly: InternalsVisibleTo("KotobaSenpai.Platform.Windows.Tests")]
// Presentation integration tests verify that native overlay handles are registered and released.
[assembly: InternalsVisibleTo("KotobaSenpai.Presentation.Wpf.Tests")]
// The overlay window registry is native-handle state owned by this assembly and read by the WPF
// presentation assembly, so the presentation layer sees it without a public mutable surface.
[assembly: InternalsVisibleTo("KotobaSenpai.Presentation.Wpf")]
