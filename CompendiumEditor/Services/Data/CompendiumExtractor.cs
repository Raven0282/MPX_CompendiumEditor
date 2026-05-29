// File: Services/Data/CompendiumExtractor.cs
using CompendiumEditor.Services.Logging;
using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CompendiumEditor.Services.Data
{
    public class CompendiumExtractor : ICompendiumExtractor
    {
        private readonly IDiagnosticLogger _logger;

        public CompendiumExtractor(IDiagnosticLogger logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// NORMALIZATION: Legacy compendium files often use Javascript object literal syntax 
        /// where keys are not quoted (e.g., { feat123: "..." }). This method uses Regex 
        /// to wrap unquoted identifiers in double quotes to ensure strict JSON compliance.
        /// </summary>
        private string HealJson(string json)
        {
            // 1. Handle keys at the start of lines or after opening braces.
            // We ensure we only match keys that are NOT inside double quotes.
            // Strict pattern: Start of line, optional whitespace, alphanumeric key, colon.
            string pattern = @"(?<prefix>\{|,|^\s*)(?<key>[a-zA-Z0-9_]+)\s*:";
            string healed = Regex.Replace(json, pattern, "${prefix}\"${key}\":", RegexOptions.Multiline);
            
            // 2. Special case for the very first key if it's on the same line as the brace.
            if (healed.StartsWith("{") && !healed.StartsWith("{\"") && !healed.StartsWith("{\r") && !healed.StartsWith("{\n"))
            {
                healed = Regex.Replace(healed, @"^\{\s*(?<key>[a-zA-Z0-9_]+)\s*:", "{\r\n  \"${key}\":");
            }

            if (healed.Length != json.Length)
            {
                _logger.Log($"JSON normalization applied. Length changed from {json.Length} to {healed.Length}", "EXTRACTOR:HEALER");
            }
            return healed;
        }

        public JsonNode ExtractObjectPayload(string rawFileContent)
        {
            if (string.IsNullOrWhiteSpace(rawFileContent))
                throw new ArgumentException("Content canvas cannot be blank.", nameof(rawFileContent));

            _logger.Log($"Starting extraction. Raw length: {rawFileContent.Length}", "EXTRACTOR:OBJECT");

            ReadOnlySpan<char> sourceSpan = rawFileContent.AsSpan();
            int firstOpenBrace = sourceSpan.IndexOf('{');
            int lastCloseBrace = sourceSpan.LastIndexOf('}');

            _logger.Log($"Brackets found at index {firstOpenBrace} and {lastCloseBrace}", "EXTRACTOR:OBJECT");

            if (firstOpenBrace == -1 || lastCloseBrace == -1 || lastCloseBrace <= firstOpenBrace)
            {
                _logger.Log($"Balanced braces not found!", "EXTRACTOR:OBJECT ERROR");
                throw new FormatException("Failed to isolate a balanced structural JSON object schema sequence boundaries inside the file stream.");
            }

            string extractedJson = sourceSpan.Slice(firstOpenBrace, (lastCloseBrace - firstOpenBrace) + 1).ToString();
            
            // HEAL: Apply normalization for legacy unquoted property names
            extractedJson = HealJson(extractedJson);

            _logger.Log($"Extracted string length: {extractedJson.Length}", "EXTRACTOR:OBJECT");
            
            if (extractedJson.Length > 200)
            {
                _logger.Log($"Content Start: {extractedJson.Substring(0, 100).Replace("\r", "\\r").Replace("\n", "\\n")}", "EXTRACTOR:OBJECT");
                _logger.Log($"Content End: {extractedJson.Substring(extractedJson.Length - 100).Replace("\r", "\\r").Replace("\n", "\\n")}", "EXTRACTOR:OBJECT");
            }
            else
            {
                _logger.Log($"Full Content: {extractedJson}", "EXTRACTOR:OBJECT");
            }

            try
            {
                // PERF: Maintain the options as verified by the user to compile and work for standard JSON
                var options = new JsonSerializerOptions
                {
                    AllowTrailingCommas = true,
                    ReadCommentHandling = JsonCommentHandling.Skip
                };

                return JsonSerializer.Deserialize<JsonNode>(extractedJson, options) 
                    ?? throw new InvalidOperationException("Failed to construct object payload visual hierarchy node maps.");
            }
            catch (JsonException jex)
            {
                _logger.LogException(jex, "EXTRACTOR:OBJECT");
                throw;
            }
        }

        public JsonArray ExtractArrayPayload(string rawFileContent)
        {
            if (string.IsNullOrWhiteSpace(rawFileContent))
                throw new ArgumentException("Content canvas cannot be blank.", nameof(rawFileContent));

            _logger.Log($"Starting extraction. Raw length: {rawFileContent.Length}", "EXTRACTOR:ARRAY");

            ReadOnlySpan<char> sourceSpan = rawFileContent.AsSpan();

            // 1. Locate the absolute end of the JSONP function parameters
            int finalCloseParenthesis = sourceSpan.LastIndexOf(')');
            if (finalCloseParenthesis == -1)
            {
                _logger.Log("No closing parenthesis found.", "EXTRACTOR:ARRAY ERROR");
                throw new FormatException("Failed to safely balance nested listing array structural layout parameters.");
            }

            // 2. Scan backward from the end to find the matching outermost array close bracket ']'
            int matrixEndIndex = -1;
            for (int i = finalCloseParenthesis - 1; i >= 0; i--)
            {
                if (sourceSpan[i] == ']')
                {
                    matrixEndIndex = i;
                    break;
                }
            }

            if (matrixEndIndex == -1)
            {
                _logger.Log("No matrix end bracket ']' found.", "EXTRACTOR:ARRAY ERROR");
                throw new FormatException("Failed to safely balance nested listing array structural layout parameters.");
            }

            // 3. Track bracket balancing backwards to isolate the beginning of this exact matrix scope
            int bracketDepth = 0;
            int matrixStartIndex = -1;

            for (int i = matrixEndIndex; i >= 0; i--)
            {
                if (sourceSpan[i] == ']') bracketDepth++;
                if (sourceSpan[i] == '[') bracketDepth--;

                if (bracketDepth == 0)
                {
                    matrixStartIndex = i;
                    break;
                }
            }

            if (matrixStartIndex == -1)
            {
                _logger.Log("No matching start bracket '[' found.", "EXTRACTOR:ARRAY ERROR");
                throw new FormatException("Failed to safely balance nested listing array structural layout parameters.");
            }

            _logger.Log($"Matrix bounds identified: {matrixStartIndex} to {matrixEndIndex}", "EXTRACTOR:ARRAY");

            // 4. Slice out the fully validated clean JSON matrix layout block cleanly
            string extractedJson = sourceSpan.Slice(matrixStartIndex, (matrixEndIndex - matrixStartIndex) + 1).ToString();
            _logger.Log($"Extracted string length: {extractedJson.Length}", "EXTRACTOR:ARRAY");

            try
            {
                var options = new JsonSerializerOptions
                {
                    AllowTrailingCommas = true,
                    ReadCommentHandling = JsonCommentHandling.Skip
                };

                return JsonSerializer.Deserialize<JsonNode>(extractedJson, options) as JsonArray
                    ?? throw new InvalidOperationException("Array allocation processing failed to translate payload.");
            }
            catch (JsonException jex)
            {
                _logger.LogException(jex, "EXTRACTOR:ARRAY");
                throw;
            }
        }
    }
}