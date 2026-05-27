// File: Services/Data/ICompendiumExtractor.cs
using System.Text.Json.Nodes;

namespace CompendiumEditor.Services.Data;

public interface ICompendiumExtractor
{
    JsonNode ExtractObjectPayload(string rawFileContent);
    JsonArray ExtractArrayPayload(string rawFileContent);
}