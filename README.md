# Railroader mod settings

A settings editor for your Railroader mod in the game's Preferences window, under one shared **Mods** tab. One file,
dropped into your project: no dependency, nothing for players to install. For Railroader 0.6.2 and 0.6.3 (Experimental branch).
Revision 11.

Each setting gets a row: a toggle, a dropdown, a number with - and + kept inside its range, a slider with a label at
each end, or a color, the default beside it, your help text in the tooltip. Long labels wrap to a second line,
centered on a row that grows to fit. Saving writes your mod's `settings.json` safely and keeps every key that isn't one
of its fields.

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
            SettingField.Heading("Paint"),
            SettingField.Color("color", "Lettering color", "#E8D9A0", true, "Color of the lettering."),
        };
        Action<string> log = m => MyMod.Log?.Warning("My Mod: " + m);
        var section = new SettingsSection("My Mod", settingsPath, fields, MyMod.Applied, log);
        _section = section;
        PreferencesModsTab.Add(harmony, b => (_section as SettingsSection)?.Build(b, true), log);
        // With Limits: run the held values; Current() is null when settings.json can't be read, so keep your own read.
        MyMod.Settings = section.Current()?.ToObject<MySettings>() ?? MyMod.Settings;
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
effect then, the others are tagged "after restart". Your mod reads the same `settings.json` at load. The section reads
it the way `JsonConvert.DeserializeObject` reads it into a class with `bool`, `int`, `float` and `string` members named
like the keys, so its rows and status line show what your mod runs (NaN in a number is the one exception: it counts
as a bad value). Save writes an infinite number as the field's Max or Min.

## Options

Chain these on a field. Each takes the section's current values, `s`: `s["key"]` (a toggle is 1 or 0, a choice its
index, a color `0xRRGGBB`), `s.On("key")`, `s.Option("key")`. They run when a value changes, never per frame.

- `.Limits(min, max)`: a number's range follows other fields, held live as the player edits:
  `SettingField.Int("low", "Low", 10, 0, 100, 1, true, "...").Limits(max: s => s["high"])`.
  A second `.Limits` keeps the first's limits and replaces only the ones it gives.
  A number in the file outside its range shows held and is saved held. Read your settings yourself as before, then
  take the held values in your start method, inside its try, as the example does:
  `MyMod.Settings = section.Current()?.ToObject<MySettings>() ?? MyMod.Settings;`.
  `Current()` is the saved `settings.json` as your mod's own read gets it, held; never unsaved edits. Keep limits and
  `ShowWhen` free of loops: a loop that never settles stops after one pass per field and can leave a limit broken.
- `.ValueLabel((v, s) => text)`: a live text after a number or slider, from its value and other fields:
  `.ValueLabel((v, s) => (v * s["length"] / 100).ToString("0.#", CultureInfo.InvariantCulture) + " m")`.
- `.BubbleText((v, s) => text)`: slider only. The text in the handle's bubble instead of the number:
  `.BubbleText((v, s) => v >= 51 ? "Unlimited" : "$" + v)`. With only a `.ValueLabel`, the bubble shows the label's
  text; with neither, the number, as before. No tags, one line, cut at 14 characters; empty text or a function that
  throws shows the number (the throw is logged once). Worked out when a value changes, never per frame.
- `.ShownAs(scale, places)`: a Float shown and typed as its value times `scale` at `places` decimals, saved as it is:
  dollars a mile as cents, `.ShownAs(100, 1)`, shows 0.145833 as 14.6, and typing 15 stores 0.15. A typed number
  that shows as the default's number is the default itself (14.6 stores the exact 7/48), so a default that doesn't fit
  its shown places stays exact. The box, the default beside it, the tooltip and the notes all show the scaled number;
  the file, Min, Max, Step, Limits and ValueLabel stay in the field's own units. Put the unit in the label.
- `.ShowWhen(s => bool)`: the row shows only while true; a hidden row keeps its value:
  `.ShowWhen(s => s.On("enabled"))`.

`SettingField.Color` uses the game's own color picker and saves `"#RRGGBB"`; read it into a string and through
`SettingField.TryColor` so a bad value falls back the same way. `SettingField.Heading("text")` draws a small heading
between rows and saves nothing. A field with none of these draws as in Revision 9.

