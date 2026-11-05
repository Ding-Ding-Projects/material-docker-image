using MaterialDockerImage.UI;

/// <summary>Checks for the transition pattern, spring bounce and wallpaper seed rules of the appearance studio. Framework-free.</summary>
public static class AppearanceMotionChecks
{
    static int count;

    static void Check(bool value, string name)
    {
        if (!value) throw new Exception(name);
        count++;
        Console.WriteLine("PASS " + name);
    }

    public static int Run()
    {
        count = 0;
        Transitions();
        Spring();
        SettingsAndLocks();
        Wallpaper();
        return count;
    }

    static void Transitions()
    {
        Check(AppearanceTransitions.Patterns.Length == 4 && AppearanceTransitions.Patterns.Distinct().Count() == 4 && AppearanceTransitions.Patterns.Select(AppearanceTransitions.Name).Distinct().Count() == 4, "four transition patterns with distinct names");
        foreach (var pattern in AppearanceTransitions.Patterns)
        {
            var off = AppearanceTransitions.For(pattern, "Off", true, 1);
            Check(!off.Animates && !off.Moves && off.DurationMs == 0, pattern + ": motion Off never animates");
            var reduced = AppearanceTransitions.For(pattern, "Reduced", true, 1);
            Check(reduced.Animates && !reduced.Moves && reduced.FromOpacity == 0 && reduced.DurationMs > 0 && reduced.DurationMs <= AppearanceMotion.Short4, pattern + ": Reduced is a short fade with no movement");
            var standard = AppearanceTransitions.For(pattern, "Standard", true, 1);
            Check(standard.Animates && standard.Moves && standard.Spatial, pattern + ": Standard moves or scales");
            var expressive = AppearanceTransitions.For(pattern, "Expressive", true, 1);
            Check(expressive.DurationMs > standard.DurationMs && Math.Abs(expressive.Dx) + Math.Abs(expressive.Dy) + Math.Abs(1 - expressive.FromScale) > Math.Abs(standard.Dx) + Math.Abs(standard.Dy) + Math.Abs(1 - standard.FromScale), pattern + ": Expressive travels further and longer");
            var slow = AppearanceTransitions.For(pattern, "Standard", true, 0.5);
            Check(Math.Abs(slow.DurationMs - standard.DurationMs * 2) < 0.001, pattern + ": half speed doubles the duration");
        }
        var forward = AppearanceTransitions.For(AppearanceTransitions.SharedAxis, "Standard", true, 1);
        var back = AppearanceTransitions.For(AppearanceTransitions.SharedAxis, "Standard", false, 1);
        Check(forward.Dx > 0 && back.Dx < 0 && forward.Dy == 0, "shared axis follows the direction of travel");
        var slide = AppearanceTransitions.For(AppearanceTransitions.Slide, "Standard", false, 1);
        Check(slide.Dx < 0 && Math.Abs(slide.Dx) > Math.Abs(forward.Dx) && slide.FromScale == 1, "slide comes from the side of travel and travels further than shared axis");
        var fade = AppearanceTransitions.For(AppearanceTransitions.FadeThrough, "Standard", true, 1);
        Check(fade.Dx == 0 && fade.Dy == 0 && fade.FromScale < 1, "fade through grows in place");
        var container = AppearanceTransitions.For(AppearanceTransitions.ContainerTransform, "Standard", true, 1);
        Check(container.FromScale < fade.FromScale && container.Dy > 0, "container transform grows from a smaller container");
        var unknown = AppearanceTransitions.For("Wobble", "Standard", true, 1);
        Check(unknown == forward, "an unknown pattern falls back to shared axis");
    }

