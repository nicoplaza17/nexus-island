using System.Windows.Input;
using NexusIsland.Core.Activities;
using NexusIsland.App.Properties;
using NexusIsland.Core.Mvvm;

namespace NexusIsland.App.Island;

/// <summary>Un botón del panel de herramientas.</summary>
public sealed class LauncherItem(QuickAction action, ICommand command)
{
    public string Label => action.Label;
    public string Glyph => action.Glyph;
    public ICommand Command => command;
}

/// <summary>Panel de herramientas: reúne los botones que aportan los módulos activos.</summary>
public sealed class LauncherViewModel : ObservableObject, IAccessibleActivity
{
    public LauncherViewModel(IEnumerable<IQuickActionProvider> providers, Action<QuickAction> run)
    {
        Items = providers
            .SelectMany(p => p.GetQuickActions())
            .OrderBy(a => a.Order)
            .Select(a => new LauncherItem(a, new AsyncCommand<object>(_ => { run(a); return Task.CompletedTask; })))
            .ToList();
    }

    public IReadOnlyList<LauncherItem> Items { get; }

    /// <summary>Accesibilidad: lo que lee el Narrador.</summary>
    public string AccessibleSummary => string.Format(Strings.A11y_Launcher, string.Join(", ", Items.Select(i => i.Label)));
}
