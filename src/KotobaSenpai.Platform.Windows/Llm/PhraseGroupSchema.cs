using System.Text.Json;
using System.Text.Json.Nodes;

namespace KotobaSenpai.Platform.Windows.Llm;

/// <summary>
/// 三个协议共用的 group 数组 JSON schema（根对象包一个 <c>groups</c> 数组），与
/// <see cref="PhraseResponseParser.ParseGroups"/> 的字段校验一致。各协议在自己的结构化输出声明里内嵌此 schema。
/// </summary>
public static class PhraseGroupSchema
{
    /// <summary>根对象：{ "groups": [ group, ... ] }。</summary>
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
        },
        ["required"] = new JsonArray("groups"),
        ["additionalProperties"] = false,
    };
}