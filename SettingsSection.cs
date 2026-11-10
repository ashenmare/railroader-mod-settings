// SettingsSection.cs (Revision 10)
// A settings editor for a Railroader mod, shown in Preferences > Mods (from the main menu and in game). Every mod that
// carries this file shares one Mods tab.
//
// USE
//   1. Copy this file into your mod. Change only the namespace line.
//   2. Make a SettingsSection with your fields (SettingField.Bool, Int, Float, Choice, IntSlider, FloatSlider, Color,
//      Heading) and your settings.json path. A slider field is a number field drawn as a slider: same key, same saved
//      value.
//   3. Load: PreferencesModsTab.Add(harmony, b => section.Build(b, true), log). Unload: PreferencesModsTab.Remove().
//   4. Your mod reads settings.json itself at load. `applied` gets the saved file (a JObject) when the player saves.
//   5. Options, chained on a field (each returns a new field; a field without them draws and saves as before):
//        .Limits(min, max)   a number's range from other fields, either may be null: .Limits(max: s => s["high"])
//                            (a second .Limits keeps the first's limits and replaces only the ones it gives)
//        .ValueLabel(text)   a number's or slider's live text, from its value as shown and the other fields:
//                            .ValueLabel((v, s) => Math.Round(v * s["length"] / 100) + " m")
//        .ShowWhen(show)     the row shows only while this is true: .ShowWhen(s => s.On("enabled"))
//      s is a SettingValues: the other fields' values as the rows show them now, saved or not (s["key"] a number, a
//      toggle 1 or 0, a choice its index, a color 0xRRGGBB; s.On("key"); s.Option("key") a choice's text).
//      A mod using Limits reads its settings itself as before, then in the NoInlining start that makes the section,
//      inside its try: settings = section.Current()?.ToObject<YourSettings>() ?? settings. It then runs the held
//      values. Current() is the saved settings.json as the mod's own read gets it, held; never unsaved edits.
//
// RULES
//   - Every copy stays identical except the namespace. If you change the file, bump Revision and keep the shared-tab
//     contract in PreferencesModsTab exactly: that contract is what lets copies of any revision share the tab.
//   - Keep the section off your mod's entry class: hold it in its own static class with object-typed fields, create it
//     from a NoInlining method called inside a try, and remove it the same way on every unload or give-up path. Then a
//     game update that breaks a type this file uses costs only the settings section, not your mod (on Mono, a static
//     field of a missing type fails the whole class that holds it).
//   - The tab's Harmony patch is applied by hand, never by attribute, so your own PatchAll can't sweep it in.
//
// BEHAVIOR
//   - Rows: a toggle, a dropdown, a slider, a color, or - number + held between min and max. The default shows dimmed
//     beside a toggle, dropdown, color or number (a slider's is in its tooltip), help is in the tooltip, and fields the
//     mod reads only at load are tagged "after restart".
//   - A slider has an optional text at each end ("Steam", "Diesel"); the handle shows the number, so there is no
//     separate value text (ValueText is accepted and not drawn). It moves in steps of Step from Min, and the last stop
//     is Max. Dragging edits the value like typing does; Save, Undo and Reset work the same. The number is not typed on
//     a slider row. Keep an end text to 14 characters (a longer one is cut with "..."). A FloatSlider shows at least as
//     many decimals as its step has.
//   - Every label in a section keeps one size: a long label wraps to a second line (the game's own field label would
//     shrink it to fit, so long ones would be smaller than the rest) and its row grows to hold both lines, so rows
//     never overlap. A label that needs a third line shows two lines, with "..." at the end of the second. A single
//     word wider than the label (about 17 capitals) can't wrap: keep words short. The label's full text is in the
//     row's tooltip.
//   - Choices are saved as their text. Text that matches no option (ignoring case and outer spaces) falls back to the
//     default for that one setting.
//   - Numbers: "1,000" and "1 000" are a thousand; "12,5" is 12.5 where decimals are allowed. Input that reads two ways
//     ("1.000", "0,125") is refused, never guessed.
//   - Saving re-reads the file, keeps every key that isn't one of its fields, and writes atomically (temp file, then
//     replace). A file it couldn't read is never written over. A field's key in other capitals ("ON") is saved as the
//     field's own key ("on").
//   - settings.json is read the way the mod's own read (JsonConvert into its settings class: bool, int, float, string)
//     reads it, so the rows, the status line and Current() show what the mod runs. Only NaN in a number isn't: the
//     mod takes it, and here it counts as a bad value. Save writes infinity (1e39 in a float) as Max or Min, and says
//     so when it changed the file but no setting.
//   - A status line says when the mod isn't running what's shown (a bad value, a hand edit, a failed apply).
//   - Limits: a number is held inside its range as other fields change, and its row shows the held number; the number
//     the player set comes back if the range opens again before a save. Save writes the held number. A number in the
//     file outside its range is shown held and the file keeps it until the player saves. Ranges are worked out in
//     field order from the held values, again until nothing moves; Min and Max stay the outer bounds, a limit is
//     rounded inward to the field's decimals, and a lower limit above the upper one wins. A limit that throws or
//     isn't a number is ignored. Limits and ShowWhen that depend on each other in a loop may never settle: after one
//     pass per field and one more, the rows show and Save writes what the last pass left, which can break a limit
//     (logged once). Keep them free of loops.
//     Clicking into a held number and out again changes nothing.
//   - ShowWhen: a hidden row keeps its value, unheld, and is saved as it is.
//   - ValueLabel: after the slider's right end text or the number's + button, one line in a fixed width.
//   - Color: the game's color dropdown (swatch, picker and a hex box), with the default dimmed beside it, saved as
//     "#RRGGBB". The box and the file take #RRGGBB or #RGB (the # optional, any case). Anything else keeps the
//     color; in the file it means the default, so read yours with SettingField.TryColor and the mod agrees.
//   - Heading: a small heading between rows. It has no key and saves nothing.
//   - The Mods tab: the newest revision of this file present draws it, mods A to Z by name, with a search box at the
//     top that finds mods by name and settings by label, on Enter. A section joins the search as it draws; nothing to
//     call. Under a search, a mod of an older revision draws only when its name matches. How it works:
//     PreferencesModsTab, ModsOrder, ModsSearch.
//   - Limits, ValueLabel and ShowWhen functions run when a value changes, never per frame. One that throws is logged
//     once (a ShowWhen then shows its row, a ValueLabel shows nothing).
//   - Reset to defaults asks first. Nothing runs per frame: rows are built when the game builds the panel, edits update
//     in place, and a click asks the game for a rebuild after it, never inside it.
//   - Nothing throws into the game; each problem is logged once.
//   - Logs and the screen go through PathClean: the player's home folder and account name are dropped, and the path
//     from the Railroader folder on is kept. "Show where it is" shows the full path only when the player asks.
//     PathClean (at the end of this file) names no game type and also works alone in a mod with no settings.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TMPro;
using UI.Builder;
using UI.PreferencesWindow;
using UnityEngine;

namespace SETTINGS_NAMESPACE
{
    internal enum SettingKind { Bool, Int, Float, Choice, Color, Heading }

    internal sealed class SettingField
    {
        internal readonly string Key, Label, Help;
        internal readonly SettingKind Kind;
        internal readonly double Default, Min, Max, Step;
        internal readonly int Decimals;
        internal readonly bool Live;   // true: the mod reads it each time it's used, so a save takes effect at once
        internal readonly string[] Options;   // Choice only: the texts written to the file, in dropdown order
        internal readonly bool Slider;        // Int or Float drawn as a slider (same key and saved value as without)
        internal readonly string LeftLabel, RightLabel, ValueText;   // slider only: the end texts; ValueText is kept, not drawn

        private SettingField(string key, string label, SettingKind kind, double def, double min, double max, double step,
                             int decimals, bool live, string help, string[] options = null, bool slider = false,
                             string left = null, string right = null, string valueText = null)
        {
            Key = key; Label = label; Kind = kind; Default = def; Min = min; Max = max; Step = step;
            Decimals = decimals; Live = live; Help = help; Options = options;
            Slider = slider; LeftLabel = left; RightLabel = right; ValueText = valueText;
        }

        internal static SettingField Bool(string key, string label, bool def, bool live, string help) =>
            new SettingField(key, label, SettingKind.Bool, def ? 1 : 0, 0, 1, 1, 0, live, help);

        internal static SettingField Int(string key, string label, int def, int min, int max, int step, bool live, string help) =>
            new SettingField(key, label, SettingKind.Int, def, min, max, Math.Max(1, step), 0, live, help);

        internal static SettingField Float(string key, string label, double def, double min, double max, double step,
                                           int decimals, bool live, string help) =>
            new SettingField(key, label, SettingKind.Float, def, min, max, step > 0 ? step : 1, Math.Max(0, decimals), live, help);

        // An Int drawn as a slider: left and right are the texts at the two ends (either may be null), valueText the
        // value's text with {0} for the number ("{0}% steam"; null shows the number alone). Saved like Int.
        internal static SettingField IntSlider(string key, string label, int def, int min, int max, int step, bool live,
                                               string help, string left, string right, string valueText = null) =>
            new SettingField(key, label, SettingKind.Int, def, min, max, Math.Max(1, step), 0, live, help, null, true, left, right, valueText);

        // A Float drawn as a slider. Saved like Float (same decimals, but never fewer than its step needs).
        internal static SettingField FloatSlider(string key, string label, double def, double min, double max, double step,
                                                 int decimals, bool live, string help, string left, string right, string valueText = null) =>
            new SettingField(key, label, SettingKind.Float, def, min, max, step > 0 ? step : 1,
                             Math.Max(Math.Max(0, decimals), PlacesOf(step > 0 ? step : 1)), live, help, null, true, left, right, valueText);

        // The decimals a step needs to show exactly (0.05 needs 2), up to 6. A slider whose step is finer than its
        // decimals would round two stops to the same shown value, so FloatSlider raises its decimals to this.
        private static int PlacesOf(double step)
        {
            for (int d = 0; d < 6; d++) if (Math.Abs(Math.Round(step, d) - step) < 1e-9) return d;
            return 6;
        }

        // One of a fixed list of texts, shown as a dropdown. Held as the option's index; the file holds its text.
        internal static SettingField Choice(string key, string label, string[] options, int def, bool live, string help)
        {
            if (options == null || options.Length == 0) throw new ArgumentException(key + ": a choice needs at least one option");
            string[] copy = (string[])options.Clone();
            int d = Math.Max(0, Math.Min(copy.Length - 1, def));
            return new SettingField(key, label, SettingKind.Choice, d, 0, copy.Length - 1, 1, 0, live, help, copy);
        }

        internal int Places => Kind == SettingKind.Int || Kind == SettingKind.Choice ? 0 : Decimals;

        // The option at a held index (kept inside the list).
        internal string OptionAt(double v) => Options[(int)Math.Max(0, Math.Min(Options.Length - 1, Math.Round(v)))];

