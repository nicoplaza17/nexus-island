# Componentes de terceros

Nexus Island usa los siguientes componentes. Gracias a sus autores.

| Componente | Uso | Licencia |
|---|---|---|
| [Opus-MT en-es / es-en](https://huggingface.co/Helsinki-NLP) — Helsinki-NLP (Language Technology Research Group, Universidad de Helsinki) | Modelos de traducción. No vienen con la app: se bajan al primer uso | [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/) |
| [Xenova/opus-mt-en-es](https://huggingface.co/Xenova/opus-mt-en-es) y [Xenova/opus-mt-es-en](https://huggingface.co/Xenova/opus-mt-es-en) | Conversión de esos modelos a ONNX (versión int8) | CC BY 4.0 (la del modelo original) |
| [ONNX Runtime](https://github.com/microsoft/onnxruntime) — Microsoft | Ejecuta los modelos de traducción | MIT |
| [Microsoft.ML.Tokenizers](https://github.com/dotnet/machinelearning) — .NET Foundation | Divide el texto en piezas (SentencePiece) | MIT |
| [Microsoft.Extensions.DependencyInjection / Logging](https://github.com/dotnet/runtime) — .NET Foundation | Infraestructura de la app | MIT |
| [.NET](https://github.com/dotnet/runtime) — .NET Foundation | Incluido en el instalador | MIT |

Referencia de los modelos: Jörg Tiedemann y Santhosh Thottingal, *OPUS-MT — Building open translation services for the
World*, EAMT 2020.
