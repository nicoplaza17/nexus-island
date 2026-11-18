using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using NexusIsland.Core.Media;

namespace NexusIsland.Services.Windows.Media;

/// <summary>
/// Nivel de sonido de la salida de audio predeterminada, con el medidor de picos de Windows
/// (Core Audio, IAudioMeterInformation): el mismo que usa el mezclador de volumen. No graba ni
/// escucha el audio: Windows solo informa cuán fuerte suena. Incluye todo lo que suena por esos
/// parlantes (si suena otra cosa a la vez, también se ve).
/// </summary>
public sealed class CoreAudioLevelMeter(ILogger<CoreAudioLevelMeter> logger) : IAudioLevelMeter, IDisposable
{
    private const int eRender = 0, eMultimedia = 1, CLSCTX_ALL = 23;
    private static readonly Guid CLSID_MMDeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid IID_IAudioMeterInformation = new("C02216F6-8C67-4B5B-9D00-D008E73E0064");

    private IMMDeviceEnumerator? _enumerator;
    private IAudioMeterInformation? _meter;
    private readonly float[] _channels = new float[2];
    private DateTime _retryAfter;

    public bool TryRead(out float left, out float right)
    {
        left = right = 0;
        if (_meter is null && !TryOpen()) return false;
        try
        {
            Marshal.ThrowExceptionForHR(_meter!.GetMeteringChannelCount(out var count));
            if (count >= 2)
            {
                Marshal.ThrowExceptionForHR(_meter.GetChannelsPeakValues(2, _channels));
                (left, right) = (_channels[0], _channels[1]);
            }
            else
            {
                Marshal.ThrowExceptionForHR(_meter.GetPeakValue(out var peak));
                left = right = peak;
            }
            return true;
        }
        catch (Exception ex)
        {
            // Cambió el dispositivo (auriculares, HDMI…) o se desconectó: se vuelve a abrir el predeterminado.
            logger.LogDebug(ex, "Medidor de audio inválido: se reabre");
            Close();
            return false;
        }
    }

    private bool TryOpen()
    {
        if (DateTime.UtcNow < _retryAfter) return false;
        try
        {
            _enumerator ??= (IMMDeviceEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(CLSID_MMDeviceEnumerator)!)!;
            Marshal.ThrowExceptionForHR(_enumerator.GetDefaultAudioEndpoint(eRender, eMultimedia, out var device));
            try
            {
                var iid = IID_IAudioMeterInformation;
                Marshal.ThrowExceptionForHR(device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out var meter));
                _meter = (IAudioMeterInformation)meter;
            }
            finally
            {
                Marshal.ReleaseComObject(device);
            }
            logger.LogInformation("Medidor de nivel de audio abierto (salida predeterminada)");
            return true;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "No hay medidor de audio disponible");
            _retryAfter = DateTime.UtcNow.AddSeconds(5); // sin parlantes: no reintentar en cada cuadro
            Close();
            return false;
        }
    }

    private void Close()
    {
        if (_meter is not null) Marshal.ReleaseComObject(_meter);
        _meter = null;
    }

    public void Dispose()
    {
        Close();
        if (_enumerator is not null) Marshal.ReleaseComObject(_enumerator);
        _enumerator = null;
    }

    // ------------------------------------------------------------------ Core Audio (COM)

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    }

    [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioMeterInformation
    {
        [PreserveSig] int GetPeakValue(out float peak);
        [PreserveSig] int GetMeteringChannelCount(out int count);
        [PreserveSig] int GetChannelsPeakValues(int count, [Out, MarshalAs(UnmanagedType.LPArray)] float[] values);
        [PreserveSig] int QueryHardwareSupport(out int mask);
    }
}
