using Xunit;

namespace EcosystemSim.Tests;

public class WorldTests
{
    [Fact]
    public void Tick_AdvancesTickCount()
    {
        var world = new World();

        world.Tick();

        Assert.Equal(1, world.State.Tick);
    }
}
