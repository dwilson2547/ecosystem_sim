namespace EcosystemSim;

public class ResourcePool
{
    public required ResourceType Type { get; init; }
    public float Amount { get; set; }
    public float Capacity { get; set; }
    public float RegenPerTick { get; set; }

    public void Regen() =>
        Amount = Math.Min(Amount + RegenPerTick, Capacity);

    public float Consume(float requested)
    {
        var granted = Math.Min(requested, Amount);
        Amount -= granted;
        return granted;
    }
}