    static void Spring()
    {
        Check(AppearanceSpring.Ease(0, 50) == 0 && AppearanceSpring.Ease(1, 50) == 1 && AppearanceSpring.Ease(double.NaN, 50) == 0, "spring starts at 0 and ends at 1");
        Check(AppearanceSpring.Overshoot(0) == 0 && AppearanceSpring.Sample(0, 60).All(p => p.Value <= 1.0000001), "0% bounce never overshoots");
        var samples = AppearanceSpring.Sample(0, 60);
        Check(samples.Zip(samples.Skip(1)).All(p => p.Second.Value >= p.First.Value - 1e-9), "0% bounce is a plain monotonic ease-out");
        Check(AppearanceSpring.Overshoot(30) > 0 && AppearanceSpring.Overshoot(100) > AppearanceSpring.Overshoot(30) && AppearanceSpring.Overshoot(100) < 0.25, "overshoot grows with bounce and stays under 25%");
        var peak = AppearanceSpring.Sample(100, 400).Max(p => p.Value) - 1;
        Check(Math.Abs(peak - AppearanceSpring.Overshoot(100)) < 0.01, "the reported overshoot matches the drawn curve");
        Check(AppearanceSpring.Overshoot(500) == AppearanceSpring.Overshoot(100) && AppearanceSpring.Overshoot(-5) == 0, "bounce is clamped to 0 to 100");
        Check(AppearanceSpring.Applies("Expressive", 30) && !AppearanceSpring.Applies("Standard", 30) && !AppearanceSpring.Applies("Reduced", 30) && !AppearanceSpring.Applies("Off", 30) && !AppearanceSpring.Applies("Expressive", 0), "spring only applies to Expressive motion with some bounce");
        // Reduced motion and Low stimulation turn Expressive into Reduced, so the spring cannot apply there.
        Check(!AppearanceSpring.Applies(AppearanceMotion.Effective("Expressive", true, false, true, false), 100) && !AppearanceSpring.Applies(AppearanceMotion.Effective("Expressive", false, true, true, false), 100), "reduced motion and low stimulation stop the spring");
    }

    static void SettingsAndLocks()
    {
        var settings = new UserSettings();
        Check(settings.TransitionPattern == AppearanceTransitions.SharedAxis && settings.SpringBounce == AppearanceSpring.Default, "defaults: shared axis and the default bounce");
        settings.TransitionPattern = "Spin"; settings.SpringBounce = 400; settings.Normalize();
        Check(settings.TransitionPattern == AppearanceTransitions.Default && settings.SpringBounce == 100, "settings normalise an unknown pattern and an out-of-range bounce");
        settings.TransitionPattern = AppearanceTransitions.Slide; settings.SpringBounce = 70;
        var state = AppearanceState.FromSettings(settings);
        Check(state.TransitionPattern == AppearanceTransitions.Slide && state.SpringBounce == 70, "the appearance model reads both values");
        var copy = new UserSettings(); state.WriteTo(copy);
        Check(copy.TransitionPattern == AppearanceTransitions.Slide && copy.SpringBounce == 70, "the appearance model writes both values back");
        var json = state.ToJson();
        var round = AppearanceState.FromJson(json);
        Check(round.TransitionPattern == AppearanceTransitions.Slide && round.SpringBounce == 70, "both values survive the appearance JSON (undo, presets, export)");
        var invalid = state.Clone(); invalid.TransitionPattern = "Spin"; invalid.SpringBounce = 101;
        Check(invalid.Validate().Count == 2, "an imported preset with a bad pattern or bounce is refused");
        var look = new AppearanceState { Theme = "Light", TransitionPattern = AppearanceTransitions.FadeThrough, SpringBounce = 0 };
        var applied = state.WithLookOf(look);
        Check(applied.Theme == "Light" && applied.TransitionPattern == AppearanceTransitions.Slide && applied.SpringBounce == 70, "presets change the look but keep the transition pattern and bounce");
        var settingsJson = System.Text.Json.JsonSerializer.Serialize(settings);
        Check(settingsJson.Contains("\"TransitionPattern\":\"Slide\"") && settingsJson.Contains("\"SpringBounce\":70"), "the settings export carries both values");

        Check(ElementLocks.Properties["transition"].SequenceEqual(new[] { "TransitionPattern" }) && ElementLocks.Properties["springbounce"].SequenceEqual(new[] { "SpringBounce" }), "both values are lockable settings rows");
        var locked = new UserSettings(); ElementLocks.Lock(locked, "transition"); ElementLocks.Lock(locked, "springbounce");
        var before = new AppearanceState();
        var proposed = before.Clone(); proposed.TransitionPattern = AppearanceTransitions.Slide; proposed.SpringBounce = 90; proposed.Density = "Compact";
        var guarded = AppearanceLocks.GuardState(locked, before, proposed, out var held);
        Check(guarded.TransitionPattern == before.TransitionPattern && guarded.SpringBounce == before.SpringBounce && guarded.Density == "Compact" && held.Contains("transition") && held.Contains("springbounce"), "locked transition and bounce keep their values while other values change");
        var target = new UserSettings { TransitionPattern = AppearanceTransitions.Slide, SpringBounce = 5 };
        ElementLocks.Preserve(locked, target);
        Check(target.TransitionPattern == locked.TransitionPattern && target.SpringBounce == locked.SpringBounce, "an import or reset keeps locked transition and bounce values");
        Check(AppearanceLocks.LabelFor("transition") == "Appearance: Transition pattern" && AppearanceLocks.LabelFor("springbounce") == "Appearance: Spring bounce", "the Locks manager names both locks");
    }

