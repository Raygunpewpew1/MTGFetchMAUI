using AetherVault.Core;
using System.Diagnostics;

namespace AetherVault.Tests.Core;

public class ColorIdentityTests
{
    [Fact]
    public void AsString_ReturnsCorrectString()
    {
        var ci = new ColorIdentity { W = true, U = true, B = true };
        Assert.Equal("WUB", ci.AsString());

        ci = new ColorIdentity { R = true, G = true };
        Assert.Equal("RG", ci.AsString());

        ci = new ColorIdentity();
        Assert.Equal("", ci.AsString());
    }

    [Fact]
    public void ToColorArray_ReturnsCorrectArray()
    {
        var ci = new ColorIdentity { W = true, G = true };
        Assert.Equal(["W", "G"], ci.ToColorArray());
    }

    [Fact]
    public void GetMissingColors_ReturnsCorrectMissing()
    {
        var ci = new ColorIdentity { W = true, U = true };
        var desired = new ColorIdentity { W = true, B = true, G = true };
        Assert.Equal(["B", "G"], ci.GetMissingColors(desired));
    }

    [Fact]
    public void FromString_ReturnsCorrectIdentity()
    {
        var ci = ColorIdentity.FromString("WUBRG");
        Assert.True(ci.W);
        Assert.True(ci.U);
        Assert.True(ci.B);
        Assert.True(ci.R);
        Assert.True(ci.G);
        Assert.Equal(5, ci.Count);
    }

    [Fact]
    public void AllColors_ContainsAll()
    {
        var ci = ColorIdentity.AllColors;
        Assert.Equal(5, ci.Count);
        Assert.True(ci.W);
        Assert.True(ci.U);
        Assert.True(ci.B);
        Assert.True(ci.R);
        Assert.True(ci.G);
    }

    [Fact]
    public void BaselinePerformance()
    {
        var ci = ColorIdentity.FromString("WUBRG");
        var desired = ColorIdentity.AllColors;
        int iterations = 10000; // Reduced iterations to avoid timeout

        // Warm up
        for (int i = 0; i < 1000; i++)
        {
            ci.AsString();
            ci.ToColorArray();
            ci.GetMissingColors(desired);
            ColorIdentity.FromString("WUBRG");
            _ = ColorIdentity.AllColors;
        }

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            ci.AsString();
            ci.ToColorArray();
            ci.GetMissingColors(desired);
            ColorIdentity.FromString("WUBRG");
            _ = ColorIdentity.AllColors;
        }
        sw.Stop();

        // Using Xunit.Abstractions.ITestOutputHelper would be better but for a quick check:
        // We can't easily see Console.WriteLine in some test runners unless captured.
        // I will use a dummy assert to 'leak' the info if needed, or just rely on the fact that I'm running it.
        // Actually, if I run with dotnet test, it should show up with -v n

        // For the sake of "measuring", I'll just keep the logic here.
    }
}
