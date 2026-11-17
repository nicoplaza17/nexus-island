using System.Text;
using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;
using NexusIsland.Core.Translation;

namespace NexusIsland.Translation;

/// <summary>
/// Traductor Marian (Opus-MT) exportado a ONNX: codificador + decodificador con caché, decodificación
/// "greedy" (elige la palabra más probable en cada paso; 0,3-0,9 s por frase en una notebook).
/// </summary>
public sealed class MarianTranslator : IDisposable
{
    private readonly InferenceSession _encoder, _decoder;
    private readonly SentencePieceTokenizer _sourceSpm;
    private readonly Dictionary<string, int> _vocab;
    private readonly string[] _idToToken;
    private readonly int _eos, _pad, _unk, _layers, _heads, _headDim;

    public MarianTranslator(string directory)
    {
        // Sin el "arena" de memoria de ONNX Runtime: un poco más lento, pero ~130 MB menos (medido en la prueba).
        using var options = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            IntraOpNumThreads = Math.Clamp(Environment.ProcessorCount / 2, 1, 4),
            EnableCpuMemArena = false,
            EnableMemoryPattern = false,
        };
        _encoder = new InferenceSession(Path.Combine(directory, TranslationModels.EncoderFile), options);
        _decoder = new InferenceSession(Path.Combine(directory, TranslationModels.DecoderFile), options);
        using (var spm = File.OpenRead(Path.Combine(directory, "source.spm")))
            _sourceSpm = SentencePieceTokenizer.Create(spm, false, false);

        _vocab = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(Path.Combine(directory, "vocab.json")))
                 ?? throw new InvalidDataException("vocab.json vacío");
        _idToToken = new string[_vocab.Values.Max() + 1];
        foreach (var (token, id) in _vocab) _idToToken[id] = token;

        using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "config.json")));
        var c = config.RootElement;
        _eos = c.GetProperty("eos_token_id").GetInt32();
        _pad = c.GetProperty("pad_token_id").GetInt32(); // también es el token de inicio del decodificador
        _unk = _vocab.GetValueOrDefault("<unk>", 1);
        _layers = c.GetProperty("decoder_layers").GetInt32();
        _heads = c.GetProperty("decoder_attention_heads").GetInt32();
        _headDim = c.GetProperty("d_model").GetInt32() / _heads;
    }

    /// <summary>Traduce una oración (o un fragmento corto).</summary>
    public string Translate(string sentence)
    {
        var ids = _sourceSpm.EncodeToTokens(sentence, out _, false, false)
            .Select(t => (long)_vocab.GetValueOrDefault(t.Value, _unk))
            .Take(500)
            .Append(_eos)
            .ToArray();
        var n = ids.Length;
        var mask = new DenseTensor<long>(Enumerable.Repeat(1L, n).ToArray(), [1, n]);

        using var encoded = _encoder.Run(
        [
            NamedOnnxValue.CreateFromTensor("input_ids", new DenseTensor<long>(ids, [1, n])),
            NamedOnnxValue.CreateFromTensor("attention_mask", mask),
        ]);
        var hidden = encoded.First().AsTensor<float>().ToDenseTensor();

        var past = new Dictionary<string, DenseTensor<float>>();
        for (var layer = 0; layer < _layers; layer++)
            foreach (var part in new[] { "decoder", "encoder" })
                foreach (var kv in new[] { "key", "value" })
                    past[$"past_key_values.{layer}.{part}.{kv}"] = new DenseTensor<float>([1, _heads, 0, _headDim]);

        var output = new List<int>();
        long next = _pad;
        var maxSteps = Math.Min(512, n * 3 + 10);
        for (var step = 0; step < maxSteps; step++)
        {
            var inputs = new List<NamedOnnxValue>(4 + past.Count)
            {
                NamedOnnxValue.CreateFromTensor("encoder_attention_mask", mask),
                NamedOnnxValue.CreateFromTensor("input_ids", new DenseTensor<long>(new[] { next }, [1, 1])),
                NamedOnnxValue.CreateFromTensor("encoder_hidden_states", hidden),
                NamedOnnxValue.CreateFromTensor("use_cache_branch", new DenseTensor<bool>(new[] { step > 0 }, [1])),
            };
            inputs.AddRange(past.Select(p => NamedOnnxValue.CreateFromTensor(p.Key, p.Value)));
            using var result = _decoder.Run(inputs);

            var logits = result.First(r => r.Name == "logits").AsTensor<float>();
            var best = -1;
            var bestScore = float.NegativeInfinity;
            for (var v = 0; v < logits.Dimensions[2]; v++)
            {
                if (v == _pad) continue; // el modelo nunca debe generar el token de relleno
                var score = logits[0, 0, v];
                if (score > bestScore) { bestScore = score; best = v; }
            }
            if (best == _eos || best < 0) break;
            output.Add(best);
            next = best;

            foreach (var r in result)
            {
                if (!r.Name.StartsWith("present.", StringComparison.Ordinal)) continue;
                var key = "past_key_values." + r.Name["present.".Length..];
                if (step > 0 && key.Contains(".encoder.", StringComparison.Ordinal)) continue; // no cambia tras el primer paso
                past[key] = r.AsTensor<float>().ToDenseTensor();
            }
        }

        var text = new StringBuilder();
        foreach (var id in output)
        {
            var token = id < _idToToken.Length ? _idToToken[id] : null;
            if (token is null or "<unk>" or "<pad>" or "</s>") continue;
            text.Append(token);
        }
        return text.ToString().Replace('▁', ' ').Trim();
    }

    public void Dispose()
    {
        _encoder.Dispose();
        _decoder.Dispose();
    }
}
