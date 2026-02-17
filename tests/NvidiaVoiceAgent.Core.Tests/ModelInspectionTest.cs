using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using NvidiaVoiceAgent.Core.Models;
using NvidiaVoiceAgent.Core.Services;

namespace NvidiaVoiceAgent.Core.Tests;

/// <summary>
/// Temporary test to inspect the actual model inputs.
/// </summary>
public class ModelInspectionTest
{
    [Fact]
    public void InspectParakeetModelInputs()
    {
        // Arrange
        var config = TestConfiguration.Instance;
        if (!config.AsrModelExists())
        {
            // Skip if model not available
            return;
        }

        var modelPath = config.ModelConfig.AsrModelPath;

        // Find the actual encoder.onnx file
        if (Directory.Exists(modelPath))
        {
            var encoderPath = Path.Combine(modelPath, "onnx", "encoder.onnx");
            if (!File.Exists(encoderPath))
            {
                var files = Directory.GetFiles(modelPath, "encoder.onnx", SearchOption.AllDirectories);
                if (files.Length > 0)
                {
                    encoderPath = files[0];
                }
            }
            modelPath = encoderPath;
        }

        if (!File.Exists(modelPath))
        {
            return;
        }

        // Load the model and inspect its inputs
        using var session = new InferenceSession(modelPath);

        Console.WriteLine("=== Parakeet-TDT Model Input Metadata ===");
        foreach (var input in session.InputMetadata)
        {
            var dims = input.Value.Dimensions.ToArray();
            Console.WriteLine($"Input: {input.Key}");
            Console.WriteLine($"  Type: {input.Value.ElementType.Name}");
            Console.WriteLine($"  Shape: [{string.Join(", ", dims)}]");
            Console.WriteLine($"  Is Symbolic: {dims.Any(d => d == -1)}");
            Console.WriteLine();
        }

        Console.WriteLine("=== Parakeet-TDT Model Output Metadata ===");
        foreach (var output in session.OutputMetadata)
        {
            var dims = output.Value.Dimensions.ToArray();
            Console.WriteLine($"Output: {output.Key}");
            Console.WriteLine($"  Type: {output.Value.ElementType.Name}");
            Console.WriteLine($"  Shape: [{string.Join(", ", dims)}]");
            Console.WriteLine();
        }
    }
}
