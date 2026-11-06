# Railroader mod settings

A settings editor for your Railroader mod in the game's Preferences window, under one shared **Mods** tab. One file,
dropped into your project: no dependency, nothing for players to install. For Railroader 0.6.2 (Experimental branch).
Revision 9.

Each setting gets a row: a toggle, a dropdown, a number with - and + kept inside its range, or a slider with a label at
each end, the default beside it, your help text in the tooltip. Long labels wrap to a second line, centered on a row that grows to fit. Saving writes your
mod's `settings.json` safely and keeps any keys it doesn't know.

## Use it

1. Copy `SettingsSection.cs` into your project and change only its namespace line (`namespace SETTINGS_NAMESPACE`) to
   yours. Keep the rest exactly as it is.
2. Reference the game's `Railroader.dll`, `Newtonsoft.Json.dll`, `Unity.TextMeshPro.dll`, `UnityEngine.dll`,
   `UnityEngine.CoreModule.dll`, `UnityEngine.UI.dll` and `UnityEngine.TextRenderingModule.dll` (all in
   `Railroader_Data/Managed`), and Harmony (`0Harmony`).
3. When your mod loads, build the section and add it to the tab. When it unloads, remove it.

```csharp
internal static class SettingsUi
{
    private static object _section;   // a SettingsSection, typed object so a game update that breaks it stays here

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void Start(Harmony harmony, string settingsPath)
    {
        Stop();
        var fields = new[]
        {
            SettingField.Bool("enabled", "Enabled", true, true, "Turns the mod on or off."),
            SettingField.Int("percent", "Share (%)", 30, 0, 100, 5, true, "How many go the other way."),
            SettingField.Float("miles", "Minimum distance (miles)", 1, 0, 100, 1, 1, true, "Skip anything nearer."),
            SettingField.Choice("mode", "Mode", new[] { "nearest", "farthest" }, 0, false, "Which one goes first."),
            SettingField.IntSlider("steam", "Steam share", 50, 0, 100, 5, true, "Chance a locomotive is steam.",
                "Diesel", "Steam"),
        };
        Action<string> log = m => MyMod.Log?.Warning("My Mod: " + m);
        _section = new SettingsSection("My Mod", settingsPath, fields, MyMod.Applied, log);
        PreferencesModsTab.Add(harmony, b => (_section as SettingsSection)?.Build(b, true), log);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void Stop()
    {
        PreferencesModsTab.Remove();
        _section = null;
    }
}
```

Call `SettingsUi.Start` inside a try after your own patches are in, and `SettingsUi.Stop` (also in a try) on unload.
A slider saves exactly what a number row with the same key would, so a field can switch between the two with no
change to anyone's `settings.json`. The left label sits at the minimum.

`MyMod.Applied` receives the saved file as a `JObject` when the player saves; fields marked live (`true`) can take
effect then, the others are tagged "after restart". Your mod reads the same `settings.json` at load.

## Sharing the tab

Every mod that carries this file, any revision, shares one Mods tab through a frozen contract written at the top of
`PreferencesModsTab` in the file: a `List<Action<UIPanelBuilder>>` stored as AppDomain data under
`ashenmare.preferences-mods.v1`. The first mod in the list adds the tab; the tab draws every mod's section in turn, each
in its own try. Only .NET and game types cross between mods, so copies from different authors and revisions get along.
If you change the file, keep that contract exactly. A new revision never needs an update from any other mod: mixed
revisions keep working side by side.

## Logs

Exception text the file logs or shows goes through its `PathClean` class first, so a player's account name and home
folder never reach Player.log or the screen; the part of a path from the game's Railroader folder on is kept, for bug
reports. A mod with no settings can use `PathClean.cs` on its own for its logs; a mod that carries `SettingsSection.cs`
already has it, so don't add both.
