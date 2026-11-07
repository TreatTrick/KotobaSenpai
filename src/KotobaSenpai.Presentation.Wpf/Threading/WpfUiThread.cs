using System.Windows.Threading;
using KotobaSenpai.Core.Threading;

namespace KotobaSenpai.Presentation.Wpf.Threading;

/// <summary>Adapts the WPF dispatcher to the UI ownership contract consumed by native adapters.</summary>
public sealed class WpfUiThread(Dispatcher dispatcher) : IUiThread
{
    public bool CheckAccess() => dispatcher.CheckAccess();
    public void Post(Action action) => dispatcher.BeginInvoke(action, DispatcherPriority.DataBind);
    public T Invoke<T>(Func<T> action) => dispatcher.CheckAccess() ? action() : dispatcher.Invoke(action);
}
