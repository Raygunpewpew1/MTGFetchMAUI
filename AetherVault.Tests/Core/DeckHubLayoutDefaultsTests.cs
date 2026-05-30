using AetherVault.Core;

namespace AetherVault.Tests.Core;

public class DeckHubLayoutDefaultsTests
{
    [Fact]
    public void GetDefaultForDevice_PhoneUsesList()
    {
        Assert.Equal(DeckHubLayoutMode.List, DeckHubLayoutDefaults.GetDefaultForDevice(DeviceIdiom.Phone));
    }

    [Fact]
    public void GetDefaultForDevice_TabletUsesTiles()
    {
        Assert.Equal(DeckHubLayoutMode.Tiles, DeckHubLayoutDefaults.GetDefaultForDevice(DeviceIdiom.Tablet));
    }

    [Theory]
    [InlineData(0, 196)]
    [InlineData(180, 160)]
    [InlineData(250, 180)]
    [InlineData(400, 220)]
    public void ComputeTileHeight_ClampsToReadableRange(double width, double expected)
    {
        Assert.Equal(expected, DeckHubLayoutDefaults.ComputeTileHeight(width), precision: 1);
    }
}
