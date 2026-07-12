using System.Text;
using System.Text.Json;
using Microsoft.VisualBasic.FileIO;

namespace Jalium.UI.ApiParity;

internal sealed record CsvDataRow(int SourceRow, IReadOnlyDictionary<string, string> Values)
{
    public string Get(string name)
        => Values.TryGetValue(name, out string? value)
            ? value
            : throw new InvalidDataException($"CSV row {SourceRow} has no '{name}' column.");
}

internal static class LegacyCsvReader
{
    public static IReadOnlyList<CsvDataRow> Read(string path)
    {
        using var parser = new TextFieldParser(path, Encoding.UTF8)
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = false,
        };
        parser.SetDelimiters(",");

        string[] headers = parser.ReadFields()
            ?? throw new InvalidDataException($"CSV '{path}' contains no header row.");
        if (headers.Length > 0)
        {
            headers[0] = headers[0].TrimStart('\uFEFF');
        }

        var rows = new List<CsvDataRow>();
        int sourceRow = 1;
        while (!parser.EndOfData)
        {
            sourceRow++;
            string[]? fields = parser.ReadFields();
            if (fields is null || fields.All(string.IsNullOrEmpty))
            {
                continue;
            }

            if (fields.Length != headers.Length)
            {
                throw new InvalidDataException(
                    $"CSV '{path}' row {sourceRow} has {fields.Length} fields; expected {headers.Length}.");
            }

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < headers.Length; index++)
            {
                values.Add(headers[index], fields[index]);
            }

            rows.Add(new CsvDataRow(sourceRow, values));
        }

        return rows;
    }
}

internal sealed record LegacyValidationResult(
    string GapId,
    string ApiId,
    string Category,
    int Tier,
    string WpfAssembly,
    string WpfNamespace,
    string WpfType,
    string ExpectedJaliumType,
    string ExpectedSignature,
    string Status,
    string Actual,
    string SourceCsv,
    int SourceRow,
    string Diagnostic);

internal static class LegacyResultWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly string[] CsvHeaders =
    [
        "gap_id",
        "api_id",
        "category",
        "tier",
        "wpf_assembly",
        "wpf_namespace",
        "wpf_type",
        "expected_jalium_type",
        "expected_signature",
        "status",
        "actual",
        "source_csv",
        "source_row",
        "diagnostic",
    ];

    public static void Write(
        string outputDirectory,
        IReadOnlyList<LegacyValidationResult> results)
    {
        Directory.CreateDirectory(outputDirectory);
        string jsonlPath = Path.Combine(outputDirectory, "legacy-validation.jsonl");
        string csvPath = Path.Combine(outputDirectory, "legacy-validation.csv");

        using (var writer = new StreamWriter(jsonlPath, append: false, new UTF8Encoding(false)))
        {
            foreach (LegacyValidationResult result in results)
            {
                writer.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            }
        }

        using (var writer = new StreamWriter(csvPath, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)))
        {
            writer.WriteLine(string.Join(",", CsvHeaders));
            foreach (LegacyValidationResult result in results)
            {
                string[] fields =
                [
                    result.GapId,
                    result.ApiId,
                    result.Category,
                    result.Tier.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    result.WpfAssembly,
                    result.WpfNamespace,
                    result.WpfType,
                    result.ExpectedJaliumType,
                    result.ExpectedSignature,
                    result.Status,
                    result.Actual,
                    result.SourceCsv,
                    result.SourceRow.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    result.Diagnostic,
                ];
                writer.WriteLine(string.Join(",", fields.Select(Escape)));
            }
        }
    }

    private static string Escape(string value)
    {
        bool quote = value.Contains(',')
            || value.Contains('"')
            || value.Contains('\r')
            || value.Contains('\n');
        if (!quote)
        {
            return value;
        }

        return '"' + value.Replace("\"", "\"\"") + '"';
    }
}
