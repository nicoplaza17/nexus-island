using System.ComponentModel;
using System.Windows;

namespace NexusIsland.Modules.Security;

/// <summary>Detalle de una alerta: evidencia y explicación de la IA local (se va escribiendo a medida que llega).</summary>
public partial class SecurityWindow : Window
{
    private readonly SecurityAlertViewModel _vm;

    public SecurityWindow(SecurityAlertViewModel vm)
    {
        _vm = vm;
#pragma warning disable WPF0001
        ThemeMode = ThemeMode.System;
#pragma warning restore WPF0001
        InitializeComponent();
        DataContext = vm;
        // Al llegar texto nuevo, se baja hasta el final para ir leyendo.
        vm.PropertyChanged += OnVmChanged;
        Closed += (_, _) => vm.PropertyChanged -= OnVmChanged;
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SecurityAlertViewModel.Explanation)) ExplanationBox.ScrollToEnd();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
