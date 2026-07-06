namespace EcosystemSim;

// World-level weather, layered on top of seasons. Multi-tick spells of Rainy / Drought punctuate
// Normal weather and scale resource regen (stacking with the season multiplier), so a drought in
// winter is brutal and rain in spring is lush.
public enum Weather { Normal, Rainy, Drought }
