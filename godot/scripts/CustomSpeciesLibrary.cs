using System.Text.Json;
using Godot;

namespace EcosystemGame;

public static class CustomSpeciesLibrary
{
    private const string SavePath = "user://custom_species.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static List<SavedCustomSpecies> _species = [];
    private static bool _loaded;

    public static event Action? Changed;
    public static string LastError { get; private set; } = string.Empty;
    public static IReadOnlyList<SavedCustomSpecies> Species
    {
        get
        {
            EnsureLoaded();
            return _species;
        }
    }

    public static SavedCustomSpecies? Find(string name)
    {
        EnsureLoaded();
        return _species.FirstOrDefault(saved =>
            string.Equals(saved.Template.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    public static bool Upsert(
        SavedCustomSpecies saved,
        string? originalName,
        out string message)
    {
        EnsureLoaded();
        if (!DemoWorldSeeder.CustomizableBaseNames.Contains(
                saved.Template.BaseSpeciesName,
                StringComparer.OrdinalIgnoreCase))
        {
            message = "Choose one of the available base species.";
            return false;
        }
        var existingNames = _species
            .Where(item => !string.Equals(
                item.Template.Name,
                originalName,
                StringComparison.OrdinalIgnoreCase))
            .Select(item => item.Template.Name);
        var reserved = ReservedNames(existingNames);
        if (!saved.Template.TryValidate(out message, reserved))
            return false;
        if (!Color.HtmlIsValid(saved.TintHex))
        {
            message = "Choose a valid sprite tint.";
            return false;
        }
        if (saved.SpriteScale is < 0.7f or > 1.4f)
        {
            message = "Sprite scale must be between 0.7 and 1.4.";
            return false;
        }

        var snapshot = _species.ToList();
        var existing = originalName is null
            ? null
            : _species.FirstOrDefault(item => string.Equals(
                item.Template.Name,
                originalName,
                StringComparison.OrdinalIgnoreCase));
        if (existing is null)
            _species.Add(saved);
        else
            _species[_species.IndexOf(existing)] = saved;

        if (!Persist(out message))
        {
            _species = snapshot;
            return false;
        }

        Changed?.Invoke();
        message = $"Saved {saved.Template.Name}.";
        return true;
    }

    public static bool Delete(string name, out string message)
    {
        EnsureLoaded();
        var existing = Find(name);
        if (existing is null)
        {
            message = "That custom species no longer exists.";
            return false;
        }

        _species.Remove(existing);
        if (!Persist(out message))
        {
            _species.Add(existing);
            return false;
        }

        Changed?.Invoke();
        message = $"Deleted {name}.";
        return true;
    }

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        var path = ProjectSettings.GlobalizePath(SavePath);
        if (!File.Exists(path))
        {
            _species = [];
            LastError = string.Empty;
            return;
        }

        try
        {
            var json = File.ReadAllText(path);
            var loaded = JsonSerializer.Deserialize<List<SavedCustomSpecies>>(json, JsonOptions) ?? [];
            var valid = new List<SavedCustomSpecies>();
            var errors = new List<string>();
            foreach (var saved in loaded)
            {
                var reserved = ReservedNames(loaded
                    .Where(item => !ReferenceEquals(item, saved))
                    .Select(item => item.Template.Name));
                var reason = string.Empty;
                if (!DemoWorldSeeder.CustomizableBaseNames.Contains(
                        saved.Template.BaseSpeciesName,
                        StringComparer.OrdinalIgnoreCase))
                {
                    reason = "unknown base species";
                }
                else
                {
                    var templateValid = saved.Template.TryValidate(out reason, reserved);
                    if (templateValid && !Color.HtmlIsValid(saved.TintHex))
                        reason = "invalid sprite tint";
                    else if (templateValid && saved.SpriteScale is < 0.7f or > 1.4f)
                        reason = "sprite scale outside 0.7–1.4";
                }

                if (!string.IsNullOrEmpty(reason))
                {
                    errors.Add($"{saved.Template.Name}: {reason}");
                    continue;
                }
                valid.Add(saved);
            }
            _species = valid;
            LastError = errors.Count == 0
                ? string.Empty
                : "Some custom species were skipped: " + string.Join("; ", errors);
        }
        catch (IOException ex)
        {
            _species = [];
            LastError = $"Unable to read custom species: {ex.Message}";
        }
        catch (UnauthorizedAccessException ex)
        {
            _species = [];
            LastError = $"Unable to access custom species: {ex.Message}";
        }
        catch (JsonException ex)
        {
            _species = [];
            LastError = $"Custom species data is invalid: {ex.Message}";
        }
    }

    private static bool Persist(out string message)
    {
        var path = ProjectSettings.GlobalizePath(SavePath);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tempPath = path + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(_species, JsonOptions));
            File.Move(tempPath, path, overwrite: true);
            LastError = string.Empty;
            message = string.Empty;
            return true;
        }
        catch (IOException ex)
        {
            LastError = $"Unable to save custom species: {ex.Message}";
        }
        catch (UnauthorizedAccessException ex)
        {
            LastError = $"Unable to save custom species: {ex.Message}";
        }

        message = LastError;
        return false;
    }

    private static IEnumerable<string> ReservedNames(IEnumerable<string> customNames)
    {
        foreach (var name in DemoWorldSeeder.BuiltInSpeciesNames.Concat(customNames))
        {
            yield return name;
            yield return $"Greater {name}";
            yield return $"Giant {name}";
            yield return $"Lesser {name}";
            yield return $"Dwarf {name}";
        }
    }
}
