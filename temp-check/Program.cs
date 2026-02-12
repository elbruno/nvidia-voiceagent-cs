using HuggingfaceHub;
using System.Reflection;

// Check HFDownloader methods
var methods = typeof(HFDownloader).GetMethods(BindingFlags.Public | BindingFlags.Static);
Console.WriteLine("=== HFDownloader Methods ===");
foreach (var method in methods.Where(m => m.Name.Contains("Download")))
{
    Console.WriteLine($"\n{method.Name}:");
    foreach (var param in method.GetParameters())
    {
        Console.WriteLine($"  - {param.Name}: {param.ParameterType.FullName} (Optional: {param.HasDefaultValue})");
    }
}

// Check IGroupedProgress
Console.WriteLine("\n=== IGroupedProgress Interface ===");
var groupedProgressType = typeof(IGroupedProgress);
foreach (var method in groupedProgressType.GetMethods())
{
    Console.WriteLine($"Method: {method.Name}");
    foreach (var param in method.GetParameters())
    {
        Console.WriteLine($"  - {param.Name}: {param.ParameterType.FullName}");
    }
}
