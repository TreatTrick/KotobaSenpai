using System.Text.Json;
using System.Text.Json.Nodes;

namespace KotobaSenpai.Platform.Windows.Llm;

/// <summary>
/// The group-array JSON schema shared by the three protocols (a root object wrapping a <c>groups</c> array), consistent
/// with the field validation in <see cref="PhraseResponseParser.ParseGroups"/>. Each protocol embeds this schema in its
/// own structured-output declaration.
/// </summary>
public static class PhraseGroupSchema
{
    /// <summary>Root object: { "groups": [ group, ... ] }.</summary>
    public static JsonObject Root { get; } = new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["groups"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["modelGroupId"] = new JsonObject { ["type"] = "string" },
                        ["type"] = new JsonObject { ["type"] = "string" },
                        ["parts"] = new JsonObject
                        {
                            ["type"] = "array",
                            ["items"] = new JsonObject
                            {
                                ["type"] = "array",
                                ["items"] = new JsonObject { ["type"] = "string" },
                            },
                        },
                        ["label"] = new JsonObject { ["type"] = "string" },
                        ["meaning"] = new JsonObject { ["type"] = "string" },
                        ["grammar"] = new JsonObject { ["type"] = "string" },
                    },
                    ["required"] = new JsonArray("modelGroupId", "type", "parts", "label", "meaning", "grammar"),
                    ["additionalProperties"] = false,
                },
            },
            ["words"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["headword"] = new JsonObject { ["type"] = "string" },
                        ["pos"] = new JsonObject { ["type"] = "string" },
                        ["meaning"] = new JsonObject { ["type"] = "string" },
                        ["grammar"] = new JsonObject { ["type"] = "string" },
                    },
                    ["required"] = new JsonArray("headword", "pos", "meaning", "grammar"),
                    ["additionalProperties"] = false,
                },
            },
        },
        ["required"] = new JsonArray("groups", "words"),
        ["additionalProperties"] = false,
    };
}