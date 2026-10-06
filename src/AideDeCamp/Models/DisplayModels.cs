namespace AideDeCamp.Models;

/// <summary>
/// Read-only projection used by the OOB canvas. It deliberately contains no save-file
/// line information and never writes back to Grand Tactician. Source points to the
/// canonical parsed object that remains the editor's source of truth.
/// </summary>
public sealed class DisplayCommandNode
{
    public required GroupNode Source { get; init; }
    public List<DisplayCommandNode> Subcommands { get; } = new();
    public List<CombatUnitNode> AttachedUnits { get; } = new();
    public int Depth { get; set; }
}

public sealed class DisplayOobModel
{
    public List<CombatUnitNode> UnattachedUnits { get; } = new();
    public List<DisplayCommandNode> Roots { get; } = new();
    public Dictionary<int, DisplayCommandNode> CommandsById { get; } = new();
}
