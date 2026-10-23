using System.ComponentModel;
using System.Globalization;

namespace KotobaSenpai.Core.Localization;

/// <summary>Supported UI cultures and the current persisted language selection.</summary>
public interface ILanguagePreferences : INotifyPropertyChanged
{
    IReadOnlyList<CultureInfo> AvailableCultures { get; }
    CultureInfo CurrentCulture { get; set; }
}
