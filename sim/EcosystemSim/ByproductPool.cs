namespace EcosystemSim;

public class ByproductPool
{
    public required ByproductType Type { get; init; }
    public float Amount { get; set; }
    public float DecayRate { get; init; } = 0.10f;
    public float Capacity { get; init; } = 200f;

    public void Add(float amount) => Amount = MathF.Min(Capacity, Amount + amount);
    public void Decay() => Amount = MathF.Max(0f, Amount * (1f - DecayRate));
}
