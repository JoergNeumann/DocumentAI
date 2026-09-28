using Azure;
using Azure.AI.ContentUnderstanding;
using System.Reflection;
using System.Text.Json;

namespace ContentUnderstandingSample
{
    class AnalysisSample
    {
        static async Task Main(string[] args)
        {
            string endpoint = Environment.GetEnvironmentVariable("AZURE_AI_ENDPOINT") ?? throw new InvalidOperationException("Missing AZURE_AI_ENDPOINT");
            string key = Environment.GetEnvironmentVariable("AZURE_AI_KEY") ?? throw new InvalidOperationException("Missing API Key.");
            string apiVersion = Environment.GetEnvironmentVariable("AZURE_AI_API_VERSION") ?? "2025-11-01";
            const string analyzerId = "Beleganalyzer";

            // read PDF file into byte array
            var exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? throw new InvalidOperationException();
            var pdfPath = Path.Combine(exeDir, "Documents\\2026-01-28 Wittmann-Beleg.jpg");
            if (!File.Exists(pdfPath))
                throw new FileNotFoundException($"PDF nicht gefunden: {pdfPath}");
            byte[] pdfBytes = await File.ReadAllBytesAsync(pdfPath);


            var client = new ContentUnderstandingClient(
                new Uri(endpoint), new AzureKeyCredential(key));

            Console.WriteLine($"Analyzing with {analyzerId} analyzer...");
            Console.WriteLine($"  File URL: {endpoint}\n");

            // Analyze
            Operation<AnalysisResult> operation;
            try
            {
                operation = await client.AnalyzeBinaryAsync(
                    WaitUntil.Completed,
                    analyzerId: analyzerId,
                    binaryInput: BinaryData.FromBytes(pdfBytes),
                    contentType: "application/pdf");
            }
            catch (RequestFailedException ex)
            {
                Console.Error.WriteLine($"\n[Azure Error] Status: {ex.Status}");
                Console.Error.WriteLine($"Message: {ex.Message}");
                return;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"\n[Unexpected Error]: {ex.Message}");
                return;
            }

            Console.WriteLine(new string('=', 50));
            Console.WriteLine("Analysis result:");
            Console.WriteLine(new string('=', 50) + "\n");

            // Display the result as JSON
            var result = operation.Value;
            var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
            string resultStr = JsonSerializer.Serialize(result, jsonOptions);

            string[] retLines = resultStr.Split(Environment.NewLine);

            if (retLines.Length > 0)
            {
                foreach (var line in retLines)
                {
                    Console.WriteLine(line);
                }
            }
            else
            {
                Console.WriteLine(resultStr);
            }

            // Display the extracted fields
            Console.WriteLine("\n------------------------------------------\n");
            
            DocumentContent documentContent = (DocumentContent)result.Contents!.First();
            
            var betragField = documentContent.Fields["Zahlbetrag"];
            Console.WriteLine($"Zahlbetrag: {betragField.Value ?? "(None)"}");

            var nettoField = documentContent.Fields["Nettosumme"];
            Console.WriteLine($"Netto: {nettoField.Value ?? "(None)"}");

            var trinkgeldField = documentContent.Fields["Trinkgeld"];
            Console.WriteLine($"Trinkgeld: {trinkgeldField.Value ?? "(None)"}");
        }
    }
}
