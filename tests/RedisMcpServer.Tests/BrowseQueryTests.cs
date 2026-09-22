using RedisMcpServer.Core.Browse;
using RedisMcpServer.Core.Tools;
using Xunit;

namespace RedisMcpServer.Tests;

public class BrowseQueryTests
{
    [Fact]
    public void Defaults_AreUiFriendly()
    {
        var query = new KeyQuery();

        Assert.Equal(KeyQuery.DefaultPageSize, query.PageSize);
        Assert.Equal(50, query.PageSize);
        Assert.Equal(1, query.PageNumber);
        Assert.True(query.IsDescendingOrder); // default: newest first
        Assert.Null(query.Db);
        Assert.Null(query.Type);
        Assert.Null(query.KeyPattern);
        Assert.Null(query.ValuePattern);
    }

    [Fact]
    public void ValidQuery_DoesNotThrow()
    {
        var query = new KeyQuery
        {
            Db = 3,
            Type = "hash",
            KeyPattern = "user*",
            ValuePattern = "VIP",
            Order = "ASC",
            PageSize = 100,
            PageNumber = 2,
        };

        query.Validate();
        Assert.False(query.IsDescendingOrder);
    }

    [Theory]
    [InlineData("sideways")]
    [InlineData("descending")]
    [InlineData("")]
    public void Order_MustBeAscOrDesc(string order)
    {
        var query = new KeyQuery { Order = order };
        var ex = Assert.Throws<ToolExecutionException>(() => query.Validate());
        Assert.Contains("order", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(KeyQuery.MaxPageSize + 1)]
    public void PageSize_MustBeInRange(int pageSize)
    {
        var query = new KeyQuery { PageSize = pageSize };
        var ex = Assert.Throws<ToolExecutionException>(() => query.Validate());
        Assert.Contains("pageSize", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void PageNumber_MustBePositive(int pageNumber)
    {
        var query = new KeyQuery { PageNumber = pageNumber };
        var ex = Assert.Throws<ToolExecutionException>(() => query.Validate());
        Assert.Contains("pageNumber", ex.Message);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Db_MustNotBeNegative(int db)
    {
        var query = new KeyQuery { Db = db };
        var ex = Assert.Throws<ToolExecutionException>(() => query.Validate());
        Assert.Contains("db", ex.Message);
    }

    [Fact]
    public void MaxPageSize_IsAccepted()
    {
        var query = new KeyQuery { PageSize = KeyQuery.MaxPageSize };
        query.Validate(); // no throw
    }
}
