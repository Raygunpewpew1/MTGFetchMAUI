using Xunit;
using MTGFetchMAUI.Data;
using MTGFetchMAUI.Core;

namespace MTGFetchMAUI.Tests;

public class MTGSearchHelperTests
{
    [Fact]
    public void WhereNameContains_AddsCorrectConditionAndParameter()
    {
        // Arrange
        var helper = new MTGSearchHelper();
        string cardName = "Black Lotus";

        // Act
        helper.SearchCards()
              .WhereNameContains(cardName);
        var (sql, parameters) = helper.Build();

        // Assert
        // SQL should contain the condition fragment
        Assert.Contains(SQLQueries.CondName, sql);

        // Find the parameter that starts with 'pName'
        var param = parameters.FirstOrDefault(p => p.name.StartsWith("pName"));

        Assert.NotNull(param.name);
        Assert.Equal("%" + cardName + "%", param.value);
        Assert.Contains("@" + param.name, sql);
    }

    [Fact]
    public void WhereNameContains_MultipleCalls_IncrementsParamCounter()
    {
        // Arrange
        var helper = new MTGSearchHelper();

        // Act
        helper.SearchCards()
              .WhereNameContains("Black")
              .WhereNameContains("Lotus");
        var (sql, parameters) = helper.Build();

        // Assert
        Assert.Equal(2, parameters.Count);

        var p1 = parameters.FirstOrDefault(p => p.value.ToString() == "%Black%");
        var p2 = parameters.FirstOrDefault(p => p.value.ToString() == "%Lotus%");

        Assert.NotNull(p1.name);
        Assert.NotNull(p2.name);
        Assert.NotEqual(p1.name, p2.name);
        Assert.Contains("@" + p1.name, sql);
        Assert.Contains("@" + p2.name, sql);
    }

    [Fact]
    public void WhereNameContains_EmptyString_StillAddsCondition()
    {
        // Arrange
        var helper = new MTGSearchHelper();

        // Act
        helper.SearchCards().WhereNameContains("");
        var (sql, parameters) = helper.Build();

        // Assert
        Assert.Contains(SQLQueries.CondName, sql);
        var param = parameters.FirstOrDefault(p => p.name.StartsWith("pName"));
        Assert.Equal("%%", param.value);
    }
}