`.Mirror(() => value)`, chained the same way, is for a value your mod holds in memory that something else can change,
such as a switch another mod flips. The row shows what the function gives each time the section draws (a toggle is 1
or 0): `SettingField.Bool("toolbar", "Toolbar menu", true, true, "...").Mirror(() => Switch.On ? 1 : 0)`.
Save writes it only if the player changed it here since it was last read, so a section drawn before the flip can't
undo it. When it isn't written, the file keeps its own value for the key (or none), and your `applied` callback gets
the function's value under that key. Reset to defaults writes it. It is left out of "settings.json changed outside the
game", and `Current()` still gives the file's value. A function that throws is logged once and the row keeps its value.

## Groups

`SettingField.Group("Steam")` draws a row that folds the fields after it, up to the next `Group`. Fields before the
first one stay always shown, and a `Heading` after a `Group` is a plain heading inside it. One level only: a mod, then
its groups, then its settings; there are no groups inside groups. A group is closed unless you write
`SettingField.Group("Steam", startOpen: true)`.

```csharp
SettingField.Bool("enabled", "Enabled", true, true, "..."),       // always shown
SettingField.Group("Diesel"),
SettingField.Float("diesel", "Fuel rate", 1.0, 0.1, 5, 0.1, 1, true, "..."),
SettingField.Group("Steam", startOpen: true),
SettingField.Float("steam", "Coal rate", 1.0, 0.1, 5, 0.1, 1, true, "..."),
```

A group has no key and saves nothing, takes none of the options above, and Save, Undo, Reset to defaults, Limits,
ShowWhen and Mirror treat a field inside a closed group exactly as one outside: the row is just not drawn. Whether a
group is open is the player's, kept across sessions in the game's own `Preferences.json` (the file its windows keep
their positions in), never in your `settings.json`: opening a group is not a change, and the file is not touched. A
search that matches a setting in a closed group opens it for as long as that search lasts; clearing the search gives
back what the player had. A closed group is remembered by its mod's name and its own text, so renaming either forgets it.

A group reads one level below its mod: the group's row, and the fields under it, each sit 12 pixels further in than
the one above, with the game's arrow on the group row. Fields before the first group, and sections with no group, draw
as before. How the rows line up on screen is the game's layout; the harness checks the nesting, not the pixels.

Revision 10 mods in the same tab draw exactly as before, and a Revision 11 mod's groups draw in the tab whichever mod
loaded first: the newest revision present draws the tab, and each mod's section draws itself.

## Search

The Mods tab has a search box at the top (the game's own filter field): type part of a mod's name or of a setting's
label and press Enter; its x clears it. A mod whose name matches shows all its settings; otherwise only the matching
settings show, under their headings and groups. Your section joins the search when it draws; there is nothing to
call. Rows hidden by a search keep their values, and Save, Undo and Reset still act on every setting. A mod with an older
revision of this file can't be searched by its settings: it shows when its name matches, and is hidden otherwise.
That name is the one in the mod's Info.json (the name in Manage Mods), which can differ from its section title.

Leaving the box also applies the search, and redraws the tab: a click made straight after typing, on a button
elsewhere, is lost to that redraw. Press Enter first.

## Sharing the tab

Every mod that carries this file, any revision, shares one Mods tab through a contract written at the top of
`PreferencesModsTab` in the file: a `List<Action<UIPanelBuilder>>` stored as AppDomain data under
`ashenmare.preferences-mods.v1`. Each mod adds its entry when it loads and removes it when it unloads; the mod whose
entry is first adds the tab, which draws every mod's section, each in its own try. From Revision 10, a copy records its
revision in a second slot (`ashenmare.preferences-mods.revisions.v1`) and moves its own entry to the front when it is
newer than every entry before it, so the newest revision present draws the tab; that copy draws the mods A to Z by
name, with the search box. Older revisions record nothing and count as revision 0, and only ever read the first entry,
so they keep working beside it. Only .NET and game types cross between mods, so copies from different authors and
revisions get along. If you change the file, keep that contract exactly. A new revision never needs an update from any
other mod: mixed revisions keep working side by side.

## Logs

Exception text the file logs or shows goes through its `PathClean` class first, so a player's account name and home
folder never reach Player.log or the screen; the part of a path from the game's Railroader folder on is kept, for bug
reports. A mod with no settings can use `PathClean.cs` on its own for its logs; a mod that carries `SettingsSection.cs`
already has it, so don't add both.
