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

        // name of the custom analyzer
        var analyzerId = "my_custom_analyzer";
        
        // name of the models
        var completionModelName = "gpt-4.1";
        var embeddingModelName = "text-embedding-3-large";

        // create client (API-Key oder DefaultAzureCredential)
        var client = new ContentUnderstandingClient(
            new Uri(endpoint),
            new AzureKeyCredential(apiKey ?? ""));

        // create custom schema and analyzer (with custom fields and model mappings)
        ContentAnalyzer analyzer = await CreateAnalyzerAsync(
            analyzerId,
            client,
            completionModelName,
            embeddingModelName);

        try
        {
            // read PDF file into byte array
            var exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? throw new InvalidOperationException();
            var pdfPath = Path.Combine(exeDir, "Documents\\2026-02-05 Telekom.pdf");
            if (!File.Exists(pdfPath))
                throw new FileNotFoundException($"PDF nicht gefunden: {pdfPath}");
            byte[] pdfBytes = await File.ReadAllBytesAsync(pdfPath);

            // Analyze a document using the custom analyzer
            var operation = await client.AnalyzeBinaryAsync(
                WaitUntil.Completed,
                analyzerId: analyzerId,
                binaryInput: BinaryData.FromBytes(pdfBytes),
                contentType: "application/pdf");

            var analyzeResult = operation.Value;

            // Extract custom fields from the result
            // Since EstimateFieldSourceAndConfidence is enabled, we can access confidence scores and source information
            if (analyzeResult.Contents?.FirstOrDefault() is DocumentContent content)
            {
                // company_name (extract)
                if (content.Fields.TryGetValue("company_name", out var companyNameField))
                {
                    var companyName = companyNameField?.Value as string;
                    Console.WriteLine($"Company Name (extract): {companyName ?? "(not found)"}");
                    Console.WriteLine($"  Confidence: {companyNameField?.Confidence?.ToString("F2") ?? "N/A"}");
                    Console.WriteLine($"  Source: {companyNameField?.Sources?.FirstOrDefault()?.RawValue ?? "N/A"}");
                    if (companyNameField?.Spans != null && companyNameField.Spans.Count > 0)
                    {
                        var span = companyNameField.Spans[0];
                        Console.WriteLine($"  Position in markdown: offset={span.Offset}, length={span.Length}");
                    }
                }

                // total_amount (extract)
                if (content.Fields.TryGetValue("total_amount", out var totalAmountField))
                {
                    double? totalAmount = null;
                    if (totalAmountField?.Value != null)
                    {
                        if (totalAmountField.Value is double d) totalAmount = d;
                        else if (double.TryParse(totalAmountField.Value.ToString(), out var parsed)) totalAmount = parsed;
                    }
                    Console.WriteLine($"Total Amount (extract): {totalAmount?.ToString("F2") ?? "(not found)"}");
                    Console.WriteLine($"  Confidence: {totalAmountField?.Confidence?.ToString("F2") ?? "N/A"}");
                    Console.WriteLine($"  Source: {totalAmountField?.Sources?.FirstOrDefault()?.RawValue ?? "N/A"}");
                    if (totalAmountField?.Spans != null && totalAmountField.Spans.Count > 0)
                    {
                        var span = totalAmountField.Spans[0];
                        Console.WriteLine($"  Position in markdown: offset={span.Offset}, length={span.Length}");
                    }
                }

                // document_summary (generate)
                if (content.Fields.TryGetValue("document_summary", out var summaryField))
                {
                    var summary = summaryField?.Value as string;
                    Console.WriteLine($"Document Summary (generate): {summary ?? "(not found)"}");
                    Console.WriteLine($"  Confidence: {summaryField?.Confidence?.ToString("F2") ?? "N/A"}");
                    if (!string.IsNullOrEmpty(summaryField?.Sources?.FirstOrDefault()?.RawValue))
                    {
                        Console.WriteLine($"  Source: {summaryField.Sources.First().RawValue}");
                    }
                }

                // document_type (classify)
                if (content.Fields.TryGetValue("document_type", out var documentTypeField))
                {
                    var documentType = documentTypeField?.Value as string;
                    Console.WriteLine($"Document Type (classify): {documentType ?? "(not found)"}");
                    Console.WriteLine($"  Confidence: {documentTypeField?.Confidence?.ToString("F2") ?? "N/A"}");
                    if (!string.IsNullOrEmpty(documentTypeField?.Sources?.FirstOrDefault()?.RawValue))
                    {
                        Console.WriteLine($"  Source: {documentTypeField.Sources.First().RawValue}");
                    }
                }
            }
        }
        catch (Exception ex) 
        {
            Console.WriteLine(ex.Message);
            throw;
        }
        finally
        {
            // delete the analyzer
            client.DeleteAnalyzerAsync(analyzerId).Wait();
        }

        return 0;
    }

    static async Task<ContentAnalyzer> CreateAnalyzerAsync(
        string analyzerId, 
        ContentUnderstandingClient client,
        string completionModelName = "gpt-4.1",
        string embeddingModelName = "text-embedding-3-large")
    {
        // Define field schema with custom fields
        // This example demonstrates three extraction methods:
        // - extract: Literal text extraction (requires estimateSourceAndConfidence)
        // - generate: AI-generated values based on content interpretation
        // - classify: Classification against predefined categories
        var fieldSchema = new ContentFieldSchema(
            new Dictionary<string, ContentFieldDefinition>
            {
                ["company_name"] = new ContentFieldDefinition
                {
                    Type = ContentFieldType.String,
                    Method = GenerationMethod.Extract,
                    Description = "Name of the company"
                },
                ["total_amount"] = new ContentFieldDefinition
                {
                    Type = ContentFieldType.Number,
                    Method = GenerationMethod.Extract,
                    Description = "Total amount on the document"
                },
                ["document_summary"] = new ContentFieldDefinition
                {
                    Type = ContentFieldType.String,
                    Method = GenerationMethod.Generate,
                    Description = "A brief summary of the document content"
                },
                ["document_type"] = new ContentFieldDefinition
                {
                    Type = ContentFieldType.String,
                    Method = GenerationMethod.Classify,
                    Description = "Type of document"
                }
            })
        {
            Name = "company_schema",
            Description = "Schema for extracting company information"
        };

        // Add enum values for the classify field
        fieldSchema.Fields["document_type"].Enum.Add("invoice");
        fieldSchema.Fields["document_type"].Enum.Add("receipt");
        fieldSchema.Fields["document_type"].Enum.Add("contract");
        fieldSchema.Fields["document_type"].Enum.Add("report");
        fieldSchema.Fields["document_type"].Enum.Add("other");

        // Create analyzer configuration
        var config = new ContentAnalyzerConfig
        {
            EnableFormula = true,
            EnableLayout = true,
            EnableOcr = true,
            EstimateFieldSourceAndConfidence = true,
            ShouldReturnDetails = true
        };

        // Create the custom analyzer
        var customAnalyzer = new ContentAnalyzer
        {
            BaseAnalyzerId = "prebuilt-document",
            Description = "Custom analyzer for extracting company information",
            Config = config,
            FieldSchema = fieldSchema
        };

        // Add model mappings for supported large language models (required for custom analyzers)
        // Maps model roles (completion, embedding) to specific model names
        customAnalyzer.Models["completion"] = completionModelName;
        customAnalyzer.Models["embedding"] = embeddingModelName;

        // Create the analyzer
        var operation = await client.CreateAnalyzerAsync(
            WaitUntil.Completed,
            analyzerId,
            customAnalyzer);

        ContentAnalyzer result = operation.Value;
        Console.WriteLine($"Analyzer '{analyzerId}' created successfully!");

        return result;
    }
}