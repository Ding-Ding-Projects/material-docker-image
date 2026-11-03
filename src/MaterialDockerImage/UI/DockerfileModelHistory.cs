using System;
using System.Collections.Generic;

namespace MaterialDockerImage.UI;

/// <summary>
/// Undo/redo for the Creator as a stack of serialised model snapshots. <see cref="Commit"/> records the state after
/// a change; identical consecutive states are ignored. Pure and UI-independent.
/// </summary>
public sealed class DockerfileModelHistory
{
    private readonly List<string> states = new();
    private int index = -1;
    public int Capacity { get; }

    public DockerfileModelHistory(int capacity = 200) { Capacity = Math.Max(2, capacity); }

    public bool CanUndo => index > 0;
    public bool CanRedo => index >= 0 && index < states.Count - 1;
    public int UndoSteps => Math.Max(0, index);
    public int RedoSteps => index < 0 ? 0 : states.Count - 1 - index;

    /// <summary>Starts a new history with one state (no undo available).</summary>
    public void Reset(DockerfileDocument document)
    {
        states.Clear();
        states.Add(DockerfileModel.Serialize(document));
        index = 0;
    }

    /// <summary>Records the document as the newest state and drops any redo states. Returns false when nothing changed.</summary>
    public bool Commit(DockerfileDocument document)
    {
        var json = DockerfileModel.Serialize(document);
        if (index >= 0 && states[index] == json) return false;
        if (index < states.Count - 1) states.RemoveRange(index + 1, states.Count - index - 1);
        states.Add(json);
        if (states.Count > Capacity) states.RemoveAt(0);
        index = states.Count - 1;
        return true;
    }

    public DockerfileDocument? Undo()
    {
        if (!CanUndo) return null;
        index--;
        return DockerfileModel.Deserialize(states[index]);
    }

    public DockerfileDocument? Redo()
    {
        if (!CanRedo) return null;
        index++;
        return DockerfileModel.Deserialize(states[index]);
    }
}
