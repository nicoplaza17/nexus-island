namespace NexusIsland.Core.Security;

/// <summary>Explica una alerta con un modelo de lenguaje local (no decide si hay ataque: eso lo hacen las reglas).</summary>
public interface ISecurityExplainer
{
    /// <summary>Nombre del modelo que se usa (para mostrarlo).</summary>
    string ModelName { get; }

    /// <summary>
    /// Pide la explicación. <paramref name="onText"/> recibe el texto a medida que se genera (en otro hilo).
    /// <paramref name="onStatus"/> avisa en qué etapa está (arrancando el servidor, cargando el modelo…).
    /// </summary>
    Task ExplainAsync(SecurityAlert alert, Action<string> onText, Action<ExplainStage> onStatus, CancellationToken cancellationToken);
}

public enum ExplainStage { StartingServer, LoadingModel, Writing }

/// <summary>Arma el pedido para el modelo: instrucciones claras y solo datos reales de la evidencia.</summary>
public static class SecurityPrompt
{
    public static string System(bool spanish) => spanish
        ? "Sos un analista de ciberseguridad defensiva. Un vigilante en una notebook con Windows 11 detectó la alerta que te paso. " +
          "Respondé en español rioplatense, en lenguaje simple y breve (máximo 220 palabras), con estas secciones: " +
          "1) Qué pasó. 2) Qué tan grave es y por qué. 3) Cómo verificar si es legítimo. 4) Qué hacer ahora (pasos concretos). " +
          "Te sugiero una técnica de MITRE ATT&CK: confirmala o corregila solo si estás seguro. " +
          "Usá únicamente los datos de la evidencia; si falta información, decilo en vez de inventarla. " +
          "Importante: las reglas detectan patrones (por ejemplo, palabras dentro de un script); eso no prueba que algo se haya " +
          "ejecutado o descargado. No afirmes que ocurrió algo que la evidencia no muestra: decí qué habría que confirmar."
        : "You are a defensive cybersecurity analyst. A watcher on a Windows 11 laptop raised the alert below. " +
          "Answer in English, plain and short (max 220 words), with these sections: 1) What happened. 2) How serious it is and why. " +
          "3) How to check whether it is legitimate. 4) What to do now (concrete steps). A MITRE ATT&CK technique is suggested: " +
          "confirm or correct it only if you are sure. Use only the evidence; if information is missing, say so instead of making it up. " +
          "Important: the rules detect patterns (for example, words inside a script); that does not prove anything was executed or " +
          "downloaded. Do not claim something happened unless the evidence shows it: say what should be confirmed.";

    public static string User(SecurityAlert a) =>
        $"Alerta: {a.Title}\nResumen: {a.Detail}\nGravedad asignada por la regla: {a.Severity}\nMITRE sugerido: {a.Mitre}\n\nEvidencia:\n{a.Evidence}";
}
