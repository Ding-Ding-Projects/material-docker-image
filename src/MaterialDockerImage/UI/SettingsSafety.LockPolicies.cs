using System;
using System.Collections.Generic;
using System.Linq;

namespace MaterialDockerImage.UI;

/// <summary>The kinds of credential a lock can ask for. A policy is a set of these, asked in the fixed order PIN, password, authenticator code.</summary>
[Flags]
public enum LockFactors { None = 0, Pin = 1, Password = 2, Totp = 4 }

/// <summary>How long an unlock lasts before the lock closes again.</summary>
public enum UnlockDuration
{
    /// <summary>Locks written before durations existed: they stay unlocked until the person locks them again.</summary>
    Manual = 0,
    /// <summary>This action only: the lock closes again as soon as the protected action has run.</summary>
    Once = 1,
    FiveMinutes = 2,
    OneHour = 3,
    /// <summary>Until the app closes: every such lock is locked again at the next launch.</summary>
    UntilRestart = 4
}

/// <summary>Framework-free facts about lock policies, so the wizard, the ladder and the checks agree on them.</summary>
public static class LockPolicies
{
    /// <summary>Every policy a person can choose, in the order the wizard lists them.</summary>
    public static readonly IReadOnlyList<LockPolicy> Choosable =
    [
        LockPolicy.None, LockPolicy.Pin, LockPolicy.Password, LockPolicy.Authenticator,
        LockPolicy.PinPassword, LockPolicy.PasswordTotp, LockPolicy.PinTotp, LockPolicy.PasswordPinTotp
    ];

    public static LockFactors Factors(LockPolicy policy) => policy switch
    {
        LockPolicy.Pin => LockFactors.Pin,
        LockPolicy.Password => LockFactors.Password,
        LockPolicy.Authenticator => LockFactors.Totp,
        LockPolicy.PinPassword => LockFactors.Pin | LockFactors.Password,
        LockPolicy.PasswordTotp => LockFactors.Password | LockFactors.Totp,
        LockPolicy.PinTotp => LockFactors.Pin | LockFactors.Totp,
        LockPolicy.PasswordPinTotp => LockFactors.Pin | LockFactors.Password | LockFactors.Totp,
        _ => LockFactors.None
    };

    /// <summary>The factors in the order they are asked: PIN, then password, then authenticator code.</summary>
    public static IReadOnlyList<LockFactors> Steps(LockPolicy policy)
    {
        var factors = Factors(policy);
        return new[] { LockFactors.Pin, LockFactors.Password, LockFactors.Totp }.Where(f => factors.HasFlag(f)).ToList();
    }

    public static bool IsMultiFactor(LockPolicy policy) => Steps(policy).Count > 1;

    /// <summary>The policy that asks for exactly these factors, or None when the set is empty.</summary>
    public static LockPolicy From(LockFactors factors) => Choosable.FirstOrDefault(p => Factors(p) == factors);

    /// <summary>The English wording shown for a policy. Cantonese wording lives with the other lock strings.</summary>
    public static string Label(LockPolicy policy) => policy switch
    {
        LockPolicy.Pin => "PIN",
        LockPolicy.Password => "Password",
        LockPolicy.Authenticator => "Authenticator app",
        LockPolicy.PinPassword => "PIN plus password",
        LockPolicy.PasswordTotp => "Password plus authenticator app",
        LockPolicy.PinTotp => "PIN plus authenticator app",
        LockPolicy.PasswordPinTotp => "Password plus PIN plus authenticator app",
        _ => "None (confirm only)"
    };

    public static string FactorLabel(LockFactors factor) => factor switch
    {
        LockFactors.Pin => "PIN",
        LockFactors.Password => "Password",
        LockFactors.Totp => "Code from your authenticator",
        _ => ""
    };
}

public static class PasswordRules
{
    public const int MinLength = 8, MaxLength = 128;
    public static bool IsValid(string? password) => password != null && password.Length is >= MinLength and <= MaxLength && !password.Any(char.IsControl);
    public static string? Problem(string? password) => IsValid(password) ? null : "A password is 8 to 128 characters with no control characters.";
}

public static class UnlockDurations
{
    /// <summary>The durations a person can choose in the wizard. <see cref="UnlockDuration.Manual"/> is only for locks that predate durations.</summary>
    public static readonly IReadOnlyList<UnlockDuration> Choosable = [UnlockDuration.Once, UnlockDuration.FiveMinutes, UnlockDuration.OneHour, UnlockDuration.UntilRestart];

    /// <summary>The length of a timed duration, or null for the ones that are not timed.</summary>
    public static TimeSpan? Span(UnlockDuration duration) => duration switch
    {
        UnlockDuration.FiveMinutes => TimeSpan.FromMinutes(5),
        UnlockDuration.OneHour => TimeSpan.FromHours(1),
        _ => null
    };

    public static string Label(UnlockDuration duration) => duration switch
    {
        UnlockDuration.Once => "This action only",
        UnlockDuration.FiveMinutes => "5 minutes",
        UnlockDuration.OneHour => "1 hour",
        UnlockDuration.UntilRestart => "Until the app closes",
        _ => "Until I lock it again"
    };
}

/// <summary>
/// One bounded attempt at a lock. The factors are asked in order and the ones already right are remembered only inside this object, so closing the prompt starts over.
/// A wrong answer at any step counts once against the lock's shared attempt budget and sends the attempt back to the first factor; nothing is unlocked until the last factor is right.
/// </summary>
public sealed class UnlockAttempt
{
    private readonly LockStore store;
    private readonly string id;
    private long totpStep = -1;

    internal UnlockAttempt(LockStore store, string id, IReadOnlyList<LockFactors> steps) { this.store = store; this.id = id; Steps = steps; }

    public IReadOnlyList<LockFactors> Steps { get; }
    public int Index { get; private set; }
    public bool Complete => Index >= Steps.Count;
    public LockFactors? Next => Complete ? null : Steps[Index];

    public UnlockResult Submit(string? input)
    {
        if (Complete) return new UnlockResult(UnlockOutcome.Unlocked);
        if (store.WaitUntil(id) is { } wait) { Reset(); return new UnlockResult(UnlockOutcome.Waiting, 0, wait); }
        switch (store.VerifyFactor(id, Steps[Index], input, out var step))
        {
            case LockStore.FactorCheck.Unavailable: Reset(); return new UnlockResult(UnlockOutcome.Unavailable);
            case LockStore.FactorCheck.Wrong: Reset(); return store.RegisterFailure(id);
        }
        if (Steps[Index] == LockFactors.Totp) totpStep = step;
        Index++;
        if (!Complete) return new UnlockResult(UnlockOutcome.NextFactor);
        return store.RegisterSuccess(id, totpStep);
    }

    private void Reset() { Index = 0; totpStep = -1; }
}
