using KotobaSenpai.Core.Llm;

namespace KotobaSenpai.Core.Tests.Llm;

/// <summary>
/// The wire form of <see cref="AgentResultCode"/> is the agent CLI contract, so the enum and the wire
/// values must stay a bijection: an unmapped member would serialize as a rename, and a stale parser would
/// silently map an unknown provider outcome to a valid code.
/// </summary>
public sealed class AgentResultCodeTests
{
    [Fact]
    public void Every_member_round_trips_through_its_wire_value()
    {
        var members = Enum.GetValues<AgentResultCode>();

        foreach (var member in members)
        {
            var wire = member.ToWire();
            Assert.False(string.IsNullOrWhiteSpace(wire));
            Assert.Equal(member, AgentResultCodes.TryParseWire(wire));
        }
    }

    [Fact]
    public void Wire_values_are_unique_and_lower_snake_case()
    {
        var wires = Enum.GetValues<AgentResultCode>().Select(code => code.ToWire()).ToArray();

        Assert.Equal(wires.Length, wires.Distinct(StringComparer.Ordinal).Count());
        Assert.All(wires, wire => Assert.Matches("^[a-z][a-z0-9]*(_[a-z0-9]+)*$", wire));
    }

    [Fact]
    public void Unknown_wire_values_do_not_map_to_a_valid_code()
    {
        Assert.Null(AgentResultCodes.TryParseWire(null));
        Assert.Null(AgentResultCodes.TryParseWire(""));
        Assert.Null(AgentResultCodes.TryParseWire("not_a_code"));
        Assert.Null(AgentResultCodes.TryParseWire("OK"));
    }
}