    static byte[] Pixels(params (byte R, byte G, byte B, byte A, int Count)[] runs)
    {
        var list = new List<byte>();
        foreach (var (r, g, b, a, n) in runs) for (var i = 0; i < n; i++) list.AddRange([b, g, r, a]);
        return list.ToArray();
    }

    static void Wallpaper()
    {
        var mostlyRed = Pixels((200, 30, 30, 255, 70), (30, 60, 200, 255, 30));
        Check(AppearanceWallpaperColour.SeedFromBgra(mostlyRed, 10, 10, 40) == "#C81E1E", "the larger of two vivid colours wins");
        var greyAndAccent = Pixels((128, 128, 128, 255, 80), (20, 180, 90, 255, 20));
        Check(AppearanceWallpaperColour.SeedFromBgra(greyAndAccent, 10, 10, 40) == "#14B45A", "a vivid accent beats a larger grey area");
        var greyOnly = Pixels((120, 120, 120, 255, 100));
        Check(AppearanceWallpaperColour.SeedFromBgra(greyOnly, 10, 10, 40) == "#787878", "an all-grey picture gives a grey seed");
        var transparent = Pixels((255, 0, 0, 0, 90), (0, 0, 255, 255, 10));
        Check(AppearanceWallpaperColour.SeedFromBgra(transparent, 10, 10, 40) == "#0000FF", "transparent pixels are ignored");
        Check(AppearanceWallpaperColour.SeedFromBgra(Pixels((255, 0, 0, 0, 4)), 2, 2, 8) == null, "a fully transparent picture gives no seed");
        var mixed = Pixels((200, 30, 30, 255, 40), (206, 28, 20, 255, 40), (0, 0, 0, 255, 20));
        Check(AppearanceWallpaperColour.SeedFromBgra(mixed, 10, 10, 40) == "#CB1D19", "the seed is the average of the winning bucket");
        var padded = new byte[2 * 48];
        padded[0] = 10; padded[1] = 20; padded[2] = 250; padded[3] = 255; // one red pixel per row, the rest is padding
        padded[48] = 10; padded[49] = 20; padded[50] = 250; padded[51] = 255;
        Check(AppearanceWallpaperColour.SeedFromBgra(padded, 1, 2, 48) == "#FA140A", "row stride padding is skipped");
        Check(AppearanceWallpaperColour.SeedFromBgra(new byte[10], 10, 10, 40) == null && AppearanceWallpaperColour.SeedFromBgra(mostlyRed, 0, 10, 40) == null, "short or empty buffers are refused");

        Check(AppearanceWallpaperColour.TryParseRegistryColour("58 110 165", out var solid) && solid == "#3A6EA5", "the Windows background colour value is parsed");
        Check(!AppearanceWallpaperColour.TryParseRegistryColour("300 0 0", out _) && !AppearanceWallpaperColour.TryParseRegistryColour("1 2", out _) && !AppearanceWallpaperColour.TryParseRegistryColour(null, out _) && !AppearanceWallpaperColour.TryParseRegistryColour("-1 2 3", out _), "malformed background colours are refused");

        var picture = AppearanceWallpaperColour.Decide(@"C:\pictures\a.jpg", true, "#112233", "0 0 0");
        Check(picture.Source == WallpaperSource.Picture && picture.Seed == "#112233", "a readable picture gives its seed");
        var unreadable = AppearanceWallpaperColour.Decide(@"C:\pictures\missing.jpg", false, null, "0 0 0");
        Check(unreadable.Source == WallpaperSource.Unreadable && unreadable.Seed == null, "an unreadable picture is reported and never replaced by the solid colour");
        var solidResult = AppearanceWallpaperColour.Decide("", false, null, "58 110 165");
        Check(solidResult.Source == WallpaperSource.SolidColour && solidResult.Seed == "#3A6EA5", "no picture (solid colour or slideshow gap) uses the background colour");
        var nothing = AppearanceWallpaperColour.Decide(null, false, null, "garbage");
        Check(nothing.Source == WallpaperSource.Nothing && nothing.Seed == null, "nothing usable is reported honestly");
    }
}
