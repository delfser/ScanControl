namespace ScanControl.Core;

/// <summary>Bounded, detached snapshots; no scanner operations are replayed.</summary>
public sealed class ProjectHistory(int capacity = 50)
{
    private readonly List<Project> undo = [];
    private readonly List<Project> redo = [];
    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;
    public static Project Copy(Project p) => p with { Channels = p.Channels.Select(c => c with { }).ToList() };
    public void Record(Project before)
    {
        if (capacity < 1) throw new InvalidOperationException("History capacity must be positive.");
        undo.Add(Copy(before));
        if (undo.Count > capacity) undo.RemoveAt(0);
        redo.Clear();
    }
    public Project Undo(Project current) => Move(undo, redo, current);
    public Project Redo(Project current) => Move(redo, undo, current);
    private static Project Move(List<Project> source, List<Project> target, Project current)
    {
        if (source.Count == 0) throw new InvalidOperationException("Keine weitere Änderung vorhanden.");
        target.Add(Copy(current));
        var result = source[^1]; source.RemoveAt(source.Count - 1);
        return Copy(result);
    }
    public void Clear() { undo.Clear(); redo.Clear(); }
}
