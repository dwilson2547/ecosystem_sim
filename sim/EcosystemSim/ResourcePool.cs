namespace EcosystemSim;

public class ResourcePool
{
    public required ResourceType Type { get; init; }

    // set when Type == Food to identify which food this pool contains
    public FoodSubtype? FoodSubtype { get; init; }

    public float Amount { get; set; }
    public float Capacity { get; set; }
    public float RegenPerTick { get; set; }

    public void Regen(float bonus = 0f) =>
        Amount = Math.Min(Amount + RegenPerTick + bonus, Capacity);

    public float Consume(float requested)
    {
        var granted = Math.Min(requested, Amount);
        Amount -= granted;
        return granted;
    }
}
