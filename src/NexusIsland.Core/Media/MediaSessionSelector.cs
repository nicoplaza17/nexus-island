namespace NexusIsland.Core.Media;

/// <summary>Una sesión multimedia abierta (para elegir cuál mostrar).</summary>
public readonly record struct SessionCandidate(string AppId, MediaPlaybackStatus Status);

/// <summary>
/// Elige qué sesión mostrar respetando las apps ignoradas:
///  1. La que Windows considera "actual", si no está ignorada.
///  2. Si no, la primera que esté sonando y no esté ignorada.
///  3. Si no, la primera no ignorada (pausada, por ejemplo).
///  4. Si todas están ignoradas, ninguna.
/// Devuelve el índice en <paramref name="sessions"/> o -1.
/// </summary>
public static class MediaSessionSelector
{
    public static int Choose(string? currentAppId, IReadOnlyList<SessionCandidate> sessions, Func<string, bool> isIgnored)
    {
        if (currentAppId is not null && !isIgnored(currentAppId))
        {
            for (var i = 0; i < sessions.Count; i++)
                if (string.Equals(sessions[i].AppId, currentAppId, StringComparison.OrdinalIgnoreCase)) return i;
        }

        for (var i = 0; i < sessions.Count; i++)
            if (sessions[i].Status == MediaPlaybackStatus.Playing && !isIgnored(sessions[i].AppId)) return i;

        for (var i = 0; i < sessions.Count; i++)
            if (!isIgnored(sessions[i].AppId)) return i;

        return -1;
    }
}
