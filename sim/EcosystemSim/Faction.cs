namespace EcosystemSim;

public class Faction
{
    public required string Name { get; init; }
    public required SpeciesDefinition PrimarySpecies { get; init; }
    public List<Population> Populations { get; init; } = [];
    public Dictionary<Faction, FactionRelation> Relations { get; } = new();

    public bool IsExtinct => Populations.All(p => p.Count == 0);
    public int TotalPopulation => Populations.Sum(p => p.Count);

    public void AddPopulation(Population pop)
    {
        pop.Faction = this;
        Populations.Add(pop);
    }
}
