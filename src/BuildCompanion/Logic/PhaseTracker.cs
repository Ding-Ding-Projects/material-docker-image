using System;

namespace BuildCompanion;

/// <summary>Best-effort phase label inferred from output lines. It only moves forward and never claims completion.</summary>
public sealed class PhaseTracker
{
    public enum Phase { Starting = 0, Dependencies = 1, Compiling = 2, Packaging = 3 }

    public Phase Current { get; private set; } = Phase.Starting;

    public Phase Observe(string line)
    {
        var next = Classify(line);
        if (next > Current) Current = next;
        return Current;
    }

    public static Phase Classify(string line)
    {
        if (string.IsNullOrEmpty(line)) return Phase.Starting;
        // The dependency readiness line names every pinned tool, Squirrel included; it is not packaging.
        if (line.TrimStart().StartsWith("Ready:", StringComparison.OrdinalIgnoreCase)) return Phase.Dependencies;
        if (Has(line, "nuget pack") || Has(line, "Squirrel") || Has(line, "releasify") || Has(line, "Attempting to build package")) return Phase.Packaging;
        if (Has(line, "Determining projects to restore") || Has(line, "dotnet publish") || Has(line, "MSBuild") || Has(line, "Restored ") || Has(line, " -> ")) return Phase.Compiling;
        if (Has(line, "Obtaining portable") || Has(line, "Ready: .NET")) return Phase.Dependencies;
        return Phase.Starting;
    }

    static bool Has(string line, string token) => line.Contains(token, StringComparison.OrdinalIgnoreCase);
}
