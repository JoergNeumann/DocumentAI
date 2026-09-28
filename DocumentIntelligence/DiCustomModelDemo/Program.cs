using Azure;
using Azure.AI.DocumentIntelligence;
using System.Reflection;

static class Program
{
    static async Task Main(string[] args)
    {
        var documentIntelligenceServiceEndpoint = Environment.GetEnvironmentVariable("DOCUMENT_INTELLIGENCE_SERVICE_ENDPOINT") ?? "";
        var documentIntelligenceServiceApiKey = Environment.GetEnvironmentVariable("DOCUMENT_INTELLIGENCE_SERVICE_API_KEY") ?? "";
        var documentIntelligenceServiceModel = "Fahrzeugliste";
        var fileName = "Test.pdf";

        // DocumentIntelligenceClient erstellen
        var client = new DocumentIntelligenceClient(
            new Uri(documentIntelligenceServiceEndpoint),
            new AzureKeyCredential(documentIntelligenceServiceApiKey));

        // Datei als Stream lesen
        using var stream = ReadStream(fileName);

        // Optionen für die Dokumentanalyse festlegen
        var options = new AnalyzeDocumentOptions(
            documentIntelligenceServiceModel,
            BinaryData.FromStream(stream));

        // Dokument analysieren
        var operation = await client.AnalyzeDocumentAsync(
            WaitUntil.Completed,
            options);
        AnalyzeResult result = operation.Value;

        // Benutzerdefinierte Felder auslesen
        foreach (var item in result.Documents)
        {
            Console.WriteLine($"Fahrzeugliste: {item.Fields["Fahrzeugliste"].ValueString}");
            Console.WriteLine($"IsNeuwagen: {item.Fields["IsNeuwagen"].ValueSelectionMark}");
            Console.WriteLine($"IsGebrauchtwagen: {item.Fields["IsGebrauchtwagen"].ValueSelectionMark}");
        }
    }
    
    static Stream ReadStream(string fileName)
    {
        var info = Assembly.GetExecutingAssembly().GetName();
        var name = info.Name;
        return Assembly
            .GetExecutingAssembly()
            .GetManifestResourceStream($"{name}.Resources." + fileName)!;
    }
}