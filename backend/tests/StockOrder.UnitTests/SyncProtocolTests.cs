using System.Text.Json;
using Shouldly;
using StockOrder.Modules.Sync.Application;
using StockOrder.Modules.Sync.Contracts;
using Xunit;

namespace StockOrder.UnitTests;

/// <summary>Section 12 pull-token rules and push dependency detection.</summary>
public sealed class SyncProtocolTests
{
    [Fact]
    public void Full_page_continues_from_the_last_row()
    {
        RowVersionToken.Next([0x10, 0x11, 0x12], limit: 3, upperRowVersion: 0x50).ShouldBe("0000000000000012");
    }

    [Fact]
    public void Short_page_jumps_to_just_below_the_upper_bound()
    {
        // Everything below MIN_ACTIVE_ROWVERSION() has been seen, even rows that didn't match.
        RowVersionToken.Next([0x10], limit: 1000, upperRowVersion: 0x50).ShouldBe("000000000000004F");
        RowVersionToken.Next([], limit: 1000, upperRowVersion: 0x50).ShouldBe("000000000000004F");
    }

    [Theory]
    [InlineData(null, 0UL)]
    [InlineData("", 0UL)]
    [InlineData("not-hex", 0UL)]
    [InlineData("000000000000004F", 0x4FUL)]
    public void Missing_or_unreadable_token_restarts_from_zero(string? token, ulong expected)
    {
        RowVersionToken.Parse(token).ShouldBe(expected);
    }

    [Fact]
    public void Binary_form_is_big_endian_like_sql_server_rowversion()
    {
        RowVersionToken.ToBinary(0x0102).ShouldBe(new byte[] { 0, 0, 0, 0, 0, 0, 1, 2 });
        RowVersionToken.FromBinary([0, 0, 0, 0, 0, 0, 1, 2]).ShouldBe(0x0102UL);
    }

    [Fact]
    public void Payload_guid_references_are_found_at_any_depth()
    {
        var order = Guid.NewGuid();
        var customer = Guid.NewGuid();
        var payload = JsonDocument.Parse($$"""
            { "orderId": "{{order}}", "lines": [ { "qty": 2, "note": "x" } ], "customer": { "id": "{{customer}}" } }
            """).RootElement;

        PayloadGuids.Find(payload).ShouldBe([order, customer], ignoreOrder: true);
    }
}
