using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace NexusIsland.Services.Windows.Monitoring;

/// <summary>
/// Temperatura APROXIMADA desde las "zonas térmicas ACPI" (contador de rendimiento de Windows).
///
/// Limitaciones reales (por eso la isla la muestra como "aprox." y no da alertas de temperatura):
///  - No es necesariamente la temperatura del procesador: es la que el fabricante expone por ACPI
///    (puede ser de la placa, de la CPU o un valor fijo). Algunas PCs no tienen ninguna zona.
///  - El sensor "exacto" de Windows (WMI MSAcpi_ThermalZoneTemperature) requiere administrador.
///  - La temperatura real del procesador solo la dan programas con drivers propios (HWiNFO, etc.).
///
/// Se usa PDH con nombres en inglés (PdhAddEnglishCounter): los nombres de contadores vienen
/// traducidos según el idioma de Windows ("Información sobre la zona térmica").
/// </summary>
internal sealed class ThermalZoneReader : IDisposable
{
    private const uint PDH_FMT_DOUBLE = 0x00000200;
    private const int PDH_MORE_DATA = unchecked((int)0x800007D2);
    private const double MinPlausibleC = 15, MaxPlausibleC = 110;

    private readonly ILogger _logger;
    private IntPtr _query, _counter;
    private bool _highPrecision;
    private bool _unavailable;

    public ThermalZoneReader(ILogger logger) => _logger = logger;

    /// <summary>La zona más caliente con un valor creíble, en °C; null si no hay sensores.</summary>
    public double? Read()
    {
        if (_unavailable) return null;
        try
        {
            if (_query == IntPtr.Zero && !Open()) return null;
            if (PdhCollectQueryData(_query) != 0) return null;

            uint size = 0, count = 0;
            var status = PdhGetFormattedCounterArray(_counter, PDH_FMT_DOUBLE, ref size, ref count, IntPtr.Zero);
            if (status != PDH_MORE_DATA || size == 0) return null;

            var buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                if (PdhGetFormattedCounterArray(_counter, PDH_FMT_DOUBLE, ref size, ref count, buffer) != 0) return null;
                var itemSize = Marshal.SizeOf<PDH_FMT_COUNTERVALUE_ITEM>();
                double? hottest = null;
                for (var i = 0; i < count; i++)
                {
                    var item = Marshal.PtrToStructure<PDH_FMT_COUNTERVALUE_ITEM>(buffer + i * itemSize);
                    if (item.CStatus != 0) continue;
                    // "High Precision Temperature" viene en décimas de Kelvin; "Temperature" en Kelvin.
                    var celsius = (_highPrecision ? item.DoubleValue / 10 : item.DoubleValue) - 273.15;
                    if (celsius is >= MinPlausibleC and <= MaxPlausibleC && (hottest is null || celsius > hottest))
                        hottest = celsius;
                }
                return hottest;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "No se pudo leer la temperatura");
            return null;
        }
    }

    private bool Open()
    {
        if (PdhOpenQuery(null, IntPtr.Zero, out _query) != 0)
        {
            _unavailable = true;
            return false;
        }
        _highPrecision = PdhAddEnglishCounter(_query, @"\Thermal Zone Information(*)\High Precision Temperature", IntPtr.Zero, out _counter) == 0;
        if (!_highPrecision && PdhAddEnglishCounter(_query, @"\Thermal Zone Information(*)\Temperature", IntPtr.Zero, out _counter) != 0)
        {
            _logger.LogInformation("Esta PC no expone zonas térmicas: no se muestra temperatura");
            _unavailable = true;
            return false;
        }
        _logger.LogInformation("Temperatura aproximada disponible (zonas térmicas ACPI)");
        return true;
    }

    public void Dispose()
    {
        if (_query != IntPtr.Zero) PdhCloseQuery(_query);
        _query = IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PDH_FMT_COUNTERVALUE_ITEM
    {
        public IntPtr Name;
        public uint CStatus;
        // En x64 el double queda alineado a 8 bytes (hay 4 bytes de relleno antes): lo maneja el marshaller.
        public double DoubleValue;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern int PdhOpenQuery(string? source, IntPtr userData, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern int PdhAddEnglishCounter(IntPtr query, string path, IntPtr userData, out IntPtr counter);
    [DllImport("pdh.dll")] private static extern int PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern int PdhGetFormattedCounterArray(IntPtr counter, uint format, ref uint bufferSize, ref uint itemCount, IntPtr buffer);
    [DllImport("pdh.dll")] private static extern int PdhCloseQuery(IntPtr query);
}