        // The index of a text in the file, or -1: trimmed, ignoring case.
        internal int OptionIndex(string text)
        {
            if (Options == null || text == null) return -1;
            string t = text.Trim();
            for (int i = 0; i < Options.Length; i++)
                if (string.Equals(Options[i], t, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        // A color, saved as "#RRGGBB"; def is like "#4A7F2C". Drawn with the game's color dropdown.
        internal static SettingField Color(string key, string label, string def, bool live, string help)
        {
            if (!TryColor(def, out int rgb)) throw new ArgumentException(key + ": a color's default must be like #RRGGBB");
            return new SettingField(key, label, SettingKind.Color, rgb, 0, 0xFFFFFF, 1, 0, live, help);
        }

        // A small heading between rows. It has no key and saves nothing.
        internal static SettingField Heading(string text) => new SettingField(null, text, SettingKind.Heading, 0, 0, 0, 1, 0, true, null);

        // Reads a color the way the section does: #RRGGBB or #RGB, the # optional, any case, outer spaces ignored.
        // rgb is 0xRRGGBB. False for anything else (a name, an alpha, a stray character).
        internal static bool TryColor(string text, out int rgb)
        {
            rgb = 0;
            string s = (text ?? "").Trim();
            if (s.Length > 0 && s[0] == '#') s = s.Substring(1);
            if (s.Length != 3 && s.Length != 6) return false;
            int v = 0;
            foreach (char ch in s)
            {
                int d = ch >= '0' && ch <= '9' ? ch - '0' : ch >= 'a' && ch <= 'f' ? ch - 'a' + 10 : ch >= 'A' && ch <= 'F' ? ch - 'A' + 10 : -1;
                if (d < 0) return false;
                v = s.Length == 3 ? v * 256 + d * 17 : v * 16 + d;
            }
            rgb = v;
            return true;
        }

        // 0xRRGGBB as "#RRGGBB", capitals.
        internal static string ColorHex(int rgb) => "#" + (rgb & 0xFFFFFF).ToString("X6", CultureInfo.InvariantCulture);

        // The options, null when unused. Set through Limits, ValueLabel and ShowWhen, which return a copy.
        internal Func<SettingValues, double> MinOf { get; private set; }
        internal Func<SettingValues, double> MaxOf { get; private set; }
        internal Func<double, SettingValues, string> ValueLabelOf { get; private set; }
        internal Func<SettingValues, bool> ShowIf { get; private set; }

        // A number whose range follows other fields: min and max (either may be null) give the limits from the
        // section's current values. Min and Max stay the outer bounds. A limit left null keeps one an earlier
        // .Limits gave, so .Limits(min: a).Limits(max: b) has both.
        internal SettingField Limits(Func<SettingValues, double> min = null, Func<SettingValues, double> max = null)
        {
            NumbersOnly("Limits");
            SettingField c = Copy();
            c.MinOf = min ?? MinOf;
            c.MaxOf = max ?? MaxOf;
            return c;
        }

        // A number's or slider's live text, from its value as shown and the other fields' values.
        internal SettingField ValueLabel(Func<double, SettingValues, string> text)
        {
            NumbersOnly("ValueLabel");
            SettingField c = Copy();
            c.ValueLabelOf = text;
            return c;
        }

        // The row shows only while show is true. A hidden row keeps its value and is saved as it is.
        internal SettingField ShowWhen(Func<SettingValues, bool> show)
        {
            SettingField c = Copy();
            c.ShowIf = show;
            return c;
        }

        private SettingField Copy() => (SettingField)MemberwiseClone();

        private void NumbersOnly(string what)
        {
            if (Kind != SettingKind.Int && Kind != SettingKind.Float) throw new ArgumentException(Key + ": " + what + " is for number fields");
        }

        internal bool Linked => MinOf != null || MaxOf != null;

        // Has an option, or is a Color or a Heading. A field that isn't takes the plain rows.
        internal bool Extra => Linked || ValueLabelOf != null || ShowIf != null || Kind == SettingKind.Color || Kind == SettingKind.Heading;
    }

    // The section's values by key as its rows show them now (held in range, saved or not), for Limits, ValueLabel and
    // ShowWhen. A toggle is 1 or 0, a choice its option's index, a color 0xRRGGBB. An unknown key throws.
    internal sealed class SettingValues
    {
        private readonly SettingField[] _fields;
        private readonly double[] _values;
        private readonly Dictionary<string, int> _at = new Dictionary<string, int>();

        internal SettingValues(SettingField[] fields, double[] values)
        {
            _fields = fields;
            _values = values;
            for (int i = 0; i < fields.Length; i++)
                if (fields[i].Key != null && !_at.ContainsKey(fields[i].Key)) _at[fields[i].Key] = i;
        }

        internal double this[string key] => _values[At(key)];

        internal bool On(string key) => _values[At(key)] != 0;

        // A choice's option text.
        internal string Option(string key)
        {
            int i = At(key);
            if (_fields[i].Kind != SettingKind.Choice) throw new ArgumentException(key + " isn't a choice");
            return _fields[i].OptionAt(_values[i]);
        }

        private int At(string key)
        {
            if (key != null && _at.TryGetValue(key, out int i)) return i;
            throw new KeyNotFoundException("no setting " + key);
        }
    }

    internal enum TypedNumber { Ok, NotANumber, Ambiguous }

    internal sealed class SettingsSection
    {
        internal const int Revision = 10;

        private enum FileState { Missing, Read, Unreadable }

        private readonly string _title, _path, _note;
        private readonly SettingField[] _fields;
        private readonly Action<JObject> _applied;   // after a save: the saved file, so the mod can take its live values
        private readonly Action<string> _log;
        private readonly HashSet<string> _logged = new HashSet<string>();
        private readonly UIState<bool> _expanded = new UIState<bool>(false);
        private readonly UIState<bool> _searchOpen = new UIState<bool>(true);   // open or closed while a search shows it
        private string _searchFor;       // the query _searchOpen was last opened for
        private bool[] _search;          // the rows the search shows in this draw; null: every row
        private readonly double[] _values, _saved;

        private bool _loaded, _confirmReset, _showPath, _redraw;
        private bool _updated;           // the last save changed the file's content (keys added, a spelling dropped)
        private string _stamp;           // the file's size and write time when last read
        private FileState _state = FileState.Missing;
        private string _problem;         // why the file couldn't be read
        private string _status = "";     // the last action's message; cleared when the panel is opened afresh
        private string _editNote;        // the last typed edit's warning, repeated by the Save, Undo or Reset that follows
        private string _behind;          // why the mod isn't running what's shown, until a save it takes puts it right
        private TMP_Text _statusText;    // the status line of the last build, changed in place by edits

        // _values are the values as set (typed, loaded); _held what the rows show and Save writes: each shown field
        // with Limits held in its range, every other value as set. _shown is which rows ShowWhen shows. _check and
        // _checkShown are the same worked out from _saved, for what the mod runs. With no field that has an option or
        // is a Color or Heading (_extras false), _held is a copy of _values and nothing else here runs.
        private readonly double[] _held, _check;
        private readonly bool[] _shown, _checkShown;
        private readonly string[] _labels;              // each ValueLabel's text for the held values
        private readonly SettingValues _view, _checkView;
        private readonly bool _extras;
        private RectTransform[] _rows;                  // the last build's rows, to show or hide in place
        private TMP_InputField[] _inputs;               // its number boxes, set in place when a held value moves
        private TMP_Text[] _valueTexts;                 // its value labels

        internal SettingsSection(string title, string path, SettingField[] fields, Action<JObject> applied, Action<string> log,
                                 string note = null)
        {
            _title = title; _path = path; _fields = fields ?? new SettingField[0]; _applied = applied; _log = log; _note = note;
            _values = new double[_fields.Length];
            _saved = new double[_fields.Length];
            for (int i = 0; i < _fields.Length; i++) _values[i] = _saved[i] = _fields[i].Default;
            int n = _fields.Length;
            _held = new double[n]; _check = new double[n]; _shown = new bool[n]; _checkShown = new bool[n]; _labels = new string[n];
            _view = new SettingValues(_fields, _held);
            _checkView = new SettingValues(_fields, _check);
            for (int i = 0; i < n; i++)
            {
                _held[i] = _values[i];
                _shown[i] = true;
                if (_fields[i].Extra) _extras = true;
            }
            ModsOrder.Made(_title);   // the mod's name in the Mods tab's order, if it is the first section made
            // Read now, while the mod has just read the same file, so a later change on disk is known to be one it
            // hasn't got.
            try { Refresh(); }
            catch (Exception e) { LogOnce("read", _title + ": couldn't read settings.json for the settings section: " + PathClean.CleanText(e.Message)); }
        }

        // Draws the section into a panel. collapsible: a header that opens and closes (the Preferences Mods tab);
        // otherwise
        // the rows straight in (a mod's own Settings tab).
        internal void Build(UIPanelBuilder b, bool collapsible)
        {
            try
            {
                if (collapsible)
                {
                    // The search (ModsSearch): the box first if this copy draws the tab, then this section as the
                    // query has it. No query: drawn as always.
                    string q = null;
                    try { q = ModsSearch.Begin(this, _title, SearchShows); }
                    catch (Exception e) { LogOnce("search", _title + ": the search failed: " + PathClean.CleanText(e.Message)); }
                    if (SearchState(q)) b.AddExpandable(_title, q == null ? _expanded : _searchOpen, Content, ExpandableStyle.SectionHeader);
                }
                else { _search = null; Content(b); }
            }
            catch (Exception e)
            {
                LogOnce("build", _title + ": couldn't draw the settings: " + PathClean.CleanText(e.ToString(), WholeCap));
                try { b.AddLabel("<color=#E8A33A>" + _title + ": couldn't show the settings. The log says why.</color>"); } catch { }
            }
        }

        // The rows a search shows: null when the mod's name matches (every row), else one flag per field: a row whose
        // label matches, every row under a heading that matches, and a heading with any row under it shown. All false:
        // nothing here matches.
        internal bool[] SearchRows(string q)
        {
            if (ModsSearch.Matches(_title, q)) return null;
            var show = new bool[_fields.Length];
            int heading = -1;
            for (int i = 0; i < _fields.Length; i++)
            {
                if (_fields[i].Kind == SettingKind.Heading) { heading = i; show[i] = ModsSearch.Matches(_fields[i].Label, q); continue; }
                show[i] = ModsSearch.Matches(_fields[i].Label, q) || heading >= 0 && ModsSearch.Matches(_fields[heading].Label, q);
                if (show[i] && heading >= 0) show[heading] = true;
            }
            return show;
        }

        // Whether the section draws for query q (null: no search), setting the rows it draws. A new query opens it
        // (_searchOpen), and no query forgets the last one, so the same search opens it again next time.
        internal bool SearchState(string q)
        {
            if (q == null) { _search = null; _searchFor = null; return true; }
            if (!SearchShows(q)) { _search = null; return false; }
            _search = SearchRows(q);
            if (q != _searchFor) { _searchFor = q; _searchOpen.Value = true; }
            return true;
        }

        // Whether a search for q shows this section: its name matches, or a row it would draw and ShowWhen shows does.
        // A row ShowWhen hides is drawn hidden, so it can appear as values change, but it doesn't count here.
        internal bool SearchShows(string q)
        {
            bool[] rows = SearchRows(q);
            if (rows == null) return true;
            if (_state == FileState.Unreadable) return false;   // it draws no rows
            for (int i = 0; i < rows.Length; i++)
                if (rows[i] && _fields[i].Kind != SettingKind.Heading && _shown[i]) return true;
            return false;
        }

        private bool Dirty
        {
            get { for (int i = 0; i < _values.Length; i++) if (_values[i] != _saved[i]) return true; return false; }
        }

        private void Content(UIPanelBuilder b)
        {
            _statusText = null;
            if (_extras)
            {
                _rows = new RectTransform[_fields.Length];
                _inputs = new TMP_InputField[_fields.Length];
                _valueTexts = new TMP_Text[_fields.Length];
            }
            if (!_redraw) _status = "";   // opened afresh, not redrawn after one of its own clicks
            _redraw = false;
            Refresh();
            Recompute();   // after an Undo or Reset too
            if (!string.IsNullOrEmpty(_note)) b.AddLabel(Footnote(_note));
            if (_state == FileState.Unreadable)
            {
                BuildUnreadable(b);
                if (_status != "") b.AddLabel(Footnote(_status));
                return;
            }

            for (int i = 0; i < _fields.Length; i++)
            {
                int k = i;
                SettingField f = _fields[k];
                if (_search != null && !_search[k]) continue;   // hidden by the search: not drawn, value kept
                if (f.Extra) { ExtraRow(b, k); continue; }
                IConfigurableElement row = f.Kind == SettingKind.Bool ? BoolRow(b, k)
                                         : f.Kind == SettingKind.Choice ? ChoiceRow(b, k)
                                         : f.Slider ? SliderRow(b, k)
                                         : NumberRow(b, k);
                row.Tooltip(f.Label, HelpText(f));
            }

            if (_confirmReset)
            {
                b.AddLabel("Put every setting here back to its default and save?");
                b.HStack(row =>
                {
                    row.AddButtonCompact("Yes, reset", () => Guard("reset", () =>
                    {
                        _confirmReset = false;
                        for (int i = 0; i < _fields.Length; i++) _values[i] = _fields[i].Default;
                        bool restart = ChangesNeedRestart();
                        if (SaveFile(out string error, out bool applied))
                            _status = !applied ? NotApplied
                                : restart ? "Back to the defaults. Takes effect after a restart." : "Back to the defaults. Takes effect now.";
                        else _status = error;
                        _editNote = null;
                        Redraw(b);
                    }));
                    row.Spacer(6f);
                    row.AddButtonCompact("Cancel", () => Guard("cancel", () => { _confirmReset = false; _status = ""; Redraw(b); }));
                });
            }
            else
            {
                b.HStack(row =>
                {
                    row.AddButtonCompact("Save", () => Guard("save", () =>
                    {
                        bool restart = ChangesNeedRestart(), changed = Dirty || _behind != null || HeldMoved();
                        if (SaveFile(out string error, out bool applied))
                            _status = !applied ? NotApplied
                                : !changed ? (_updated ? Updated : "Saved. Nothing had changed.")
                                : restart ? "Saved. Takes effect after a restart." : "Saved. Takes effect now.";
                        else _status = error;
                        _status = WithNote(_status);
                        Redraw(b);
                    }));
                    row.Spacer(6f);
                    row.AddButtonCompact("Undo changes", () => Guard("undo", () =>
                    {
                        _status = Dirty ? "Back to what's saved." : "Nothing to undo.";
                        Array.Copy(_saved, _values, _values.Length);
                        Recompute();
                        _editNote = null;
                        Redraw(b);
                    }));
                    row.Spacer(6f);
                    row.AddButtonCompact("Reset to defaults", () => Guard("ask", () => { _confirmReset = true; _editNote = null; Redraw(b); }));
                });
            }
            _statusText = b.AddLabel(Footnote(StatusShown())).GetComponent<TMP_Text>();
        }

        private const string NotApplied = "Saved. Takes effect after a restart; the log says why.";
        private const string Updated = "Saved. No setting changed; settings.json was updated.";

        // What the status line says: the last action, else unsaved edits, else why the mod isn't running what's shown.
        private string StatusShown() => _status != "" ? _status : Dirty ? "Not saved yet." : _behind ?? "";

        private string WithNote(string s)
        {
            string note = _editNote;
            _editNote = null;
            return string.IsNullOrEmpty(note) ? s : s + " " + note;
        }

        // After a click: ask the game to rebuild the panel once the click is over (as the game's own Preferences
        // sections
        // redraw), so the clicked button isn't destroyed inside its own handler. Rebuild at once only if none can be
        // queued.
        private void Redraw(UIPanelBuilder b)
        {
            _redraw = true;
            try { b.SetNeedsRebuild(); }
            catch (Exception)
            {
                try { b.Rebuild(); }
                catch (Exception e) { _redraw = false; LogOnce("redraw", _title + ": couldn't redraw the settings: " + PathClean.CleanText(e.Message)); }
            }
        }

        private IConfigurableElement BoolRow(UIPanelBuilder b, int k)
        {
            SettingField f = _fields[k];
            return Field(b, f, b.HStack(row =>
            {
                row.AddToggle(() => _values[k] != 0, on => Guard("toggle", () => { _values[k] = on ? 1 : 0; _editNote = null; Edited(""); }));
                row.Spacer(8f);
                row.AddLabel(Dim("default " + (f.Default != 0 ? "on" : "off")));
                if (!f.Live) { row.Spacer(6f); row.AddLabel(RestartTag); }
            }));
        }

        // A dropdown shows its own pick, so a choice updates the status line in place, like a toggle, with no rebuild.
        // The
        // game's dropdown has already set its value before it calls back.
        private IConfigurableElement ChoiceRow(UIPanelBuilder b, int k)
        {
            SettingField f = _fields[k];
            return Field(b, f, b.HStack(row =>
            {
                int at = (int)Math.Max(0, Math.Min(f.Options.Length - 1, Math.Round(_values[k])));
                row.AddDropdown(new List<string>(f.Options), at, i => Guard("choose", () =>
                {
                    if (i < 0 || i >= f.Options.Length) return;
                    _values[k] = i;
                    _editNote = null;
                    Edited("");
                })).Width(140f);
                row.Spacer(8f);
                row.AddLabel(Dim("default " + f.OptionAt(f.Default)));
                if (!f.Live) { row.Spacer(6f); row.AddLabel(RestartTag); }
            }));
        }

        // A field row with the game's own label, kept at one size: the game's field-row Label autosizes from 8 to 12
        // and never wraps, so a long label would come out smaller than the rest. Here it stays at its largest size and
        // wraps. The game's row is 30 px (a LayoutElement minHeight on the row, the label a fixed 30 px tall because
        // the row's layout group does not control child height), which holds one line of 14.6 px but not two lines
        // with room to spare. A label that wraps makes its row taller: the row and the label go to LabelRowHeight and
        // the label's text box (the rect less LabelPadBottom, split top and bottom) holds two lines (29.3 px) but not a
        // third (43.9 px), so a third line's worth of text ends in "..." on the second. The game's label aligns its first
        // line's capline to the middle (prefab vertical alignment Capline), which leaves a second line hanging below; a
        // wrapped label is set to Middle so both lines sit centered on the row. The label is measured with the
        // engine's own text measure at a width a little under the label's 150, so a borderline label gets the tall row.
        internal const float LabelRowHeight = 44f, LabelPadBottom = 8f, LabelMeasureWidth = 146f, LabelLineHeightPt = 109.71f / 90f;

        // Whether a label of this preferred height (measured at 12 pt) needs more than one line.
        internal static bool LabelWraps(float preferredHeight, float fontSize) => preferredHeight > fontSize * LabelLineHeightPt * 1.5f;

        private static IConfigurableElement Field(UIPanelBuilder b, SettingField f, RectTransform control)
        {
            IConfigurableElement e = b.AddField(f.Label, control);
            try
            {
                Transform t = e.RectTransform.Find("Label");
                TMP_Text label = t != null ? t.GetComponent<TMP_Text>() : null;
                if (label != null)
                {
                    float size = label.fontSizeMax;
                    label.enableAutoSizing = false;
                    label.fontSize = size;
                    label.textWrappingMode = TextWrappingModes.Normal;
                    label.overflowMode = TextOverflowModes.Ellipsis;
                    bool wraps;
                    // Measured as drawn: the game's label style is all caps (FontStyles.UpperCase), wider
                    // than the text as typed, so a borderline label measured as typed fits one line and
                    // then wraps without its tall row.
                    string drawn = (label.fontStyle & FontStyles.UpperCase) != 0 ? label.text.ToUpperInvariant() : label.text;
                    try { wraps = LabelWraps(label.GetPreferredValues(drawn, LabelMeasureWidth, 0f).y, size); }
                    catch (Exception) { wraps = f.Label != null && f.Label.Length > 16; }   // can't measure: be safe, give it room
                    if (wraps)
                    {
                        label.margin = new Vector4(0f, LabelPadBottom / 2f, 0f, LabelPadBottom / 2f);
                        label.verticalAlignment = VerticalAlignmentOptions.Middle;   // both lines centered
                        label.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, LabelRowHeight);
                        // The game's field row lays its parts out from the top (prefab child alignment UpperLeft), so
                        // in a taller row the control would stay at the top while the two lines sit centered below
                        // it: center the row's parts so the control sits level with the middle of the two lines.
                        UnityEngine.UI.LayoutGroup rowLayout = e.RectTransform.GetComponent<UnityEngine.UI.LayoutGroup>();
                        if (rowLayout != null) rowLayout.childAlignment = TextAnchor.MiddleLeft;
                        UnityEngine.UI.LayoutElement rowFit = e.RectTransform.GetComponent<UnityEngine.UI.LayoutElement>();
                        if (rowFit != null) rowFit.minHeight = Math.Max(rowFit.minHeight, LabelRowHeight);
                    }
                }
            }
            catch (Exception) { }
            return e;
        }

        // A slider row: the left text, the slider, the right text, the value's text. The slider counts stops (0 to
        // SliderStops) and the field's value is Min + stop * Step, so Min and Max are reachable whatever the step.
        private IConfigurableElement SliderRow(UIPanelBuilder b, int k)
        {
            SettingField f = _fields[k];
            return Field(b, f, b.HStack(row =>
            {
                if (!string.IsNullOrEmpty(f.LeftLabel)) row.AddLabel(EndText(f.LeftLabel)).VerticalTextAlignment(VerticalAlignmentOptions.Middle);
                RectTransform slider = row.AddSlider(() => (float)SliderStopOf(f, _values[k]), () => Format(f, _values[k]),
                    stop => Guard("slide", () => { _values[k] = SliderValueAt(f, stop); _editNote = null; Edited(""); }),
                    0f, (float)SliderStops(f), true).EqualFlexibleWidth(120f);
                UnityEngine.UI.LayoutElement fit = slider.GetComponent<UnityEngine.UI.LayoutElement>();
                if (fit != null) fit.minWidth = 100f;   // the end texts can't squeeze the slider out
                if (!string.IsNullOrEmpty(f.RightLabel)) row.AddLabel(EndText(f.RightLabel)).VerticalTextAlignment(VerticalAlignmentOptions.Middle);
                if (!f.Live) { row.Spacer(6f); row.AddLabel(RestartTag); }
            }));
        }

        // A slider's end text: no tags, and at most 14 characters (longer is cut and ends in "...").
        internal static string EndText(string s)
        {
            s = Plain(s);
            if (s.Length <= 14) return s;
            int n = char.IsHighSurrogate(s[12]) ? 12 : 13;
            return s.Substring(0, n).TrimEnd() + "...";
        }

        // The number of steps from Min to Max (the last one may be short, so Max is always a stop).
        internal static int SliderStops(SettingField f) => Math.Max(1, (int)Math.Ceiling((f.Max - f.Min) / f.Step - 1e-9));

        // The value at a slider stop: Min + stop * Step, rounded like the field, never past Max.
        internal static double SliderValueAt(SettingField f, double stop) =>
            Math.Min(f.Max, Math.Max(f.Min, Round(f, f.Min + Math.Round(stop, MidpointRounding.AwayFromZero) * f.Step)));

        // The stop nearest a value, by value (a value from a hand-edited file, between stops, shows at the nearest one
        // and is kept until the slider is moved). A tie goes to the higher stop; the short last step counts as it is.
        internal static double SliderStopOf(SettingField f, double v)
        {
            int last = SliderStops(f);
            double lo = Math.Max(0, Math.Min(last, Math.Floor((v - f.Min) / f.Step))), hi = Math.Min(last, lo + 1);
            return Math.Abs(SliderValueAt(f, hi) - v) <= Math.Abs(v - SliderValueAt(f, lo)) ? hi : lo;
        }

        private IConfigurableElement NumberRow(UIPanelBuilder b, int k)
        {
            SettingField f = _fields[k];
            TMP_InputField input = null;
            return Field(b, f, b.HStack(row =>
            {
                row.AddButtonCompact("-", () => Guard("step", () => Step(k, -1, input)));
                RectTransform rt = row.AddInputField(Format(f, _values[k]), text => Guard("type", () => Typed(k, text, input)), null, 12);
                rt.Width(72f);
                input = rt.GetComponent<TMP_InputField>();
                row.AddButtonCompact("+", () => Guard("step", () => Step(k, +1, input)));
                row.Spacer(8f);
                row.AddLabel(Dim("default " + Format(f, f.Default)));
                if (!f.Live) { row.Spacer(6f); row.AddLabel(RestartTag); }
            }));
        }

        // A field with an option, or a Color or a Heading. Every other field takes the plain rows above. Each row is
        // kept so ShowWhen can show or hide it in place.
        private void ExtraRow(UIPanelBuilder b, int k)
        {
            SettingField f = _fields[k];
            RectTransform shape;
            if (f.Kind == SettingKind.Heading) shape = b.AddLabel("<style=SectionHeader>" + f.Label + "</style>");
            else
            {
                IConfigurableElement row = f.Kind == SettingKind.Bool ? BoolRow(b, k)
                                         : f.Kind == SettingKind.Choice ? ChoiceRow(b, k)
                                         : f.Kind == SettingKind.Color ? ColorRow(b, k)
                                         : f.Slider ? SliderRowExtra(b, k)
                                         : NumberRowExtra(b, k);
                row.Tooltip(f.Label, HelpText(f));
                shape = row.RectTransform;
            }
            _rows[k] = shape;
            if (!_shown[k]) shape.gameObject.SetActive(false);
        }

        // NumberRow showing the held value, its box kept so a value held by another field's change is shown in place,
        // and the value label after +.
        private IConfigurableElement NumberRowExtra(UIPanelBuilder b, int k)
        {
            SettingField f = _fields[k];
            TMP_InputField input = null;
            return Field(b, f, b.HStack(row =>
            {
                row.AddButtonCompact("-", () => Guard("step", () => Step(k, -1, input)));
                RectTransform rt = row.AddInputField(Format(f, _held[k]), text => Guard("type", () => Typed(k, text, input)), null, 12);
                rt.Width(72f);
                input = rt.GetComponent<TMP_InputField>();
                _inputs[k] = input;
                row.AddButtonCompact("+", () => Guard("step", () => Step(k, +1, input)));
                if (f.ValueLabelOf != null) AddValueText(row, k);
                row.Spacer(8f);
                row.AddLabel(Dim("default " + Format(f, f.Default)));
                if (!f.Live) { row.Spacer(6f); row.AddLabel(RestartTag); }
            }));
        }

        // SliderRow showing the held value (the game polls the slider's value, so a value held by another field's
        // change moves the handle too), a drag held in range, and the value label after the right end text.
        private IConfigurableElement SliderRowExtra(UIPanelBuilder b, int k)
        {
            SettingField f = _fields[k];
            return Field(b, f, b.HStack(row =>
            {
                if (!string.IsNullOrEmpty(f.LeftLabel)) row.AddLabel(EndText(f.LeftLabel)).VerticalTextAlignment(VerticalAlignmentOptions.Middle);
                RectTransform slider = row.AddSlider(() => (float)SliderStopOf(f, _held[k]), () => Format(f, _held[k]),
                    stop => Guard("slide", () => { _values[k] = HoldIn(k, SliderValueAt(f, stop), _view); _editNote = null; Edited(""); }),
                    0f, (float)SliderStops(f), true).EqualFlexibleWidth(120f);
                UnityEngine.UI.LayoutElement fit = slider.GetComponent<UnityEngine.UI.LayoutElement>();
                if (fit != null) fit.minWidth = 100f;   // the end texts can't squeeze the slider out
                if (!string.IsNullOrEmpty(f.RightLabel)) row.AddLabel(EndText(f.RightLabel)).VerticalTextAlignment(VerticalAlignmentOptions.Middle);
                if (f.ValueLabelOf != null) AddValueText(row, k);
                if (!f.Live) { row.Spacer(6f); row.AddLabel(RestartTag); }
            }));
        }

        // A value label: one line in a fixed width, so a slider beside it keeps its size as the text changes; a longer
        // text ends in "...".
        internal const float ValueTextWidth = 100f;

        private void AddValueText(UIPanelBuilder row, int k)
        {
            row.Spacer(6f);
            RectTransform rt = row.AddLabel(_labels[k] ?? "").Width(ValueTextWidth);
            TMP_Text text = rt.GetComponent<TMP_Text>();
            _valueTexts[k] = text;
            try
            {
                text.textWrappingMode = TextWrappingModes.NoWrap;
                text.overflowMode = TextOverflowModes.Ellipsis;
                text.verticalAlignment = VerticalAlignmentOptions.Middle;
            }
            catch (Exception) { }
        }

        // A color: the game's color dropdown (a swatch that opens a picker, a hex box and Default and Apply buttons),
        // and the default, dimmed. With no dropdown in the game, a hex box instead.
        private IConfigurableElement ColorRow(UIPanelBuilder b, int k)
        {
            SettingField f = _fields[k];
            return Field(b, f, b.HStack(row =>
            {
                string hex = SettingField.ColorHex((int)_values[k]);
                RectTransform pick = null;
                try { pick = Picker(row, hex, text => Guard("color", () => Colored(k, text, b))); }
                catch (Exception e) { LogOnce("picker", _title + ": no color dropdown, so a hex box: " + PathClean.CleanText(e.Message)); }
                if (pick != null) pick.Width(120f);
                else row.AddInputField(hex, text => Guard("color", () => Colored(k, text, b)), null, 7).Width(72f);
                row.Spacer(8f);
                row.AddLabel(Dim("default " + SettingField.ColorHex((int)f.Default)));
                if (!f.Live) { row.Spacer(6f); row.AddLabel(RestartTag); }
            }));
        }

        // The dropdown in a method of its own, so a game without it fails at this call, inside the try above (Mono
        // resolves a method when it compiles its caller, before the caller's own try runs), and gets the hex box.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static RectTransform Picker(UIPanelBuilder row, string hex, Action<string> onApply) => row.AddColorDropdown(hex, onApply);

        // What the color dropdown hands back: a picked color as it is dragged, the box's text on Apply (anything the
        // player typed), or null from its Default button. Text that isn't a color keeps the value, and the panel is
        // redrawn so the swatch shows the kept color again; Default redraws too, as the dropdown then shows no swatch,
        // and so does a color typed in any other form than #RRGGBB (" #abc "), so the swatch shows it as saved.
        // A dragged color comes as #RRGGBB and never redraws.
        private void Colored(int k, string text, UIPanelBuilder b)
        {
            SettingField f = _fields[k];
            int rgb;
            if (text == null) rgb = (int)f.Default;
            else if (!SettingField.TryColor(text, out rgb))
            {
                Note("That isn't a color like #RRGGBB, so " + f.Label + " stays at " + SettingField.ColorHex((int)_values[k]) + ".");
                Redraw(b);
                return;
            }
            _values[k] = rgb;
            _editNote = null;
            Edited("");
            if (text == null || text != SettingField.ColorHex(rgb)) Redraw(b);
        }

        // The buttons move to the next whole step (125 -> 150 -> 175), then stay inside min and max (or the field's
        // Limits).
        private void Step(int k, int dir, TMP_InputField input)
        {
            SettingField f = _fields[k];
            double cur = _held[k], n = cur / f.Step;
            double next = dir > 0 ? (Math.Floor(n + 1e-9) + 1) * f.Step : (Math.Ceiling(n - 1e-9) - 1) * f.Step;
            Range(k, _view, out double lo, out double hi);
            Set(k, Math.Max(lo, Math.Min(hi, Round(f, next))), input);
            _editNote = null;
            Edited("");
        }

        // Applied on losing focus or on Enter, never with a rebuild: a click on Save that took the focus must still
        // land on
        // Save. The field always ends up showing the value that will be saved.
        private void Typed(int k, string text, TMP_InputField input)
        {
            SettingField f = _fields[k];
            double before = _held[k];
            TypedNumber got = ParseTyped(text, f.Places, out double v);
            if (got != TypedNumber.Ok)
            {
                Set(k, _values[k], input);   // unchanged; a held value is shown again by Edited
                Note(got == TypedNumber.NotANumber
                    ? "That isn't a number, so " + f.Label + " stays at " + Format(f, before) + "."
                    : "Can't tell what " + Plain(text) + " means, so " + f.Label + " stays at " + Format(f, before) + ". Type it like "
                      + (f.Places == 0 ? "1 or 1000." : "1.5 or 1000."));
                return;
            }
            double r = Round(f, v);
            if (f.Linked && r == _held[k] && _values[k] != _held[k])
            {
                // The held number as shown, handed back when the box loses focus: no edit, so the number the player
                // set still comes back if the range opens again.
                if (input != null) input.SetTextWithoutNotify(Format(f, _held[k]));
                return;
            }
            Range(k, _view, out double lo, out double hi);
            double c = Math.Max(lo, Math.Min(hi, r));
            Set(k, c, input);
            if (c != r) Note(f.Label + " goes from " + Format(f, lo) + " to " + Format(f, hi) + ", so it's " + Format(f, c) + ".");
            else if (r != v && RoundingMatters(v, r)) Note(f.Label + " takes " + PlacesWord(f) + ", so " + Plain(v) + " is " + Format(f, c) + ".");
            else
            {
                _editNote = null;
                Edited(c != before && Dirty ? f.Label + ": " + Format(f, c) + ". Not saved yet." : "");
            }
        }

        // Rounding that matters: it turns a value to or from zero (often on or off), or moves it by over a hundredth of
        // itself.
        private static bool RoundingMatters(double v, double r) => (r == 0) != (v == 0) || Math.Abs(r - v) > 0.01 * Math.Abs(v);

        private static string PlacesWord(SettingField f) =>
            f.Places == 0 ? "whole numbers" : f.Places == 1 ? "one decimal place" : f.Places + " decimal places";

        // Reads a typed number. Returns Ok, or why not.
        //   - Spaces between digits are dropped (no-break and thin spaces too).
        //   - Commas are thousands separators when each starts a group of exactly three digits, after a first group of
        //     1 to 3 digits that isn't 0 ("1,000", "12,500,000", "1,000.5").
        //   - Otherwise a single comma not followed by exactly three digits is a decimal comma ("12,5", "0,04"), in a
        //     field that takes decimals only.
        //   - A dot is the decimal point, except a single dot followed by exactly three digits after a group like that,
        //     in a field showing fewer than three decimals: it could be a European thousands dot ("1.000"), so it's
        //     refused.
        //   - Also refused: several dots, a dot and a comma in any other arrangement, a comma in a number with an
        //     exponent.
        internal static TypedNumber ParseTyped(string text, int places, out double value)
        {
            value = 0;
            var sb = new StringBuilder();
            foreach (char ch in (text ?? "").Trim())
                if (!char.IsWhiteSpace(ch) && ch != ' ' && ch != ' ' && ch != ' ') sb.Append(ch);
            string s = sb.ToString();
            if (s.Length == 0) return TypedNumber.NotANumber;
            int start = s[0] == '-' || s[0] == '+' ? 1 : 0;
            string sign = s.Substring(0, start), body = s.Substring(start);
            int commas = 0, dots = 0;
            bool other = false;
            foreach (char ch in body)
            {
                if (ch == ',') commas++;
                else if (ch == '.') dots++;
                else if (ch < '0' || ch > '9') other = true;
            }
            string plain;
            if (other)
            {
                if (commas > 0) return TypedNumber.Ambiguous;   // "1,5e3" and the like
                plain = body;
            }
            else if (commas > 0 && dots > 0)
            {
                int dot = body.IndexOf('.');
                if (dots > 1 || body.LastIndexOf(',') > dot || !Grouped(body.Substring(0, dot))) return TypedNumber.Ambiguous;
                plain = body.Replace(",", "");
            }
            else if (commas > 0)
            {
                if (Grouped(body)) plain = body.Replace(",", "");
                else if (commas == 1 && places > 0 && body.Length - body.IndexOf(',') - 1 != 3) plain = body.Replace(',', '.');
                else return TypedNumber.Ambiguous;
            }
            else if (dots > 1) return TypedNumber.Ambiguous;
            else
            {
                int dot = body.IndexOf('.');
                if (dot >= 0 && places < 3 && body.Length - dot - 1 == 3 && Grouped(body.Substring(0, dot) + "," + body.Substring(dot + 1)))
                    return TypedNumber.Ambiguous;
                plain = body;
            }
            if (!double.TryParse(sign + plain, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
                || double.IsNaN(v) || double.IsInfinity(v))
                return TypedNumber.NotANumber;
            value = v;
            return TypedNumber.Ok;
        }

        // Digits in thousands groups: "1,000", "12,500,000" (a first group of 1 to 3 digits, not 0 or led by 0).
        private static bool Grouped(string s)
        {
            string[] g = s.Split(',');
            if (g.Length < 2 || g[0].Length < 1 || g[0].Length > 3 || g[0][0] == '0') return false;
            foreach (string part in g)
                foreach (char ch in part) if (ch < '0' || ch > '9') return false;
            for (int i = 1; i < g.Length; i++) if (g[i].Length != 3) return false;
            return true;
        }

        private void Set(int k, double v, TMP_InputField input)
        {
            _values[k] = v;
            if (input != null) input.text = Format(_fields[k], v);
        }

        private void Note(string message)
        {
            _editNote = message;
            Edited(message);
        }

        private void Edited(string message)
        {
            Recompute();
            ShowHeld();
            SetStatus(message);
        }

        // Works out what the rows show from the values as set: the held values, which rows show, and the value labels.
        // On load and after every edit, never per frame.
        private void Recompute()
        {
            Settle(_values, _held, _shown, _view);
            if (!_extras) return;
            for (int i = 0; i < _fields.Length; i++)
                if (_fields[i].ValueLabelOf != null) _labels[i] = LabelFor(i);
        }

        // Fills held from raw: each shown field with Limits held in its range, in field order from the held values so
        // far, every other value as it is; shown gets which rows ShowWhen shows. Repeated until nothing moves. With no
        // loop among the fields' Limits and ShowWhen that takes one pass per field at most, plus one that moves
        // nothing. A loop may never settle: it stops after that many passes, leaving what the last pass left (a limit
        // can be broken), and is logged once.
        private void Settle(double[] raw, double[] held, bool[] shown, SettingValues view)
        {
            Array.Copy(raw, held, raw.Length);
            if (!_extras) return;
            for (int i = 0; i < shown.Length; i++) shown[i] = true;
            for (int pass = 0; pass <= _fields.Length; pass++)
            {
                bool moved = false;
                for (int i = 0; i < _fields.Length; i++)
                {
                    SettingField f = _fields[i];
                    if (f.ShowIf != null)
                    {
                        bool show = Shows(f, view);
                        if (show != shown[i]) { shown[i] = show; moved = true; }
                    }
                    double h = shown[i] && f.Linked ? HoldIn(i, raw[i], view) : raw[i];
                    if (h != held[i]) { held[i] = h; moved = true; }
                }
                if (!moved) return;
            }
            LogOnce("settle", _title + ": its Limits and ShowWhen depend on each other in a loop that doesn't settle.");
        }

        // The range a field's value is held in now: Min to Max, narrowed by its Limits. A limit is kept inside Min to
        // Max and rounded inward to the field's decimals (a whole-number field's max of 7.5 holds at 7). A lower limit
        // above the upper one wins, as Min does over Max.
        private void Range(int k, SettingValues view, out double lo, out double hi)
        {
            SettingField f = _fields[k];
            lo = f.Min;
            hi = f.Max;
            if (!f.Linked) return;
            double p = Math.Pow(10, f.Places);
            if (f.MinOf != null && Limit(f, f.MinOf, view, out double m))
                lo = Math.Max(f.Min, Math.Min(f.Max, Math.Ceiling(m * p - 1e-6) / p));
            if (f.MaxOf != null && Limit(f, f.MaxOf, view, out double x))
                hi = Math.Min(f.Max, Math.Max(f.Min, Math.Floor(x * p + 1e-6) / p));
            if (lo > hi) hi = lo;
        }

        private double HoldIn(int k, double v, SettingValues view)
        {
            Range(k, view, out double lo, out double hi);
            return Math.Max(lo, Math.Min(hi, Round(_fields[k], v)));
        }

        // A limit's value; false (logged once) when it throws or isn't a finite number, so that limit is left out.
        private bool Limit(SettingField f, Func<SettingValues, double> limit, SettingValues view, out double v)
        {
            try { v = limit(view); }
            catch (Exception e)
            {
                v = 0;
                LogOnce("limit:" + f.Key, _title + ": a limit of " + f.Key + " failed: " + PathClean.CleanText(e.Message));
                return false;
            }
            return !double.IsNaN(v) && !double.IsInfinity(v);
        }

        private bool Shows(SettingField f, SettingValues view)
        {
            try { return f.ShowIf(view); }
            catch (Exception e)
            {
                LogOnce("show:" + (f.Key ?? f.Label), _title + ": ShowWhen of " + (f.Key ?? f.Label) + " failed: " + PathClean.CleanText(e.Message));
                return true;
            }
        }

        // A value label's text for the held values: no tags, one line.
        private string LabelFor(int k)
        {
            SettingField f = _fields[k];
            try { return Plain(f.ValueLabelOf(_held[k], _view)).Replace('\r', ' ').Replace('\n', ' '); }
            catch (Exception e)
            {
                LogOnce("label:" + f.Key, _title + ": the value label of " + f.Key + " failed: " + PathClean.CleanText(e.Message));
                return "";
            }
        }

        // After an edit, in place on the last build: rows shown or hidden, number boxes and value labels set to the
        // held values.
        private void ShowHeld()
        {
            if (!_extras || _rows == null) return;
            try
            {
                for (int i = 0; i < _fields.Length; i++)
                {
                    if (_rows[i] != null && _rows[i].gameObject.activeSelf != _shown[i]) _rows[i].gameObject.SetActive(_shown[i]);
                    if (_inputs[i] != null)
                    {
                        string s = Format(_fields[i], _held[i]);
                        if (_inputs[i].text != s) _inputs[i].SetTextWithoutNotify(s);
                    }
                    if (_valueTexts[i] != null) _valueTexts[i].text = _labels[i] ?? "";
                }
            }
            catch (Exception e) { LogOnce("rows", _title + ": couldn't update the rows: " + PathClean.CleanText(e.Message)); }
        }

        // A held value differs from the file's (a number held at load), or is one Save can't write as it is
        // (infinity): saving changes the file.
        private bool HeldMoved()
        {
            for (int i = 0; i < _held.Length; i++) if (_held[i] != _saved[i] || Writable(_fields[i], _held[i]) != _held[i]) return true;
            return false;
        }

        // settings.json as it is on disk now, read as the mod's own read gets it (ReadValues), each value held in its
        // range, over the file's other keys. Unsaved edits are never in it, and it changes nothing here (no read
        // state, no status). Null when settings.json can't be read. A value that makes the mod's own read fail means
        // it starts on its defaults (the status line says so): then this is every field's default, held, and none of
        // the file's other keys. A mod with Limits reads its settings from this at load (USE 5), so it runs the held
        // values.
        internal JObject Current()
        {
            try
            {
                JObject disk = ReadFile(out FileState state, out string problem);
                if (state == FileState.Unreadable) return null;
                int n = _fields.Length;
                double[] raw = ReadValues(disk, out string odd, out bool oddChoice), held = new double[n];
                bool defaults = odd != null && !oddChoice;   // a bad toggle or number: the mod's read fails
                if (defaults) for (int i = 0; i < n; i++) raw[i] = _fields[i].Default;
                Settle(raw, held, new bool[n], new SettingValues(_fields, held));
                return WithValues(defaults ? new JObject() : disk ?? new JObject(), held);
            }
            catch (Exception e)
            {
                LogOnce("current", _title + ": couldn't read the settings: " + PathClean.CleanText(e.Message));
                return null;
            }
        }

        // Writes values into o, every field but a heading, each under its own key: the same key in other capitals goes,
        // as the mod's own read would take it in place of this one.
        private JObject WithValues(JObject o, double[] values)
        {
            foreach (JProperty p in new List<JProperty>(o.Properties()))
            {
                int i = Target(p.Name);
                if (i >= 0 && p.Name != _fields[i].Key) p.Remove();
            }
            for (int i = 0; i < _fields.Length; i++)
                if (_fields[i].Kind != SettingKind.Heading) o[_fields[i].Key] = Token(_fields[i], values[i]);
            return o;
        }

        private void SetStatus(string s)
        {
            _status = s ?? "";
            if (_statusText != null) _statusText.text = Footnote(StatusShown());
        }

        // True when a change about to be saved is to a field the mod reads only at load (or the mod may not be running
        // what's saved, and some field is read only at load). Held values on both sides: what will be saved, and what
        // the mod runs from the file.
        private bool ChangesNeedRestart()
        {
            Recompute();
            Settle(_saved, _check, _checkShown, _checkView);
            for (int i = 0; i < _fields.Length; i++)
                if (!_fields[i].Live && (Writable(_fields[i], _held[i]) != _check[i] || _behind != null)) return true;
            return false;
        }

        private void BuildUnreadable(UIPanelBuilder b)
        {
            b.AddLabel("<color=#E8A33A>settings.json can't be read, so it's left alone: <noparse>" + PathClean.ForScreen(_problem ?? "unknown") + "</noparse></color>");
            b.AddLabel(Footnote("Fix or delete it, then press Check again."));
            b.HStack(row =>
            {
                row.AddButtonCompact(_showPath ? "Hide where it is" : "Show where it is", () => Guard("path", () => { _showPath = !_showPath; Redraw(b); }));
                row.Spacer(6f);
                row.AddButtonCompact("Check again", () => Guard("recheck", () => { _loaded = false; Redraw(b); }));
            });
            if (_showPath) b.AddInputFieldReadOnly(_path);
        }

        // Re-reads the file if it changed on disk since the last read. Unsaved edits win: Save merges them onto what's
        // on disk
        // by then. A change made while the game runs isn't what the mod has (it keeps what it read until a save here or
        // a restart), and the status line says so.
        private void Refresh()
        {
            if (_loaded && Dirty) return;
            string stamp = Stamp();
            if (_loaded && stamp == _stamp) return;
            bool first = _stamp == null;
            _loaded = true;
            _stamp = stamp;
            JObject obj = ReadFile(out _state, out _problem);
            double[] read = ReadValues(obj, out string odd, out _);
            bool moved = false;
            for (int i = 0; i < _fields.Length; i++)
            {
                moved |= read[i] != _saved[i];
                _values[i] = _saved[i] = read[i];
            }
            if (!first && _state != FileState.Unreadable && (moved || _behind != null))
                _behind = "settings.json changed outside the game. Save or restart to use it.";
            if (odd != null) _behind = odd;
            Recompute();   // a value outside its Limits is shown held; the file keeps it until a save
        }

        private string Stamp()
        {
            try
            {
                var fi = new FileInfo(_path);
                return fi.Exists ? fi.Length + ":" + fi.LastWriteTimeUtc.Ticks : "missing";
            }
            catch (Exception e) { return "error:" + e.Message; }
        }

        // The file's value after any comments before it, as JsonConvert reads it; null when there is only comment.
        private static JToken AfterComments(string text)
        {
            using (var r = new JsonTextReader(new StringReader(text)))
            {
                bool any;
                while ((any = r.Read()) && r.TokenType == JsonToken.Comment) { }
                if (!any) return null;
                JToken t = JToken.ReadFrom(r);
                while (r.Read())
                    if (r.TokenType != JsonToken.Comment) throw new FormatException("there is more after the { } object");
                return t;
            }
        }

        private JObject ReadFile(out FileState state, out string problem)
        {
            problem = null;
            try
            {
                if (!File.Exists(_path)) { state = FileState.Missing; return null; }
                string text = File.ReadAllText(_path);
                if (string.IsNullOrWhiteSpace(text)) { state = FileState.Read; return new JObject(); }   // the mods read an empty file as the defaults
                JToken parsed = JToken.Parse(text);
                if (parsed.Type == JTokenType.Comment) parsed = AfterComments(text);   // the mod's read skips them
                if (parsed == null) { state = FileState.Read; return new JObject(); }   // comments only: the defaults
                if (parsed is JObject o) { state = FileState.Read; return o; }
                state = FileState.Unreadable;
                problem = "it isn't a { } object";
                return null;
            }
            catch (Exception e)
            {
                state = FileState.Unreadable;
                problem = e.Message;
                return null;
            }
        }

        // The mod's own read of the file, field by field, for the rows, the status line and Current() alike: values,
        // and odd, the status line's text when a value isn't what the mod runs (oddChoice: only a choice or color fell
        // back, on its own). JsonConvert meets every key in the file's order: a key goes to a field exactly, else
        // ignoring case (Target); the last key a field gets is its value, and a value it can't take anywhere before
        // that fails the whole read too. Null obj reads every default.
        private double[] ReadValues(JObject obj, out string odd, out bool oddChoice)
        {
            odd = null;
            oddChoice = false;
            double[] v = new double[_fields.Length];
            for (int i = 0; i < v.Length; i++) v[i] = _fields[i].Default;
            if (obj == null) return v;
            List<JProperty> keys = Walk(obj);
            for (int i = 0; i < v.Length; i++)
            {
                if (_fields[i].Kind == SettingKind.Heading) continue;
                int last = -1;
                for (int k = 0; k < keys.Count; k++) if (Target(keys[k].Name) == i) last = k;
                for (int k = 0; k <= last; k++)
                {
                    if (Target(keys[k].Name) != i) continue;
                    string name = keys[k].Name;
                    int nth = 0;
                    for (int e = 0; e < k; e++) if (keys[e].Name == name) nth++;
                    Func<string> literal = () => Literal(name, nth);
                    if (k == last) { v[i] = ValueIn(keys[k].Value, name, _fields[i], ref odd, ref oddChoice, literal); continue; }
                    string failed = null;
                    bool alone = false;
                    ValueIn(keys[k].Value, name, _fields[i], ref failed, ref alone, literal);
                    if (failed != null && !alone && (odd == null || oddChoice)) { odd = failed; oddChoice = false; }
                }
            }
            return v;
        }

        // Every key of the file with its value, in the file's own order, as the mod's own read meets them (the parsed
        // file keeps a repeated key once). The parsed file's keys when the text can't be walked.
        private List<JProperty> Walk(JObject obj)
        {
            var keys = new List<JProperty>();
            try
            {
                using (var r = new JsonTextReader(new StringReader(File.ReadAllText(_path))))
                {
                    while (r.Read() && r.TokenType == JsonToken.Comment) { }
                    if (r.TokenType != JsonToken.StartObject) throw new FormatException("not an object");
                    while (r.Read() && r.TokenType != JsonToken.EndObject)
                    {
                        if (r.TokenType != JsonToken.PropertyName) continue;   // a comment
                        string name = (string)r.Value;
                        do r.Read(); while (r.TokenType == JsonToken.Comment);
                        keys.Add(new JProperty(name, JToken.ReadFrom(r)));
                    }
                }
                return keys;
            }
            catch (Exception)
            {
                keys.Clear();
                foreach (JProperty p in obj.Properties()) keys.Add(p);
                return keys;
            }
        }

        // The field a key in the file goes to, as JsonConvert matches it, or -1.
        private int Target(string name)
        {
            int loose = -1;
            for (int i = 0; i < _fields.Length; i++)
            {
                string key = _fields[i].Key;
                if (key == null) continue;
                if (key == name) return i;
                if (loose < 0 && string.Equals(key, name, StringComparison.OrdinalIgnoreCase)) loose = i;
            }
            return loose;
        }

        // One value as the mod's own read (JsonConvert.DeserializeObject into its settings class: bool for a toggle,
        // int, float, and string for a choice or a color) takes t, the value of key name (null: no such key).
        // literal gives the text a string read takes from it (a number as written).
        //   - A value that read can't convert makes it fail, and the mod starts on ALL its defaults: the field shows
        //     its default and `odd` names the key (300.0 or 1e3 in a whole-number field, true in a number field, null
        //     in a toggle or number, text that isn't one, an array or object anywhere).
        //   - A toggle takes true or false, a number of any size (non-zero is on) or the text "true" or "false".
        //   - A number field takes what a float does, text read through a double: 1e39 and "1e39" are infinity, shown
        //     as it is (Limits hold it; Save writes the nearest of Min and Max). NaN, which that read also takes, is
        //     counted odd here: it equals nothing, so no row could hold it.
        //   - A choice is a text matching one of its options (trimmed, any case). Any other text, or a number, shows
        //     the default, and the mod uses the default for that setting alone; null is no value.
        //   - A color is text TryColor takes. A number there is read as written, as the mod's own string read takes
        //     it (literal gives that text: 123456 and 000000 are colors). Anything else shows the default, and the mod
        //     uses it for that one; null is no value.
        //   - The status line names the first odd value, except that one failing the read replaces a choice or color
        //     that fell back alone (oddChoice), so it never understates what the mod is running.
        private static double ValueIn(JToken t, string name, SettingField f, ref string odd, ref bool oddChoice, Func<string> literal)
        {
            bool text = f.Kind == SettingKind.Choice || f.Kind == SettingKind.Color;   // read into a string: null is no value
            if (t == null || f.Kind == SettingKind.Heading || t.Type == JTokenType.Null && text) return f.Default;
            try
            {
                switch (f.Kind)
                {
                    case SettingKind.Bool:
                        if (t.Type == JTokenType.Integer) return t.ToString(Formatting.None) != "0" ? 1 : 0;   // any size
                        if (t.Type == JTokenType.Boolean || t.Type == JTokenType.Float || t.Type == JTokenType.String)
                            return t.ToObject<bool>() ? 1 : 0;
                        break;
                    case SettingKind.Int:
                        if (t.Type == JTokenType.Integer || t.Type == JTokenType.String) return t.ToObject<int>();
                        break;
                    case SettingKind.Choice:
                        if (t.Type == JTokenType.String)
                        {
                            int i = f.OptionIndex((string)t);
                            if (i >= 0) return i;
                        }
                        break;
                    case SettingKind.Color:
                        string s = t.Type == JTokenType.String ? (string)t
                                 : t.Type == JTokenType.Integer || t.Type == JTokenType.Float ? literal?.Invoke() : null;
                        if (SettingField.TryColor(s, out int rgb)) return rgb;
                        break;
                    default:
                        if (t.Type == JTokenType.Integer || t.Type == JTokenType.Float || t.Type == JTokenType.String)
                        {
                            // text goes through a double, as the mod's float read does: "1e39" is infinity, not too big
                            double v = t.Type != JTokenType.String ? t.ToObject<float>()
                                     : double.TryParse((string)t, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture,
                                                       out double d) ? (float)d : double.NaN;
                            if (!double.IsNaN(v)) return Round(f, v);
                        }
                        break;
                }
            }
            catch { }
            bool own = text && t.Type != JTokenType.Array && t.Type != JTokenType.Object;   // falls back for this setting alone
            if (odd == null || (oddChoice && !own))
            {
                oddChoice = own;
                string raw = t.ToString(Formatting.None);
                if (raw.Length > 24) raw = raw.Substring(0, 24) + "...";
                string what = f.Kind == SettingKind.Bool ? "on or off" : f.Kind == SettingKind.Int ? "a whole number"
                            : f.Kind == SettingKind.Choice ? "one of " + Plain(string.Join(", ", f.Options))
                            : f.Kind == SettingKind.Color ? "a color like #RRGGBB" : "a number";
                string then = !own ? "starts on its defaults"
                            : "uses " + (f.Kind == SettingKind.Choice ? Plain(f.OptionAt(f.Default)) : SettingField.ColorHex((int)f.Default));
                odd = (name == f.Key ? f.Key : Plain(name)) + " = " + Plain(raw) + " in settings.json isn't " + what + ", so the mod " + then + ".";
            }
            return f.Default;
        }

        // The text the mod's own read takes from the nth value of key name (counting from 0, that spelling only), read
        // again from the file: a number as written (000000, 1e3), which the parsed file has lost. Null when it can't
        // be read.
        private string Literal(string name, int nth)
        {
            try
            {
                using (var r = new JsonTextReader(new StringReader(File.ReadAllText(_path))))
                {
                    int n = 0;
                    while (r.Read() && r.TokenType != JsonToken.StartObject) { }
                    while (r.Read() && r.TokenType != JsonToken.EndObject)
                    {
                        if (r.TokenType != JsonToken.PropertyName) continue;   // a comment
                        if ((string)r.Value == name && n++ == nth) return r.ReadAsString();
                        r.Skip();
                    }
                    return null;
                }
            }
            catch (Exception) { return null; }
        }

        private bool SaveFile(out string error, out bool applied)
        {
            error = null;
            applied = false;
            // Re-read first: keys this section doesn't know (and anything written since it opened) are kept, and a file
            // that
            // has become unreadable is left alone.
            JObject disk = ReadFile(out FileState state, out string problem);
            if (state == FileState.Unreadable)
            {
                _state = state; _problem = problem; _loaded = true; _stamp = Stamp();
                error = "Not saved: settings.json can't be read, so it's left alone.";
                return false;
            }
            Recompute();
            double[] w = new double[_held.Length];
            for (int i = 0; i < w.Length; i++) w[i] = Writable(_fields[i], _held[i]);
            JToken before = disk?.DeepClone();
            JObject o = WithValues(disk ?? new JObject(), w);   // a value outside its Limits is saved held
            _updated = before == null || !JToken.DeepEquals(before, o);
            string tmp = _path + ".tmp";
            try
            {
                File.WriteAllText(tmp, o.ToString(Formatting.Indented));
                if (File.Exists(_path)) File.Replace(tmp, _path, null);
                else File.Move(tmp, _path);
            }
            catch (Exception e)
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                LogOnce("save:" + e.Message, _title + ": couldn't save settings.json: " + PathClean.CleanText(e.Message));
                error = "Couldn't save: <noparse>" + PathClean.ForScreen(e.Message) + "</noparse>";
                return false;
            }
            Array.Copy(w, _values, _values.Length);
            Array.Copy(w, _saved, _values.Length);
            _state = FileState.Read;
            _stamp = Stamp();
            _loaded = true;
            try { _applied?.Invoke(o); applied = true; }
            catch (Exception e) { LogOnce("apply", _title + ": saved settings.json, but couldn't apply it until a restart: " + PathClean.CleanText(e.ToString(), WholeCap)); }
            _behind = applied ? null : NotApplied;
            return true;
        }

        // What Save writes for a value: an infinite one (a float field's 1e39) as the nearer of Min and Max, NaN as the
        // default, so the file never gets a number JSON has no form for. Every other value as it is.
        private static double Writable(SettingField f, double v) =>
            double.IsNaN(v) ? f.Default : double.IsInfinity(v) ? (v > 0 ? f.Max : f.Min) : v;

        private static JToken Token(SettingField f, double v)
        {
            switch (f.Kind)
            {
                case SettingKind.Bool: return new JValue(v != 0);
                case SettingKind.Int: return new JValue((long)Round(f, v));
                case SettingKind.Choice: return new JValue(f.OptionAt(v));
                case SettingKind.Color: return new JValue(SettingField.ColorHex((int)v));
                default: return new JValue(Round(f, v));
            }
        }

        // Half away from zero, the way people round (Math.Round alone rounds 2.5 to 2).
        private static double Round(SettingField f, double v) => Math.Round(v, f.Places, MidpointRounding.AwayFromZero);

        private static string Format(SettingField f, double v) =>
            double.IsInfinity(v) || double.IsNaN(v) ? Plain(v) : f.Places == 0 ? ((long)Round(f, v)).ToString(CultureInfo.InvariantCulture)
                          : Round(f, v).ToString("0." + new string('#', f.Places), CultureInfo.InvariantCulture);

        private static string Plain(double v) => v.ToString("0.##########", CultureInfo.InvariantCulture);

        // Outside text (a typed entry or a file's value) for a rich-text label: no tags get through, and no TextMeshPro
        // \u or
        // \U escape either (it decodes them itself, so an escaped < would be a tag), so a \ or run of them just before
        // u or U is dropped. Exception text goes through PathClean.ForScreen instead.
        private static string Plain(string s)
        {
            var b = new StringBuilder((s ?? "").Length);
            foreach (char c in s ?? "")
            {
                if (c == '<' || c == '>') continue;
                if (c == 'u' || c == 'U') while (b.Length > 0 && b[b.Length - 1] == '\\') b.Length--;
                b.Append(c);
            }
            return b.ToString();
        }

        private const int WholeCap = 4000;   // PathClean.CleanText's cap for a whole exception with its stack

        private static string HelpText(SettingField f)
        {
            string range = f.Kind == SettingKind.Bool ? ""
                : f.Kind == SettingKind.Choice ? " One of " + string.Join(", ", f.Options) + "; default " + f.OptionAt(f.Default) + "."
                : f.Kind == SettingKind.Color ? " Default " + SettingField.ColorHex((int)f.Default) + "."
                : f.Linked ? " Range follows other settings; default " + Format(f, f.Default) + "."
                : " From " + Format(f, f.Min) + " to " + Format(f, f.Max) + ", default " + Format(f, f.Default) + ".";
            return f.Help + range + (f.Live ? "" : " Needs a restart.");
        }

        private const string RestartTag = "<alpha=#B0><i>after restart</i>";
        private static string Dim(string s) => "<alpha=#80>" + s;
        private static string Footnote(string s) => "<style=Footnote>" + s + "</style>";

        private void Guard(string what, Action action)
        {
            try { action(); }
            catch (Exception e) { LogOnce(what, _title + ": settings " + what + " failed: " + PathClean.CleanText(e.ToString(), WholeCap)); SetStatus("Something went wrong. The log says what."); }
        }

        private void LogOnce(string key, string message)
        {
            if (_logged.Add(key)) { try { _log?.Invoke(message); } catch { } }
        }
    }

    // One "Mods" tab in the game's Preferences window, shared by every mod that carries this file.
    //
    // THE SHARED CONTRACT (every copy of this file, of any revision, keeps it)
    //   - AppDomain.CurrentDomain.GetData("ashenmare.preferences-mods.v1") is a List<Action<UIPanelBuilder>>, one entry
    //     per loaded mod. Its key and type never change, and it is changed only under lock (list).
    //   - Each mod adds its entry (at the end) when it loads and removes it when it unloads.
    //   - In the game's PreferencesBuilder.BuildTabs, only the mod whose entry is first adds the tab ("Mods", id
    //     "ashenmare.mods"). The tab calls every entry, each inside its own try; the order is the drawing copy's own.
    //   - A copy may move its own entry to the front, only when its revision is higher than the revision of every
    //     entry before it. Revisions are recorded in AppDomain data "ashenmare.preferences-mods.revisions.v1" (a
    //     List<object[]>: entry, revision (int), load index (int), mod name (string or null)); an entry with no record
    //     is revision 0 (Revisions 7 to 9 never write one). So the newest revision present draws the tab, and among
    //     equals the first loaded. When the first entry goes, the copy removing it moves the highest remaining entry
    //     (the first loaded among equals) to the front, so the tab passes on the same way.
    //   - Nothing else crosses between mods but the revisions slot and the search slot (ModsSearch), BCL types only.
    internal static class PreferencesModsTab
    {
        private const string SlotKey = "ashenmare.preferences-mods.v1";
        private static Action<UIPanelBuilder> _mine;
        private static Action<string> _log;
        private static bool _loggedPostfix, _loggedEntry;

        // Patches PreferencesBuilder.BuildTabs on your Harmony instance (its UnpatchAll undoes it) and adds draw to the
        // shared list (moved to the front when this revision is newer than every entry before it). Returns false, with
        // one log line, when this game version has no such method. Safe to call again without an unload: the postfix
        // goes on once (a second would add a second Mods tab) and the previous entry is replaced.
        internal static bool Add(Harmony harmony, Action<UIPanelBuilder> draw, Action<string> log)
        {
            _log = log;
            try
            {
                MethodInfo target = AccessTools.Method(typeof(PreferencesBuilder), "BuildTabs", new[] { typeof(UITabbedPanelBuilder) });
                if (target == null) { Log("the game's Preferences has no BuildTabs here, so the settings aren't in Preferences > Mods"); return false; }
                MethodInfo postfix = AccessTools.Method(typeof(PreferencesModsTab), nameof(BuildTabsPostfix));
                if (!Patched(target, postfix)) harmony.Patch(target, postfix: new HarmonyMethod(postfix));
                Remove();
                _mine = draw;
                List<Action<UIPanelBuilder>> list = Slot(true);
                lock (list)
                {
                    list.Add(draw);
                    ModsOrder.Join(list, draw, SettingsSection.Revision);
                }
                return true;
            }
            catch (Exception e) { Log("couldn't add the settings to Preferences > Mods: " + PathClean.CleanText(e.Message)); return false; }
        }

        // True when this copy's postfix is already on the method (this assembly's own MethodInfo, so another mod's copy
        // never matches).
        private static bool Patched(MethodBase target, MethodInfo postfix)
        {
            Patches info = Harmony.GetPatchInfo(target);
            if (info == null) return false;
            foreach (Patch p in info.Postfixes) if (p.PatchMethod == postfix) return true;
            return false;
        }

        // Takes this mod's entry out (on unload, and on any path where the mod gives up after Add), with its revision
        // record. The patch itself goes with the mod's own UnpatchAll.
        internal static void Remove()
        {
            try
            {
                List<Action<UIPanelBuilder>> list = Slot(false);
                if (list != null && _mine != null)
                    lock (list)
                    {
                        list.Remove(_mine);
                        ModsOrder.Leave(list, _mine);
                    }
            }
            catch { }
            _mine = null;
        }

        private static List<Action<UIPanelBuilder>> Slot(bool create)
        {
            AppDomain d = AppDomain.CurrentDomain;
            var list = d.GetData(SlotKey) as List<Action<UIPanelBuilder>>;
            if (list == null && create) { list = new List<Action<UIPanelBuilder>>(); d.SetData(SlotKey, list); }
            return list;
        }

        private static void BuildTabsPostfix(UITabbedPanelBuilder builder)
        {
            try
            {
                List<Action<UIPanelBuilder>> list = Slot(false);
                if (list == null || _mine == null) return;
                lock (list) { if (list.Count == 0 || !ReferenceEquals(list[0], _mine)) return; }
                builder.AddTab("Mods", "ashenmare.mods", DrawTab);
            }
            catch (Exception e)
            {
                if (!_loggedPostfix) { _loggedPostfix = true; Log("couldn't add the Mods tab to Preferences: " + PathClean.CleanText(e.Message)); }
            }
        }

        private static void DrawTab(UIPanelBuilder b)
        {
            b.VScrollView(s =>
            {
                s.ConfigureLayout(null, new RectOffset(0, 4, 0, 0));
                s.AddLabel("<style=Footnote>Settings for the mods you have that offer them, saved in each mod's settings.json on this computer.</style>");
                Body(s);
            });
        }

        // The tab's content after its note, drawn by its build closure, so every rebuild of the tab (in place or not)
        // draws it again: the search box, then every entry A to Z by mod name (ModsOrder), each in its own try.
        internal static void Body(UIPanelBuilder s)
        {
            List<Action<UIPanelBuilder>> list = Slot(false);
            if (list == null) return;
            Action<UIPanelBuilder>[] entries;
            lock (list) entries = list.ToArray();
            object first = entries.Length > 0 ? entries[0] : null;
            string q = ModsSearch.Drawing(s, first);
            List<object[]> records = ModsOrder.Records(false);
            foreach (Action<UIPanelBuilder> entry in ModsOrder.Order(entries))
            {
                // under a search an older revision's mod can only be found by its name: it draws in full or not at all
                if (q != null && ModsOrder.IsOlder(records, entry) && !ModsSearch.OlderShows(ModsOrder.Name(records, entry), q)) continue;
                try { entry(s); }
                catch (Exception e)
                {
                    if (!_loggedEntry) { _loggedEntry = true; Log("a mod's settings section failed to draw: " + PathClean.CleanText(e.Message)); }
                }
            }
        }

        private static void Log(string message)
        {
            try { _log?.Invoke(message); } catch { }
        }
    }

    // The order of the Mods tab: who draws it (the newest revision present, through the revisions slot) and the order
    // the drawing copy calls the entries in (A to Z by mod name, ties in load order). Everything that crosses is BCL.
    internal static class ModsOrder
    {
        private const string RevisionsKey = "ashenmare.preferences-mods.revisions.v1";
        private static string _title;   // the first section made since this copy's entry last went: the mod's name here
        private static readonly Dictionary<object, string> _names = new Dictionary<object, string>();   // per entry

        internal static void Made(string title) { if (_title == null) _title = title; }

        // The revisions slot (made when create and absent); null when it holds something else.
        internal static List<object[]> Records(bool create)
        {
            AppDomain d = AppDomain.CurrentDomain;
            object o = d.GetData(RevisionsKey);
            if (o == null && create) { o = new List<object[]>(); d.SetData(RevisionsKey, o); }
            return o as List<object[]>;
        }

        private static object[] RecordOf(List<object[]> records, object entry)
        {
            if (records != null)
                foreach (object[] r in records)
                    if (r != null && r.Length >= 3 && ReferenceEquals(r[0], entry)) return r;
            return null;
        }

        // An entry's revision: its record's, or 0 (no record, or one that isn't read as written here).
        internal static int RevisionOf(List<object[]> records, object entry)
        {
            object[] r = RecordOf(records, entry);
            return r != null && r[1] is int v ? v : 0;
        }

        // At Add, under lock (list), with draw just appended: records it, and moves it to the front when its revision
        // is higher than that of every entry before it.
        internal static void Join(List<Action<UIPanelBuilder>> list, Action<UIPanelBuilder> draw, int revision)
        {
            List<object[]> records = Records(true);
            Action<UIPanelBuilder>[] all = list.ToArray();
            int at = Array.IndexOf(all, draw);
            if (records != null)
            {
                records.RemoveAll(r => r != null && r.Length >= 1 && ReferenceEquals(r[0], draw));
                records.Add(new object[] { draw, revision, at, _title });
            }
            for (int i = 0; i < at; i++)
                if (RevisionOf(records, all[i]) >= revision) return;   // an entry before it is as new or newer
            if (at > 0) { list.Remove(draw); list.Insert(0, draw); }
        }

        // At Remove, under lock (list), with this entry just taken out: drops its record; and when it was first, so the
        // tab now has no newest drawer, moves the highest remaining entry (the first loaded among equals) to the front.
        internal static void Leave(List<Action<UIPanelBuilder>> list, Action<UIPanelBuilder> mine)
        {
            List<object[]> records = Records(false);
            object[] gone = RecordOf(records, mine);
            records?.RemoveAll(r => r != null && r.Length >= 1 && ReferenceEquals(r[0], mine));
            _title = null;
            if (gone == null || list.Count == 0) return;
            Action<UIPanelBuilder>[] all = list.ToArray();
            Action<UIPanelBuilder> best = all[0];
            int bestRevision = RevisionOf(records, best), bestIndex = int.MaxValue;
            foreach (Action<UIPanelBuilder> e in all)
            {
                int rev = RevisionOf(records, e);
                object[] r = RecordOf(records, e);
                int index = r != null && r[2] is int i ? i : int.MaxValue;
                if (rev > bestRevision || rev == bestRevision && rev > 0 && index < bestIndex) { best = e; bestRevision = rev; bestIndex = index; }
            }
            if (!ReferenceEquals(best, all[0]) && bestRevision > RevisionOf(records, all[0])) { list.Remove(best); list.Insert(0, best); }
        }

        // The entries in the order the tab draws them: by mod name, any case (invariant culture), ties in load order.
        internal static Action<UIPanelBuilder>[] Order(Action<UIPanelBuilder>[] entries)
        {
            List<object[]> records = Records(false);
            // load order: the entries without a record keep their order, each recorded one goes back to its load index
            // (a List<object>, so the only List<Action<UIPanelBuilder>> in this file is the shared one)
            var load = new List<object>();
            foreach (Action<UIPanelBuilder> e in entries) if (RecordOf(records, e) == null) load.Add(e);
            var recorded = new List<KeyValuePair<int, object>>();
            foreach (Action<UIPanelBuilder> e in entries)
            {
                object[] r = RecordOf(records, e);
                if (r != null) recorded.Add(new KeyValuePair<int, object>(r[2] is int i ? i : int.MaxValue, e));
            }
            recorded.Sort((x, y) => x.Key.CompareTo(y.Key));
            foreach (KeyValuePair<int, object> kv in recorded) load.Insert(Math.Max(0, Math.Min(kv.Key, load.Count)), kv.Value);
            // names, kept per entry while it stays in the list
            var keep = new HashSet<object>(entries);
            foreach (object k in new List<object>(_names.Keys)) if (!keep.Contains(k)) _names.Remove(k);
            var named = new List<KeyValuePair<string, Action<UIPanelBuilder>>>();
            foreach (object o in load)
            {
                var e = (Action<UIPanelBuilder>)o;
                named.Add(new KeyValuePair<string, Action<UIPanelBuilder>>(Name(records, e), e));
            }
            CompareInfo compare = CultureInfo.InvariantCulture.CompareInfo;
            for (int i = 1; i < named.Count; i++)   // insertion sort: stable, so ties keep load order
            {
                KeyValuePair<string, Action<UIPanelBuilder>> x = named[i];
                int j = i - 1;
                while (j >= 0 && compare.Compare(named[j].Key, x.Key, CompareOptions.IgnoreCase) > 0) { named[j + 1] = named[j]; j--; }
                named[j + 1] = x;
            }
            var order = new Action<UIPanelBuilder>[named.Count];
            for (int i = 0; i < order.Length; i++) order[i] = named[i].Value;
            return order;
        }

        // An entry with no record in the revisions slot is a Revision 7 to 9 mod's (they never write one). With no
        // revisions slot to read (absent, or not ours), nothing can be told apart: no entry counts as older.
        internal static bool IsOlder(List<object[]> records, object entry) => records != null && RecordOf(records, entry) == null;

        // An entry's mod name, found once and kept while the entry stays in the list (Order drops the others).
        internal static string Name(List<object[]> records, object entry)
        {
            if (_names.TryGetValue(entry, out string name)) return name;
            name = NameOf(records, entry, out bool keep);
            if (keep) _names[entry] = name;   // only a good read is kept: a failed read or an empty name is asked again
            return name;
        }

        // An entry's mod name: the first section title its copy recorded; else the "name" in the Info.json of the mod's
        // folder (the name the game shows, read from the file the game read); else its assembly's name; else "".
        internal static string NameOf(List<object[]> records, object entry) => NameOf(records, entry, out _);

        // keep: whether the answer may be remembered (a title, the Info.json name, or the assembly's name when no
        // Info.json is there; not a fallback after a read that threw, and not an empty name).
        private static string NameOf(List<object[]> records, object entry, out bool keep)
        {
            keep = true;
            object[] r = RecordOf(records, entry);
            if (r != null && r.Length >= 4 && r[3] is string title && title.Trim().Length > 0) return title.Trim();
            Assembly assembly = null;
            try { assembly = (entry as Delegate)?.Method.DeclaringType?.Assembly; } catch (Exception) { }
            if (assembly == null) { keep = false; return ""; }
            try
            {
                string dir = Path.GetDirectoryName(assembly.Location);
                for (int up = 0; up < 3 && !string.IsNullOrEmpty(dir); up++, dir = Path.GetDirectoryName(dir))
                {
                    string info = Path.Combine(dir, "Info.json");
                    if (!File.Exists(info)) continue;
                    string name;
                    try { name = (string)(JToken.Parse(File.ReadAllText(info)) as JObject)?.GetValue("name", StringComparison.OrdinalIgnoreCase); }
                    catch (Exception) { keep = false; name = null; }   // the file is there but wasn't read: ask again next time
                    if (!string.IsNullOrEmpty(name?.Trim())) return name.Trim();
                    break;
                }
            }
            catch (Exception) { }
            string fallback;
            try { fallback = assembly.GetName().Name ?? ""; } catch (Exception) { fallback = ""; }
            if (fallback.Length == 0) keep = false;
            return fallback;
        }
    }

    // Search in the Mods tab (Revision 10): a box at the top finds mods by name and settings by label.
    //   - AppDomain.CurrentDomain.GetData("ashenmare.preferences-mods.search.v1") is a Dictionary<string, object>, made
    //     by the first copy that needs it: "version" (int, 1), "query" (string, trimmed; empty is no search), "owner"
    //     (the first v1 entry when the drawing copy last drew the box; null while it draws), "drawing" (int, counts the
    //     drawings of the tab), "sections" (a List<object> of Dictionary<string, object>: "name" (string), "shows"
    //     (Func<string, bool>: whether a search for that text shows the section), "section" (object, the section
    //     itself), "entry" (Func<object>: that copy's v1 entry now, null once removed) and "built" (int, the drawing it
    //     last drew in)). Only BCL types cross. Anything else in the slot means no search; a key that is missing or of
    //     another type is read as absent. A later version only adds keys.
    //   - The copy that draws the tab draws the box in the tab's build closure, before every entry, so each rebuild of
    //     the tab draws it again. A section filters only while the owner is the first v1 entry, which the drawing copy
    //     sets after the box is drawn: under an older drawer, or one whose box failed, every section draws in full.
    //   - Matching: the trimmed query, any case, anywhere in the mod's name, a row's label or a heading's text. A mod
    //     whose name matches shows in full; else its matching rows (and the rows under a matching heading), under
    //     their headings, with its buttons and status line; a mod with no matching row that ShowWhen shows draws
    //     nothing. A matching row ShowWhen hides is drawn hidden. A row the search hides keeps its value: Save, Undo
    //     and Reset act on every field.
    //   - A mod of an older revision (an entry with no record in the revisions slot) can't be searched by its rows:
    //     under a search the drawing copy draws it in full when its name matches (the same rule as a Revision 10 name)
    //     and skips it otherwise. That is the drawing copy's own choice; nothing about it crosses. A line under the
    //     box says so when one is installed.
    //   - The box is the game's filter field, applied on Enter or on leaving it (its x clears): showing or hiding a
    //     section rebuilds the panel (the game's section header can't be hidden in place), and a rebuild while typing
    //     would take the field's focus away. The query stays until cleared.
    //   - Each section records itself as it draws; a record not drawn in the tab's last drawing, or whose mod's v1
    //     entry has gone, is dropped at the next drawing, so nothing builds up across reloads.
    internal static class ModsSearch
    {
        private const string SlotKey = "ashenmare.preferences-mods.v1", SearchKey = "ashenmare.preferences-mods.search.v1";
        internal static Action<UIPanelBuilder, string, int> DrawBox = Box;   // the box: builder, query, last drawing

        // The search slot (made when create and absent); null when it holds something else.
        internal static Dictionary<string, object> Slot(bool create)
        {
            AppDomain d = AppDomain.CurrentDomain;
            object o = d.GetData(SearchKey);
            if (o == null && create) { o = new Dictionary<string, object> { { "version", 1 }, { "query", "" } }; d.SetData(SearchKey, o); }
            return o as Dictionary<string, object>;
        }

        private static T Get<T>(Dictionary<string, object> d, string key) where T : class =>
            d.TryGetValue(key, out object v) ? v as T : null;

        private static int Int(Dictionary<string, object> d, string key) => d.TryGetValue(key, out object v) && v is int i ? i : 0;

        // The v1 entries, first to last. The v1 list is only read here.
        private static object[] Entries()
        {
            var list = AppDomain.CurrentDomain.GetData(SlotKey) as List<Action<UIPanelBuilder>>;
            if (list == null) return new object[0];
            lock (list) return list.ToArray();
        }

        // This copy's v1 entry, read from PreferencesModsTab.
        private static object Mine()
        {
            try { return typeof(PreferencesModsTab).GetField("_mine", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null); }
            catch (Exception) { return null; }
        }

        // Whether an older mod's section draws under a query: its name matches, or it has none to match (never hidden).
        internal static bool OlderShows(string name, string q) => string.IsNullOrEmpty(name) || Matches(name, q);

        internal static bool Matches(string text, string q) => text != null && text.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;

        // The query sections filter by now; null when there is no search (none typed, or no box in this tab).
        internal static string Query()
        {
            Dictionary<string, object> d = Slot(false);
            if (d == null) return null;
            object[] entries = Entries();
            lock (d)
            {
                object owner = Get<object>(d, "owner");
                if (owner == null || entries.Length == 0 || !ReferenceEquals(owner, entries[0])) return null;
                string q = (Get<string>(d, "query") ?? "").Trim();
                return q.Length > 0 ? q : null;
            }
        }

        // Sets the query; true when it changed.
        internal static bool SetQuery(string text)
        {
            Dictionary<string, object> d = Slot(true);
            if (d == null) return false;
            string q = (text ?? "").Trim();
            lock (d)
            {
                if ((Get<string>(d, "query") ?? "") == q) return false;
                d["query"] = q;
                return true;
            }
        }

        // The drawing copy, before the entries in each drawing of the tab: a new drawing, the box, then the owner.
        // Returns the query this drawing filters by: null when there is none, or the box didn't draw.
        internal static string Drawing(UIPanelBuilder s, object first)
        {
            Dictionary<string, object> d = Slot(true);
            if (d == null) return null;
            string q;
            int last;
            lock (d)
            {
                last = Int(d, "drawing");
                d["drawing"] = last + 1;
                d["owner"] = null;   // no filtering unless the box is drawn
                if (!(Get<object>(d, "version") is int)) d["version"] = 1;
                q = (Get<string>(d, "query") ?? "").Trim();
            }
            Records(last, out _);   // drops records not drawn last time, or whose mod has gone
            try { DrawBox(s, q, last); }
            catch (Exception) { return null; }
            lock (d) d["owner"] = first;
            return q.Length > 0 && first != null ? q : null;
        }

        // A section records itself as it draws (its earlier record, by the section itself, is replaced) and gets the
        // query. A "sections" that isn't a List<object> is left alone, and nothing is recorded.
        internal static string Begin(object section, string name, Func<string, bool> shows)
        {
            Dictionary<string, object> d = Slot(true);
            if (d == null || section == null) return Query();
            lock (d)
            {
                List<object> list = null;
                if (!d.TryGetValue("sections", out object o) || o == null) { list = new List<object>(); d["sections"] = list; }
                else list = o as List<object>;
                if (list != null)
                {
                    list.RemoveAll(x => x is Dictionary<string, object> r && ReferenceEquals(Get<object>(r, "section"), section));
                    list.Add(new Dictionary<string, object> { { "name", name }, { "shows", shows }, { "section", section },
                                                              { "entry", (Func<object>)Mine }, { "built", Int(d, "drawing") } });
                }
            }
            return Query();
        }

        // The records counted for the lines under the box: drawn in the tab's drawing since or later (all, when since
        // is negative), with their mod's v1 entry still there. The others are dropped. older: a v1 entry of an older
        // revision is installed (ModsOrder.IsOlder); olderShows: one of them has a name that q matches (q null: none).
        internal static List<KeyValuePair<string, Func<string, bool>>> Records(int since, out bool older) => Scan(since, out older, null, out _);

        internal static List<KeyValuePair<string, Func<string, bool>>> Scan(int since, out bool older, string q, out bool olderShows)
        {
            var found = new List<KeyValuePair<string, Func<string, bool>>>();
            object[] entries = Entries();
            Dictionary<string, object> d = Slot(false);
            if (d != null)
                lock (d)
                {
                    List<object> list = Get<List<object>>(d, "sections");
                    if (list != null)
                        list.RemoveAll(o =>
                        {
                            if (!(o is Dictionary<string, object> r)) return false;   // not ours to judge: left alone
                            object entry = null;
                            try { entry = Get<Func<object>>(r, "entry")?.Invoke(); } catch (Exception) { }
                            if (entry == null || Array.IndexOf(entries, entry) < 0) return true;   // its mod has gone
                            if (since >= 0 && !(r.TryGetValue("built", out object b) && b is int built && built >= since)) return true;   // not in the tab
                            string name = Get<string>(r, "name");
                            Func<string, bool> shows = Get<Func<string, bool>>(r, "shows");
                            if (name != null && shows != null) found.Add(new KeyValuePair<string, Func<string, bool>>(name, shows));
                            return false;
                        });
                }
            older = false; olderShows = false;
            List<object[]> records = ModsOrder.Records(false);
            foreach (object e in entries)
                if (ModsOrder.IsOlder(records, e))
                {
                    older = true;
                    if (q != null && OlderShows(ModsOrder.Name(records, e), q)) olderShows = true;
                }
            return found;
        }

        // What the lines under the box say for a query: nothing shows (no section and no older mod's name matches), and
        // whether an older mod is installed.
        internal static void Notes(string q, int since, out bool nothing, out bool older)
        {
            nothing = true;
            List<KeyValuePair<string, Func<string, bool>>> found = Scan(since, out older, q, out bool olderShows);
            foreach (KeyValuePair<string, Func<string, bool>> r in found)
            {
                bool shows = false;
                try { shows = r.Value(q); } catch (Exception) { }
                if (shows) nothing = false;
            }
            if (olderShows) nothing = false;
        }

        // The box: the game's filter field (Enter or leaving it applies the query; its x clears), and the lines.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void Box(UIPanelBuilder s, string q, int last)
        {
            UIPanelBuilder tab = s;
            Action redraw = () => { try { tab.SetNeedsRebuild(); } catch (Exception) { try { tab.Rebuild(); } catch (Exception) { } } };
            s.AddFilterField(q, text => { if (SetQuery(text)) redraw(); }, "Search, Enter to apply", false);
            if (q.Length == 0) return;
            Notes(q, last, out bool nothing, out bool older);
            if (nothing) s.AddLabel("<style=Footnote>Nothing here matches.</style>");
            if (older) s.AddLabel("<style=Footnote>Older settings menus match by mod name only.</style>");
        }
    }

    // ---- BEGIN PathClean (Revision 8): copy verbatim, keep every copy identical ----
    // Takes paths out of exception text: CleanText for a log, ForScreen for a rich-text label. Player.log goes with bug
    // reports, and an IO message carries the file's whole path (C:\Users\<name>\..., Z:\home\<name>\... under Proton,
    // /home/<name>/...). It keeps what a bug report needs, which file and where under the game, and nothing before it.
    // Its own class, naming no game type, so a caller whose game types fail to load can still use it. One hand-written
    // linear scan, no Regex; it never throws.
    //
    // TERMS. A separator is / or \. A character is glued when it comes just after a letter, digit, _ or dot. A token is
    // a run of text between white space (a newline is white space, so no token crosses a line); its lead is the quotes
    // and brackets it opens with (' " ` ( [ { < and the curly quotes and guillemets). A segment is the text between two
    // separators.
    //
    // WHERE A PATH STARTS
    //  1. A drive, C:\ or C:/ (ASCII letter, colon, separator), not glued. file:/// isn't a drive.
    //  2. A separator, not glued, followed by anything but white space: /x, \\server\share, \\?\C:\, ->/x, (a)/b.
    //  3. A separator, not glued, followed by white space other than a newline, with more text after it on the line (a
    //     name can follow a space, a tab or a no-break space): "/ home/x", "/\tjane", "x / jane\x". Except the spaced
    //     operator, which stays: one plain space before the separator, exactly one after, then a word with no separator
    //     in it (3 / 4).
    //  4. The first character after the lead of a token that has a separator plus any of: a second separator; the word
    //     users, home, documents, steamuser or onedrive anywhere in it (any case); a ~ first (~/x, ~jane/x); dots then
    //     a separator first (./x, ..\x); an ASCII letter and a colon first (C:Users\x, C:jane\x); or the input was cut
    //     and the token runs to the cut end. So a path glued to a word is taken from the token's start (at/home/jane/x,
    //     x=/home/jane/x, file:///x), and a single separator between plain words (3/4, rr/ties, km/h, (1/2), and/or,
    //     Foo/Bar) isn't a path.
    //  5. The character after a path's closing quote, when that quote isn't just after white space and the rest of its
    //     token passes rule 4's test: x 'C:\a',x/home/jane/y. It ends at the end of its token, or where a path starts
    //     inside it by rule 1, 2 or 3.
    //
    // WHERE A PATH ENDS. A closing quote is the same quote as the one just before the path's first character, never
    // followed by a letter or digit (so the apostrophe in O'Brien never ends a path early).
    //  - Input cut on the path's line: at the end (its closing quote was cut off, and the last quote left may be an
    //    apostrophe).
    //  - Just after a ' or ", with a closing quote on its own line: at the last such quote when it starts by rule 1 or
    //    2, at the end of its line when it starts by rule 3 or 4.
    //  - Just after a ' or ", with no closing quote on its own line: on over the next lines (a name can hold a newline)
    //    to the end of the first line that holds one, looking no further than QuoteSpan (1000) characters from its
    //    start, else to the end of the line that limit falls in.
    //  - Any other path: at the end of its line, so a whole exception keeps its stack frames.
    // No path ends before one that starts inside it would.
    //
    // WHAT REPLACES IT. A found path can run past the path itself (to the end of its line, or over two paths), so it is
    // cut into pieces: a new one wherever a path starts inside it by rule 1, or by rule 2 or 3 just after white space
    // or a lead (' and '/home/x). A piece's body runs from its start to the first closing quote when it opens just
    // after a ' or ", else to the first white space, and never past a newline. Everything after the body is hidden, as
    // one more <path> unless it is only spaces or tabs (a stack frame's trailing space).
    // GLUED CLOSE. A piece that opens just after a ' or ", with no closing quote ending its body (and the input not cut
    // inside it), may have a quote glued to text as its real close ('x\a.json'is gone). R1 to R4 still read the whole
    // body, but nothing from that quote on is kept: it joins the hidden rest. That quote is the first one (the same as
    // the opening one) with no separator after it before the next white space, else the first one (an apostrophe in a
    // folder name, O'Brien\Documents, has a separator after it). The body becomes:
    //  R1. When a segment in it is exactly Railroader (any case, with the body's start or a separator before it, and a
    //      separator or the body's end after it): <home> for everything before the last such segment, then the
    //      separator before it and the rest of the body as written. C:\Users\Jane\Documents\Railroader\Mods\X\a.json
    //      gives <home>\Railroader\Mods\X\a.json, and ...\common\Railroader\Railroader_Data\Managed\x.dll gives
    //      <home>\Railroader\Railroader_Data\Managed\x.dll (Railroader_Data isn't Railroader). The last one, so a Steam
    //      library or other folder itself named Railroader goes with the rest of the head. No <home> when only a
    //      separator comes before it (/Railroader/x stays as it is).
    //  R2. Otherwise, when the body has a separator and its last segment is a file name (a dot with a letter or digit
    //      on each side): <path>, the separator before that name, and the name. /home/jane/a/x.dll gives <path>/x.dll.
    //  R3. In what R1 and R2 keep, a user's name becomes <user>. A user's name is: the segment just after a segment
    //      users, home, media or "documents and settings" (any case) in any path found in the text; the rest of a
    //      segment after its first ~ (~jane, x=~jane); steamuser (Proton's user); and Environment.UserName when it is
    //      known. A kept segment equal to one becomes <user>; otherwise each chunk of it (text between white space and
    //      dots) that holds a word of one (letters and digits, two or more, skipping zero-width and other format
    //      characters inside) becomes <user>. Mods\Jane\x gives Mods\<user>\x, Saves\Jane.railroad gives
    //      Saves\<user>.railroad, and for a user named Railroader the Railroader segment itself becomes <user>.
    //  R4. Otherwise <path>, so a body ending in a folder (C:\Users\Jane, /mnt/jane) shows no name at all.
    //  When the input was cut inside a body, its cut-short last segment isn't kept: R1 keeps up to its last separator,
    //  and R2 gives <path>.
    //
    // TIDYING. A "file:" just before a path folds in when it is the whole word (profile: keeps its letters); a <path>
    // right after another collapses into it; a <path> right before a <home> folds into the <home>.
    // LIMITS. It takes too much rather than too little (a URL, a date like 11/03/2026, a whole glued token, the rest of
    // an unquoted line), but it can't tell a name from a word in the two forms it keeps readable: a single glued
    // separator with none of rule 4's signs (at/jane), and the spaced operator (a / jane).
    // CAPS. The input is cut to 1000 characters, or twice max if that is more, before anything else, never splitting a
    // surrogate pair. The output keeps at most max characters, never splitting a surrogate pair or a <path>, <home> or
    // <user>, and adds "..." when anything was cut (so at most max + 3).
    internal static class PathClean
    {
        private const string Mark = "<path>", HomeMark = "<home>", UserMark = "<user>";
        private const int QuoteSpan = 1000;
        private static readonly string[] Homes = { "users", "home", "documents", "steamuser", "onedrive" };
        private static readonly string[] Parents = { "users", "home", "media", "documents and settings" };
        private static string _user;   // Environment.UserName as R3 uses it, read once; "" when unknown

        internal static string CleanText(string s, int max = 300)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (max < 0) max = 0;
            bool cut = false;
            long inCap = Math.Max(1000L, 2L * max);
            if (s.Length > inCap)
            {
                int c = (int)inCap;
                s = s.Substring(0, char.IsHighSurrogate(s[c - 1]) ? c - 1 : c);
                cut = true;
            }
            var runs = Runs(s, cut);
            var names = Names(s, runs);
            var sb = new StringBuilder(s.Length + 16);
            int at = 0;
            for (int r = 0; r < runs.Count; r += 2)
            {
                for (; at < runs[r]; at++) Copy(sb, s, at);
                if (Tail(sb, "file:") && (sb.Length == 5 || !char.IsLetterOrDigit(sb[sb.Length - 6]))) Trim(sb, sb.Length - 5);
                for (int p = runs[r], q; p < runs[r + 1]; p = q)
                {
                    q = p + 1;
                    while (q < runs[r + 1] && !Splits(s, q)) q++;
                    Piece(sb, s, p, q, cut, names);
                }
                at = runs[r + 1];
            }
            for (; at < s.Length; at++) Copy(sb, s, at);
            if (sb.Length > max)
            {
                int n = max;
                if (n > 0 && char.IsHighSurrogate(sb[n - 1])) n--;
                for (int k = Math.Max(0, n - Mark.Length + 1); k < n; k++)
                    if (IsMark(sb.ToString(k, Math.Min(Mark.Length, sb.Length - k)), 0)) { n = k; break; }
                Trim(sb, n);
                cut = true;
            }
            if (cut) Put(sb, "...");
            return sb.ToString();
        }

        // CleanText for a TextMeshPro label, to go inside <noparse>. Then:
        //   - every < and > except the <path>, <home> and <user> marks becomes [ or ], so no tag can open and the marks
        //     stay readable;
        //   - a control character other than a tab or a newline is dropped (a NUL would end the label; \r\n becomes \n,
        //     a lone \r a newline);
        //   - a \ (or a run of them) just before a u and four hex digits, or a U and eight, is dropped: TextMeshPro
        //     decodes \uXXXX and \UXXXXXXXX itself, inside <noparse> too, and an escaped < would be a tag (one could
        //     close the <noparse>). Any other \ stays, so ...\Mods\UsedEquipment reads as it should.
        // Never longer than CleanText's answer.
        internal static string ForScreen(string s, int max = 300)
        {
            string c = CleanText(s, max);
            var b = new StringBuilder(c.Length);
            for (int i = 0; i < c.Length; i++)
            {
                char ch = c[i];
                if (IsMark(c, i))
                {
                    b.Append(c, i, Mark.Length);
                    i += Mark.Length - 1;
                    continue;
                }
                if (ch == '\r')
                {
                    if (i + 1 < c.Length && c[i + 1] == '\n') continue;
                    ch = '\n';
                }
                else if (ch < ' ' && ch != '\n' && ch != '\t') continue;
                else if (ch == '<') ch = '[';
                else if (ch == '>') ch = ']';
                b.Append(ch);
            }
            var o = new StringBuilder(b.Length);
            for (int i = 0; i < b.Length; i++)
            {
                char ch = b[i];
                if ((ch == 'u' && Hex(b, i + 1, 4)) || (ch == 'U' && Hex(b, i + 1, 8)))
                    while (o.Length > 0 && o[o.Length - 1] == '\\') o.Length--;
                o.Append(ch);
            }
            return o.ToString();
        }

        private static bool Word(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '.';

        private static bool Sep(char c) => c == '/' || c == '\\';

        private static bool Newline(char c) => c == '\n' || c == '\r';

        private static bool Lead(char c) => c == '\'' || c == '"' || c == '`' || c == '(' || c == '[' || c == '{' || c == '<'
            || c == '\u00ab' || c == '\u00bb' || (c >= '\u2018' && c <= '\u201f') || c == '\u2039' || c == '\u203a';

        private static bool IsMark(string t, int at) => at + Mark.Length <= t.Length && t[at] == '<'
            && (string.CompareOrdinal(t, at, Mark, 0, Mark.Length) == 0 || string.CompareOrdinal(t, at, HomeMark, 0, Mark.Length) == 0
                || string.CompareOrdinal(t, at, UserMark, 0, Mark.Length) == 0);

        private static bool Hex(StringBuilder b, int at, int n)
        {
            if (at + n > b.Length) return false;
            for (int k = at; k < at + n; k++)
                if (!((b[k] >= '0' && b[k] <= '9') || (b[k] >= 'a' && b[k] <= 'f') || (b[k] >= 'A' && b[k] <= 'F'))) return false;
            return true;
        }

        // Every character of the answer goes in through these three, and only these take it out.
        private static void Copy(StringBuilder b, string s, int at) => b.Append(s[at]);

        private static void Put(StringBuilder b, string text) => b.Append(text);

        private static void Trim(StringBuilder b, int length) => b.Length = length;

        // The paths found (rules 1 to 5), as start and end pairs in order.
        private static System.Collections.Generic.List<int> Runs(string s, bool cut)
        {
            var runs = new System.Collections.Generic.List<int>();
            int i = 0, tokenEnd = 0, tokenPath = -1, restart = -1;
            bool rest = false;
            while (i < s.Length)
            {
                if (!char.IsWhiteSpace(s[i]) && (i == restart || (i >= tokenEnd && (i == 0 || char.IsWhiteSpace(s[i - 1])))))
                {
                    tokenPath = TokenPath(s, i, cut, out tokenEnd);
                    rest = i == restart;
                }
                int kind = StartKind(s, i);
                if (kind == 0 && i != tokenPath) { i++; continue; }
                int end;
                if (kind == 0 && rest)
                {
                    end = i + 1;
                    while (end < tokenEnd && StartKind(s, end) == 0) end++;
                    restart = -1;
                }
                else
                {
                    end = EndOf(s, i, cut, kind == 2);
                    // rule 5: only when the closing quote is inside a token (a quote just after white space starts its
                    // own token, which
                    // rule 4 tests)
                    restart = end < s.Length && !Newline(s[end]) && !char.IsWhiteSpace(s[end - 1]) ? end + 1 : -1;
                }
                runs.Add(i);
                runs.Add(end);
                i = end;
            }
            return runs;
        }

        // Rules 1 to 3, whatever token this character is in: 2 when a path starts here by rule 1 or 2 (it can end at a
        // quote),
        // 1 by rule 3, 0 when none does.
        private static int StartKind(string t, int at)
        {
            if (at > 0 && Word(t[at - 1])) return 0;
            char c = t[at];
            if (c < 128 && char.IsLetter(c))
                return at + 2 < t.Length && t[at + 1] == ':' && Sep(t[at + 2]) ? 2 : 0;
            if (!Sep(c) || at + 1 >= t.Length) return 0;
            if (!char.IsWhiteSpace(t[at + 1])) return 2;
            int k = at + 1;
            while (k < t.Length && char.IsWhiteSpace(t[k]) && !Newline(t[k])) k++;
            if (k == t.Length || Newline(t[k])) return 0;
            if (k != at + 2 || t[at + 1] != ' ' || at == 0 || t[at - 1] != ' ') return 1;
            for (; k < t.Length && !char.IsWhiteSpace(t[k]); k++)
                if (Sep(t[k])) return 1;
            return 0;
        }

        // Where a found path is cut into pieces: a path starts here by rule 1, or by rule 2 or 3 just after white space
        // or a
        // lead.
        private static bool Splits(string t, int at) =>
            StartKind(t, at) != 0 && (!Sep(t[at]) || char.IsWhiteSpace(t[at - 1]) || Lead(t[at - 1]));

        // Rule 4, for the token that starts at at: where its path starts (just past its lead), or -1. Also where the
        // token ends, so each token is looked at once.
        private static int TokenPath(string t, int at, bool cut, out int end)
        {
            int seps = 0;
            bool home = false;
            for (end = at; end < t.Length && !char.IsWhiteSpace(t[end]); end++)
            {
                if (Sep(t[end])) seps++;
                else if (!home && HomeAt(t, end)) home = true;
            }
            if (seps == 0) return -1;
            int p = at;
            while (p < end && Lead(t[p])) p++;
            if (p == end) return -1;
            int d = p;
            while (d < end && t[d] == '.') d++;
            bool dots = d > p && d < end && Sep(t[d]);
            bool drive = p + 1 < end && t[p] < 128 && char.IsLetter(t[p]) && t[p + 1] == ':';
            return seps >= 2 || home || t[p] == '~' || dots || drive || (cut && end == t.Length) ? p : -1;
        }

        private static bool HomeAt(string t, int at)
        {
            foreach (string w in Homes)
            {
                if (at + w.Length > t.Length) continue;
                int k = 0;
                while (k < w.Length && char.ToLowerInvariant(t[at + k]) == w[k]) k++;
                if (k == w.Length) return true;
            }
            return false;
        }

        // Just past the path that starts at start (see "A path ends" above). A closing quote is never followed by a
        // letter or digit, so O'Brien's apostrophe isn't one.
        private static int EndOf(string t, int start, bool wasCut, bool quotes)
        {
            int end = LineEnd(t, start);
            if (wasCut && end == t.Length) return end;
            char open = start > 0 ? t[start - 1] : '\0';
            if (open != '"' && open != '\'') return end;
            for (int q = end - 1; q > start; q--)
                if (Closes(t, q, open)) return quotes ? q : end;
            int limit = start + QuoteSpan;
            for (int q = end; q < t.Length && q < limit; q++)
                if (Closes(t, q, open)) return LineEnd(t, q);
            return limit >= t.Length ? t.Length : LineEnd(t, limit);
        }

        private static int LineEnd(string t, int at)
        {
            while (at < t.Length && !Newline(t[at])) at++;
            return at;
        }

        private static bool Closes(string t, int q, char open) =>
            t[q] == open && (q + 1 >= t.Length || !char.IsLetterOrDigit(t[q + 1]));

        // One piece of a found path, from p to q, as R1 to R4 say. Its body ends at b, and what it keeps ends at stop.
        private static void Piece(StringBuilder sb, string s, int p, int q, bool cut,
            System.Collections.Generic.HashSet<string> names)
        {
            char open = p > 0 ? s[p - 1] : '\0';
            bool quoted = open == '"' || open == '\'';
            int b = p;
            while (b < q && !Newline(s[b]) && (quoted ? !Closes(s, b, open) : !char.IsWhiteSpace(s[b]))) b++;
            bool partial = cut && b == s.Length;
            int stop = quoted && !partial && (b == s.Length || !Closes(s, b, open)) ? Glued(s, p, b, open) : b;
            int rr = -1, lastSep = -1;
            for (int k = p, seg = p; k <= b; k++)
            {
                if (k < b && !Sep(s[k])) continue;
                if (k - seg == 10 && (k < b || !partial)
                    && string.Compare(s, seg, "Railroader", 0, 10, StringComparison.OrdinalIgnoreCase) == 0) rr = seg;
                if (k < b) lastSep = k;
                seg = k + 1;
            }
            int to = partial ? lastSep + 1 : b;
            if (rr >= 0)
            {
                int from = rr > p ? rr - 1 : p;
                if (from > p)
                {
                    if (Tail(sb, Mark)) Trim(sb, sb.Length - Mark.Length);
                    Put(sb, HomeMark);
                }
                Keep(sb, s, from, to, stop, names);
            }
            else
            {
                if (!Tail(sb, Mark)) Put(sb, Mark);
                if (lastSep >= p && !partial && FileName(s, lastSep + 1, b)) Keep(sb, s, lastSep, b, stop, names);
            }
            int w = stop;
            while (w < q && char.IsWhiteSpace(s[w]) && !Newline(s[w])) w++;
            if (w < q && !Tail(sb, Mark)) Put(sb, Mark);
        }

        // The quote where a quoted body that no closing quote ends stops keeping: in a to e, the first open with no
        // separator
        // after it before the next white space, else the first open, else e when there is none.
        private static int Glued(string t, int a, int e, char open)
        {
            int first = -1, cand = -1;
            for (int k = a; k < e; k++)
            {
                if (char.IsWhiteSpace(t[k])) { if (cand >= 0) return cand; }
                else if (Sep(t[k])) cand = -1;
                else if (t[k] == open)
                {
                    if (first < 0) first = k;
                    if (cand < 0) cand = k;
                }
            }
            return cand >= 0 ? cand : first >= 0 ? first : e;
        }

        private static bool FileName(string t, int a, int e)
        {
            for (int k = a + 1; k + 1 < e; k++)
                if (t[k] == '.' && char.IsLetterOrDigit(t[k - 1]) && char.IsLetterOrDigit(t[k + 1])) return true;
            return false;
        }

        // Text R1 or R2 keeps, from a to e, segment by segment, with R3's <user> in place of a user's name. Nothing
        // from stop on is kept, but each segment and word is still read whole, so stop never undoes a <user>.
        private static void Keep(StringBuilder sb, string s, int a, int e, int stop,
            System.Collections.Generic.HashSet<string> names)
        {
            for (int k = a, seg = a; k <= e && seg < stop; k++)
            {
                if (k < e && !Sep(s[k])) continue;
                if (k > seg && names.Contains(s.Substring(seg, k - seg))) Put(sb, UserMark);
                else
                    for (int c = seg, x; c < k && c < stop; c = x)
                    {
                        x = c + 1;
                        if (char.IsWhiteSpace(s[c]) || s[c] == '.') { Copy(sb, s, c); continue; }
                        while (x < k && !char.IsWhiteSpace(s[x]) && s[x] != '.') x++;
                        if (HasName(s, c, x, names)) Put(sb, UserMark);
                        else for (int y = c; y < x && y < stop; y++) Copy(sb, s, y);
                    }
                if (k < e && k < stop) Copy(sb, s, k);
                seg = k + 1;
            }
        }

        // Whether a word from a to e is one of R3's names or a word of one.
        private static bool HasName(string t, int a, int e, System.Collections.Generic.HashSet<string> names)
        {
            for (int w = a, x; w < e; w = x)
            {
                string word = WordAt(t, w, e, out x);
                if (word != null && names.Contains(word)) return true;
            }
            return false;
        }

        // A word is letters and digits, two or more; a format character inside one (a zero-width space, a direction
        // mark) is skipped, so Jane and Ja<ZWSP>ne are the same word. The word at w (null if there is none, or it is
        // shorter), and x just past it (always past w).
        private static string WordAt(string t, int w, int e, out int x)
        {
            x = w + 1;
            if (!char.IsLetterOrDigit(t[w])) return null;
            int n = 1;
            bool skipped = false;
            for (; x < e; x++)
            {
                if (char.IsLetterOrDigit(t[x])) n++;
                else if (char.GetUnicodeCategory(t[x]) == System.Globalization.UnicodeCategory.Format) skipped = true;
                else break;
            }
            if (n < 2) return null;
            if (!skipped) return t.Substring(w, x - w);
            var b = new StringBuilder(n);
            for (int k = w; k < x; k++)
                if (char.IsLetterOrDigit(t[k])) b.Append(t[k]);
            return b.ToString();
        }

        // R3's user names, from every path found in s, and their words.
        private static System.Collections.Generic.HashSet<string> Names(string s, System.Collections.Generic.List<int> runs)
        {
            var names = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (runs.Count == 0) return names;
            names.Add("steamuser");
            string u = UserName();
            if (u.Length > 0) AddName(names, u, 0, u.Length);
            for (int r = 0; r < runs.Count; r += 2)
            {
                bool after = false;
                for (int k = runs[r], seg = runs[r], e = runs[r + 1]; k <= e; k++)
                {
                    if (k < e && !Sep(s[k])) continue;
                    if (k > seg)
                    {
                        if (after) AddName(names, s, seg, k);
                        int t = s.IndexOf('~', seg, k - seg);
                        if (t >= 0 && t + 1 < k) AddName(names, s, t + 1, k);
                        after = false;
                        foreach (string w in Parents)
                            if (k - seg == w.Length && string.Compare(s, seg, w, 0, w.Length, StringComparison.OrdinalIgnoreCase) == 0)
                                after = true;
                    }
                    seg = k + 1;
                }
            }
            return names;
        }

        private static void AddName(System.Collections.Generic.HashSet<string> names, string t, int a, int e)
        {
            names.Add(t.Substring(a, e - a));
            for (int w = a, x; w < e; w = x)
            {
                string word = WordAt(t, w, e, out x);
                if (word != null) names.Add(word);
            }
        }

        private static string UserName()
        {
            if (_user == null)
            {
                string u = "";
                try { u = (Environment.UserName ?? "").Trim(); }
                catch (Exception) { u = ""; }
                _user = u;
            }
            return _user;
        }

        private static bool Tail(StringBuilder b, string tail)
        {
            if (b.Length < tail.Length) return false;
            for (int k = 0; k < tail.Length; k++)
                if (char.ToLowerInvariant(b[b.Length - tail.Length + k]) != tail[k]) return false;
            return true;
        }
    }
    // ---- END PathClean ----
}
