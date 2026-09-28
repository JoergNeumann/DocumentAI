using Azure;
using Azure.AI.ContentUnderstanding;
using System.Reflection;

internal class Program
{
    private static async Task<int> Main()
    {
        // Read configuration from environment variables
        var endpoint = Environment.GetEnvironmentVariable("AZURE_AI_ENDPOINT") ?? throw new InvalidOperationException("Missing AZURE_AI_ENDPOINT");
        var apiKey = Environment.GetEnvironmentVariable("AZURE_AI_KEY");
        var apiVersion = Environment.GetEnvironmentVariable("AZURE_AI_API_VERSION") ?? "2025-11-01";

        // read PDF file into byte array
        var exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? throw new InvalidOperationException();
        var pdfPath = Path.Combine(exeDir, "Documents\\2026-02-05 Telekom.pdf");
        if (!File.Exists(pdfPath))
            throw new FileNotFoundException($"PDF nicht gefunden: {pdfPath}");
        byte[] pdfBytes = await File.ReadAllBytesAsync(pdfPath);

        // model
        var analyzerId = "prebuilt-invoice";

        // create client (API-Key oder DefaultAzureCredential)
        var client = new ContentUnderstandingClient(
            new Uri(endpoint), 
            new AzureKeyCredential(apiKey ?? ""));
        Console.WriteLine($"Endpoint: {endpoint}");
        Console.WriteLine($"API Version: {apiVersion}");
        Console.WriteLine($"Datei: {Path.GetFileName(pdfPath)}");
        Console.WriteLine();

        // start analysis (long-running operation)
        var operation = await client.AnalyzeBinaryAsync(
            WaitUntil.Completed,
            analyzerId: analyzerId,
            binaryInput: BinaryData.FromBytes(pdfBytes),
            contentType: "application/pdf");

        // get the result
        var result = operation.Value;

        // output all fields
        foreach (var field in result.Contents!.First().Fields)
        {
            //Console.WriteLine($"Feld: {field.Key}, Wert: {field.Value.Value}, Confidence: {field.Value.Confidence}");
        }
        Console.WriteLine( "------------------------------------------------------------");

        // Get the document content (invoices are documents)
        DocumentContent documentContent = (DocumentContent)result.Contents!.First();

        // Print document unit information
        // The unit indicates the measurement system used for coordinates in the source field
        Console.WriteLine($"Document unit: {documentContent.Unit ?? "unknown"}");
        Console.WriteLine($"Pages: {documentContent.StartPageNumber} to {documentContent.EndPageNumber}");
        if (documentContent.Pages != null && documentContent.Pages.Count > 0)
        {
            var page = documentContent.Pages[0];
            var unit = documentContent.Unit?.ToString() ?? "units";
            Console.WriteLine($"Page dimensions: {page.Width} x {page.Height} {unit}");
        }
        Console.WriteLine();

        // Extract simple string fields
        var customerNameField = documentContent.Fields["CustomerName"];
        Console.WriteLine($"Customer Name: {customerNameField.Value ?? "(None)"}");
        Console.WriteLine($"  Confidence: {customerNameField.Confidence?.ToString("F2") ?? "N/A"}");
        Console.WriteLine($"  Source: {customerNameField.Sources?.FirstOrDefault()?.RawValue ?? "N/A"}");
        if (customerNameField.Spans?.Count > 0)
        {
            var span = customerNameField.Spans[0];
            Console.WriteLine($"  Position in markdown: offset={span.Offset}, length={span.Length}");
        }

        // Extract simple date field
        var invoiceDateField = documentContent.Fields.GetFieldOrDefault("InvoiceDate");
        Console.WriteLine($"Invoice Date: {invoiceDateField?.Value ?? "(None)"}");
        Console.WriteLine($"  Confidence: {invoiceDateField?.Confidence?.ToString("F2") ?? "N/A"}");
        Console.WriteLine($"  Source: {invoiceDateField?.Sources?.FirstOrDefault()?.RawValue ?? "N/A"}");
        if (invoiceDateField?.Spans?.Count > 0)
        {
            var span = invoiceDateField.Spans[0];
            Console.WriteLine($"  Position in markdown: offset={span.Offset}, length={span.Length}");
        }

        // Extract object fields (nested structures)
        var totalAmountField = documentContent.Fields.GetFieldOrDefault("TotalAmount");
        if (totalAmountField != null && totalAmountField.Value is IDictionary<string, ContentField> totalAmountDict)
        {
            totalAmountDict.TryGetValue("Amount", out var amountField);
            totalAmountDict.TryGetValue("CurrencyCode", out var currencyField);

            var amount = amountField?.Value as double?;
            var currency = currencyField?.Value?.ToString();

            Console.WriteLine($"Total: {currency ?? "$"}{amount?.ToString("F2") ?? "(None)"}");
            Console.WriteLine($"  Confidence: {totalAmountField.Confidence?.ToString("F2") ?? "N/A"}");
            Console.WriteLine($"  Source: {totalAmountField.Sources?.FirstOrDefault()?.RawValue ?? "N/A"}");
        }

        // Extract array fields (collections like line items)
        var lineItemsField = documentContent.Fields.GetFieldOrDefault("LineItems");
        if (lineItemsField != null && lineItemsField.Value is IList<ContentField> itemsList)
        {
            Console.WriteLine($"Line Items ({itemsList.Count}):");
            for (int i = 0; i < itemsList.Count; i++)
            {
                var itemField = itemsList[i];
                if (itemField.Value is IDictionary<string, ContentField> itemDict)
                {
                    itemDict.TryGetValue("Description", out var descField);
                    itemDict.TryGetValue("Quantity", out var qtyField);

                    var description = descField?.Value;
                    var quantity = qtyField?.Value as double?;

                    Console.WriteLine($"  Item {i + 1}: {description ?? "N/A"} (Qty: {quantity?.ToString() ?? "N/A"})");
                    Console.WriteLine($"    Confidence: {itemField.Confidence?.ToString("F2") ?? "N/A"}");
                }
            }
        }
        return 0;
    }
}