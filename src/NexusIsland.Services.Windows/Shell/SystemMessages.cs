using NexusIsland.Services.Windows.Interop;

namespace NexusIsland.Services.Windows.Shell;

/// <summary>Eventos del sistema que llegan como mensajes de ventana.</summary>
public enum SystemEvent
{
    None,

    /// <summary>La PC se va a suspender o hibernar.</summary>
    Suspending,

    /// <summary>La PC volvió de suspensión o hibernación.</summary>
    Resumed,

    /// <summary>
    /// La barra de tareas se (re)creó: el Explorador de Windows se reinició o se colgó.
    /// Los íconos de la bandeja hay que volver a agregarlos (WinForms NotifyIcon ya lo hace solo).
    /// </summary>
    TaskbarCreated,
}

public static class SystemMessages
{
    // Mensaje "registrado": Windows le asigna un número al vuelo, igual para todas las apps.
    private static readonly uint TaskbarCreatedMessage = NativeMethods.RegisterWindowMessage("TaskbarCreated");

    public static SystemEvent Classify(int msg, IntPtr wParam)
    {
        if (msg == NativeMethods.WM_POWERBROADCAST)
        {
            return wParam.ToInt64() switch
            {
                NativeMethods.PBT_APMSUSPEND => SystemEvent.Suspending,
                NativeMethods.PBT_APMRESUMEAUTOMATIC => SystemEvent.Resumed,
                _ => SystemEvent.None,
            };
        }
        if (TaskbarCreatedMessage != 0 && (uint)msg == TaskbarCreatedMessage) return SystemEvent.TaskbarCreated;
        return SystemEvent.None;
    }
}
