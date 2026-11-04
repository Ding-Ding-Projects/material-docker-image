using System;
using System.Collections.Generic;
using System.Linq;

namespace MaterialDockerImage.UI;

public enum KeypadKind { Digit, Backspace, Clear, Cancel, Submit }

/// <summary>One key of the PIN keypad. <see cref="Digit"/> is meaningful only for a digit key.</summary>
public sealed record KeypadKey(KeypadKind Kind, char Digit, string Label);

/// <summary>
/// The on-screen PIN keypad's state and layout, an access-control style pad. It shares the lock's validator and attempt budget with typing: pressing keys fills the same entry that
/// typing fills, and submitting hands that entry to the one <see cref="UnlockAttempt"/>, so the keypad is never a second, weaker way in. The entry is never shown or announced:
/// only its length is. Paste is not offered by the pad. The layout is the usual 1-2-3 / 4-5-6 / 7-8-9 / Clear-0-Backspace, and is shuffled only when the person has chosen that.
/// </summary>
public sealed class PinKeypad
{
    private readonly List<char> entry = new();
    private readonly int maxLength;

    public PinKeypad(int maxLength = PinRules.MaxLength) { this.maxLength = Math.Clamp(maxLength, 1, PinRules.MaxLength); }

    public int Length => entry.Count;
    public int MaxLength => maxLength;

    /// <summary>The entry as dots, one per digit, so a display never shows the digits.</summary>
    public string Masked => new('•', entry.Count);

    /// <summary>What a screen reader is told after each key: how many digits are entered, never which ones.</summary>
    public string Announcement => entry.Count == 0 ? "No digits entered" : entry.Count == 1 ? "1 digit entered" : entry.Count + " digits entered";

    public bool CanSubmit => PinRules.IsValid(new string(entry.ToArray()));

    /// <summary>Adds one digit; anything else, or a digit past the maximum length, is refused. Returns whether it was accepted.</summary>
    public bool Press(char digit)
    {
        if (!char.IsAsciiDigit(digit) || entry.Count >= maxLength) return false;
        entry.Add(digit);
        return true;
    }

    /// <summary>A key from the physical keyboard: digits (main row or number pad) are the same as pressing the pad; Backspace and Escape-style keys are the caller's.</summary>
    public bool PressKeyboardText(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        var accepted = false;
        foreach (var c in text) accepted |= Press(c);
        return accepted;
    }

    public bool Backspace()
    {
        if (entry.Count == 0) return false;
        entry.RemoveAt(entry.Count - 1);
        return true;
    }

    public void Clear() => entry.Clear();

    /// <summary>Replaces the entry with what was typed in the manual field, so typing and the pad always agree. Non-digits and anything beyond the maximum are dropped.</summary>
    public void SetFromManual(string? typed)
    {
        entry.Clear();
        foreach (var c in typed ?? "") if (char.IsAsciiDigit(c) && entry.Count < maxLength) entry.Add(c);
    }

    /// <summary>
    /// Hands the entry to the lock's single attempt, exactly as typing does (a PIN that is too short is still an attempt, as it is when typed), then clears the pad whatever the answer was.
    /// There is one validator and one attempt budget; the pad adds no way around either.
    /// </summary>
    public UnlockResult Submit(UnlockAttempt attempt)
    {
        var text = new string(entry.ToArray());
        Clear();
        return attempt.Submit(text);
    }

    /// <summary>The digits entered so far, for the manual PIN field the pad fills. It is the person's own entry on their own screen and is never logged, announced or stored.</summary>
    public string EntryForField() => new(entry.ToArray());

    /// <summary>
    /// The rows of the pad, top to bottom. Unshuffled it is the standard layout. Shuffled (an explicit choice) the ten digits take fresh places while Clear and Backspace stay
    /// where hands expect them. <paramref name="random"/> is required for a shuffle so checks can be deterministic.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<KeypadKey>> Layout(bool shuffle, Random? random = null)
    {
        var digits = "1234567890".ToList();
        if (shuffle)
        {
            random ??= Random.Shared;
            for (var i = digits.Count - 1; i > 0; i--) { var j = random.Next(i + 1); (digits[i], digits[j]) = (digits[j], digits[i]); }
        }
        KeypadKey D(int i) => new(KeypadKind.Digit, digits[i], digits[i].ToString());
        return
        [
            [D(0), D(1), D(2)],
            [D(3), D(4), D(5)],
            [D(6), D(7), D(8)],
            [new KeypadKey(KeypadKind.Clear, '\0', "Clear"), D(9), new KeypadKey(KeypadKind.Backspace, '\0', "Backspace")]
        ];
    }

    /// <summary>Every key the pad offers, including the Cancel and Submit controls under it.</summary>
    public static IReadOnlyList<KeypadKey> AllKeys(bool shuffle, Random? random = null) =>
        Layout(shuffle, random).SelectMany(r => r).Concat([new KeypadKey(KeypadKind.Cancel, '\0', "Cancel"), new KeypadKey(KeypadKind.Submit, '\0', "Submit")]).ToList();

    /// <summary>The name a screen reader gets for a key. A digit is named by its digit, nothing about what has been entered.</summary>
    public static string AccessibleName(KeypadKey key) => key.Kind switch
    {
        KeypadKind.Digit => "Digit " + key.Digit,
        KeypadKind.Backspace => "Backspace, delete the last digit",
        KeypadKind.Clear => "Clear all digits",
        KeypadKind.Cancel => "Cancel",
        _ => "Submit PIN"
    };
}

/// <summary>What a click on a locked, visibly disabled control should do.</summary>
public enum DisabledClickDecision
{
    /// <summary>The control is not locked: the click goes through as normal.</summary>
    PassThrough,
    /// <summary>Locked: the click is swallowed and the control's own authentication prompt opens. The protected action does not run.</summary>
    OpenPrompt,
    /// <summary>Locked and its prompt is already open: the click is swallowed and nothing else happens.</summary>
    Swallow
}

/// <summary>
/// The rule behind "clicking a locked control opens its unlock prompt instead of doing nothing". A locked control never lets a click through, and each control has at most one prompt open at a time.
/// </summary>
public sealed class UnlockPromptGate
{
    private readonly HashSet<string> open = new(StringComparer.Ordinal);

    public static DisabledClickDecision Decide(bool locked, bool promptAlreadyOpen) =>
        !locked ? DisabledClickDecision.PassThrough : promptAlreadyOpen ? DisabledClickDecision.Swallow : DisabledClickDecision.OpenPrompt;

    public DisabledClickDecision Click(string id, bool locked) => Decide(locked, open.Contains(id));

    /// <summary>Marks the prompt for this lock as open. Returns false when it already was.</summary>
    public bool TryBegin(string id) => open.Add(id);
    public void End(string id) => open.Remove(id);
    public bool IsOpen(string id) => open.Contains(id);
}
