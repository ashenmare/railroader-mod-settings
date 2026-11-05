// SettingsSection.cs (Revision 8)
// A settings editor for a Railroader mod, shown in Preferences > Mods (from the main menu and in game). Every mod that
// carries this file shares one Mods tab.
//
// USE
//   1. Copy this file into your mod. Change only the namespace line.
//   2. Make a SettingsSection with your fields (SettingField.Bool, Int, Float, Choice) and your settings.json path.
//   3. Load: PreferencesModsTab.Add(harmony, b => section.Build(b, true), log). Unload: PreferencesModsTab.Remove().
//   4. Your mod reads settings.json itself at load. `applied` gets the saved file (a JObject) when the player saves.
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
//   - Rows: a toggle, a dropdown, or - number + held between min and max. The default shows dimmed, help is in the
//     tooltip, and fields the mod reads only at load are tagged "after restart".
//   - Choices are saved as their text. Text that matches no option (ignoring case and outer spaces) falls back to the
//     default for that one setting.
//   - Numbers: "1,000" and "1 000" are a thousand; "12,5" is 12.5 where decimals are allowed. Input that reads two ways
//     ("1.000", "0,125") is refused, never guessed.
//   - Saving re-reads the file, keeps keys it doesn't know, and writes atomically (temp file, then replace). A file it
//     couldn't read is never written over.
//   - A status line says when the mod isn't running what's shown (a bad value, a hand edit, a failed apply).
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
    internal enum SettingKind { Bool, Int, Float, Choice }

    internal sealed class SettingField
    {
        internal readonly string Key, Label, Help;
        internal readonly SettingKind Kind;
        internal readonly double Default, Min, Max, Step;
        internal readonly int Decimals;
        internal readonly bool Live;   // true: the mod reads it each time it's used, so a save takes effect at once
        internal readonly string[] Options;   // Choice only: the texts written to the file, in dropdown order

        private SettingField(string key, string label, SettingKind kind, double def, double min, double max, double step,
                             int decimals, bool live, string help, string[] options = null)
        {
            Key = key; Label = label; Kind = kind; Default = def; Min = min; Max = max; Step = step;
            Decimals = decimals; Live = live; Help = help; Options = options;
        }

        internal static SettingField Bool(string key, string label, bool def, bool live, string help) =>
            new SettingField(key, label, SettingKind.Bool, def ? 1 : 0, 0, 1, 1, 0, live, help);

        internal static SettingField Int(string key, string label, int def, int min, int max, int step, bool live, string help) =>
            new SettingField(key, label, SettingKind.Int, def, min, max, Math.Max(1, step), 0, live, help);

        internal static SettingField Float(string key, string label, double def, double min, double max, double step,
                                           int decimals, bool live, string help) =>
            new SettingField(key, label, SettingKind.Float, def, min, max, step > 0 ? step : 1, Math.Max(0, decimals), live, help);

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
    }

    internal enum TypedNumber { Ok, NotANumber, Ambiguous }

    internal sealed class SettingsSection
    {
        internal const int Revision = 8;

        private enum FileState { Missing, Read, Unreadable }

        private readonly string _title, _path, _note;
        private readonly SettingField[] _fields;
        private readonly Action<JObject> _applied;   // after a save: the saved file, so the mod can take its live values
        private readonly Action<string> _log;
        private readonly HashSet<string> _logged = new HashSet<string>();
        private readonly UIState<bool> _expanded = new UIState<bool>(false);
        private readonly double[] _values, _saved;

        private bool _loaded, _confirmReset, _showPath, _redraw;
        private string _stamp;           // the file's size and write time when last read
        private FileState _state = FileState.Missing;
        private string _problem;         // why the file couldn't be read
        private string _status = "";     // the last action's message; cleared when the panel is opened afresh
        private string _editNote;        // the last typed edit's warning, repeated by the Save, Undo or Reset that follows
        private string _behind;          // why the mod isn't running what's shown, until a save it takes puts it right
        private TMP_Text _statusText;    // the status line of the last build, changed in place by edits

        internal SettingsSection(string title, string path, SettingField[] fields, Action<JObject> applied, Action<string> log,
                                 string note = null)
        {
            _title = title; _path = path; _fields = fields ?? new SettingField[0]; _applied = applied; _log = log; _note = note;
            _values = new double[_fields.Length];
            _saved = new double[_fields.Length];
            for (int i = 0; i < _fields.Length; i++) _values[i] = _saved[i] = _fields[i].Default;
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
                if (collapsible) b.AddExpandable(_title, _expanded, Content, ExpandableStyle.SectionHeader);
                else Content(b);
            }
            catch (Exception e)
            {
                LogOnce("build", _title + ": couldn't draw the settings: " + PathClean.CleanText(e.ToString(), WholeCap));
                try { b.AddLabel("<color=#E8A33A>" + _title + ": couldn't show the settings. The log says why.</color>"); } catch { }
            }
        }

        private bool Dirty
        {
            get { for (int i = 0; i < _values.Length; i++) if (_values[i] != _saved[i]) return true; return false; }
        }

        private void Content(UIPanelBuilder b)
        {
            _statusText = null;
            if (!_redraw) _status = "";   // opened afresh, not redrawn after one of its own clicks
            _redraw = false;
            Refresh();
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
                IConfigurableElement row = f.Kind == SettingKind.Bool ? BoolRow(b, k)
                                         : f.Kind == SettingKind.Choice ? ChoiceRow(b, k)
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
                        bool restart = ChangesNeedRestart(true);
                        for (int i = 0; i < _fields.Length; i++) _values[i] = _fields[i].Default;
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
                        bool changed = Dirty || _behind != null, restart = ChangesNeedRestart(false);
                        if (SaveFile(out string error, out bool applied))
                            _status = !applied ? NotApplied
                                : !changed ? "Saved. Nothing had changed."
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
                        _editNote = null;
                        Redraw(b);
                    }));
                    row.Spacer(6f);
                    row.AddButtonCompact("Reset to defaults", () => Guard("ask", () => { _confirmReset = true; _editNote = null; Redraw(b); }));
                });
            }
            _statusText = b.AddLabel(Footnote(StatusShown())).GetComponent<TMP_Text>();
        }

        private const string NotApplied = "Saved, but the mod couldn't take the new settings now, so they take effect after a restart. The log says why.";

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
            return b.AddField(f.Label, b.HStack(row =>
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
            return b.AddField(f.Label, b.HStack(row =>
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

        private IConfigurableElement NumberRow(UIPanelBuilder b, int k)
        {
            SettingField f = _fields[k];
            TMP_InputField input = null;
            return b.AddField(f.Label, b.HStack(row =>
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

        // The buttons move to the next whole step (125 -> 150 -> 175), then stay inside min and max.
        private void Step(int k, int dir, TMP_InputField input)
        {
            SettingField f = _fields[k];
            double cur = _values[k], n = cur / f.Step;
            double next = dir > 0 ? (Math.Floor(n + 1e-9) + 1) * f.Step : (Math.Ceiling(n - 1e-9) - 1) * f.Step;
            Set(k, Clamp(f, next), input);
            _editNote = null;
            Edited("");
        }

        // Applied on losing focus or on Enter, never with a rebuild: a click on Save that took the focus must still
        // land on
        // Save. The field always ends up showing the value that will be saved.
        private void Typed(int k, string text, TMP_InputField input)
        {
            SettingField f = _fields[k];
            double before = _values[k];
            TypedNumber got = ParseTyped(text, f.Places, out double v);
            if (got != TypedNumber.Ok)
            {
                Set(k, before, input);
                Note(got == TypedNumber.NotANumber
                    ? "That isn't a number, so " + f.Label + " stays at " + Format(f, before) + "."
                    : "Can't tell what " + Plain(text) + " means, so " + f.Label + " stays at " + Format(f, before) + ". Type it like "
                      + (f.Places == 0 ? "1000 or 1,000." : "1000, 1,000 or 12.5."));
                return;
            }
            double r = Round(f, v), c = Math.Max(f.Min, Math.Min(f.Max, r));
            Set(k, c, input);
            if (c != r) Note(f.Label + " goes from " + Format(f, f.Min) + " to " + Format(f, f.Max) + ", so it's " + Format(f, c) + ".");
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

        private void Edited(string message) => SetStatus(message);

        private void SetStatus(string s)
        {
            _status = s ?? "";
            if (_statusText != null) _statusText.text = Footnote(StatusShown());
        }

        // True when a change about to be saved is to a field the mod reads only at load (or the mod may not be running
        // what's
        // saved, and some field is read only at load).
        private bool ChangesNeedRestart(bool toDefaults)
        {
            for (int i = 0; i < _fields.Length; i++)
            {
                double target = toDefaults ? _fields[i].Default : _values[i];
                if (!_fields[i].Live && (target != _saved[i] || _behind != null)) return true;
            }
            return false;
        }

        private void BuildUnreadable(UIPanelBuilder b)
        {
            b.AddLabel("<color=#E8A33A>settings.json can't be read, so it's left alone: <noparse>" + PathClean.ForScreen(_problem ?? "unknown") + "</noparse></color>");
            b.AddLabel(Footnote("Until it's fixed and saved here, or the game restarts, the mod keeps what it started with (its defaults, if the file was already like this). Fix or delete it, then press Check again."));
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
            string odd = null;
            bool oddChoice = false;
            bool moved = false;
            for (int i = 0; i < _fields.Length; i++)
            {
                double v = ValueIn(obj, _fields[i], ref odd, ref oddChoice);
                moved |= v != _saved[i];
                _values[i] = _saved[i] = v;
            }
            if (!first && _state != FileState.Unreadable && (moved || _behind != null))
                _behind = "settings.json changed outside the game; this shows it as it is now. The mod keeps what it had until you Save here or restart.";
            if (odd != null) _behind = odd;
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

        private JObject ReadFile(out FileState state, out string problem)
        {
            problem = null;
            try
            {
                if (!File.Exists(_path)) { state = FileState.Missing; return null; }
                string text = File.ReadAllText(_path);
                if (string.IsNullOrWhiteSpace(text)) { state = FileState.Read; return new JObject(); }   // the mods read an empty file as the defaults
                if (JToken.Parse(text) is JObject o) { state = FileState.Read; return o; }
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

        // Reads each value as the mod's own read (JsonConvert.DeserializeObject into its Settings class) would.
        //   - A token that read can't convert (300.0 or 1e3 in a whole-number field, true in a number field, text that
        //     isn't one) makes the mod start on ALL its defaults: the field shows its default and `odd` names the key.
        //   - A choice is a text matching one of its options (trimmed, any case). Anything else shows the default, and
        //     the mod uses the default for that setting alone.
        //   - The status line names the first odd value, except that a bad toggle or number replaces a bad choice
        //     (oddChoice), so it never understates what the mod is running.
        private static double ValueIn(JObject obj, SettingField f, ref string odd, ref bool oddChoice)
        {
            if (obj == null || !obj.TryGetValue(f.Key, out JToken t) || t.Type == JTokenType.Null) return f.Default;
            try
            {
                switch (f.Kind)
                {
                    case SettingKind.Bool:
                        if (t.Type == JTokenType.Boolean || t.Type == JTokenType.Integer || t.Type == JTokenType.String) return t.ToObject<bool>() ? 1 : 0;
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
                    default:
                        if (t.Type == JTokenType.Integer || t.Type == JTokenType.Float || t.Type == JTokenType.String)
                        {
                            double v = t.ToObject<float>();
                            if (!double.IsNaN(v) && !double.IsInfinity(v)) return Round(f, v);
                        }
                        break;
                }
            }
            catch { }
            if (odd == null || (oddChoice && f.Kind != SettingKind.Choice))
            {
                oddChoice = f.Kind == SettingKind.Choice;
                string raw = t.ToString(Formatting.None);
                if (raw.Length > 24) raw = raw.Substring(0, 24) + "...";
                odd = f.Kind == SettingKind.Choice
                    ? "settings.json has " + f.Key + " = " + Plain(raw) + ", which isn't one of " + Plain(string.Join(", ", f.Options))
                      + ", so the mod uses the default for this setting (" + Plain(f.OptionAt(f.Default)) + "). Save puts the file right."
                    : "settings.json has " + f.Key + " = " + Plain(raw) + ", which the mod can't read as "
                      + (f.Kind == SettingKind.Bool ? "on or off (true or false)" : f.Kind == SettingKind.Int ? "a whole number" : "a number")
                      + ", so with this file it starts on all its defaults. Save puts the file right.";
            }
            return f.Default;
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
            JObject o = disk ?? new JObject();
            for (int i = 0; i < _fields.Length; i++) o[_fields[i].Key] = Token(_fields[i], _values[i]);
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
            Array.Copy(_values, _saved, _values.Length);
            _state = FileState.Read;
            _stamp = Stamp();
            _loaded = true;
            try { _applied?.Invoke(o); applied = true; }
            catch (Exception e) { LogOnce("apply", _title + ": saved settings.json, but couldn't apply it until a restart: " + PathClean.CleanText(e.ToString(), WholeCap)); }
            _behind = applied ? null : NotApplied;
            return true;
        }

        private static JToken Token(SettingField f, double v)
        {
            switch (f.Kind)
            {
                case SettingKind.Bool: return new JValue(v != 0);
                case SettingKind.Int: return new JValue((long)Round(f, v));
                case SettingKind.Choice: return new JValue(f.OptionAt(v));
                default: return new JValue(Round(f, v));
            }
        }

        // Half away from zero, the way people round (Math.Round alone rounds 2.5 to 2).
        private static double Round(SettingField f, double v) => Math.Round(v, f.Places, MidpointRounding.AwayFromZero);

        private static double Clamp(SettingField f, double v) => Math.Max(f.Min, Math.Min(f.Max, Round(f, v)));

        private static string Format(SettingField f, double v) =>
            f.Places == 0 ? ((long)Round(f, v)).ToString(CultureInfo.InvariantCulture)
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
                : " From " + Format(f, f.Min) + " to " + Format(f, f.Max) + ", default " + Format(f, f.Default) + ".";
            return f.Help + range + (f.Live ? " Takes effect when you save." : " Takes effect after a restart.");
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
    // THE SHARED CONTRACT (frozen: every copy of this file, of any revision, keeps it exactly)
    //   - AppDomain.CurrentDomain.GetData("ashenmare.preferences-mods.v1") is a List<Action<UIPanelBuilder>>, one entry
    //     per loaded mod, in load order.
    //   - Each mod adds its entry when it loads and removes it when it unloads.
    //   - In the game's PreferencesBuilder.BuildTabs, only the mod whose entry is first adds the tab ("Mods", id
    //     "ashenmare.mods"). The tab calls every entry in turn, each inside its own try.
    //   - Nothing else crosses between mods.
    internal static class PreferencesModsTab
    {
        private const string SlotKey = "ashenmare.preferences-mods.v1";
        private static Action<UIPanelBuilder> _mine;
        private static Action<string> _log;
        private static bool _loggedPostfix, _loggedEntry;

        // Patches PreferencesBuilder.BuildTabs on your Harmony instance (its UnpatchAll undoes it) and adds draw to the
        // shared
        // list. Returns false, with one log line, when this game version has no such method. Safe to call again without
        // an unload: the postfix goes on once (a second would add a second Mods tab) and the previous entry is
        // replaced.
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
                lock (list) list.Add(draw);
                return true;
            }
            catch (Exception e) { Log("couldn't add the settings to Preferences > Mods: " + PathClean.CleanText(e.Message)); return false; }
        }

        // True when this copy's postfix is already on the method (this assembly's own MethodInfo, so another mod's copy
        // never
        // matches).
        private static bool Patched(MethodBase target, MethodInfo postfix)
        {
            Patches info = Harmony.GetPatchInfo(target);
            if (info == null) return false;
            foreach (Patch p in info.Postfixes) if (p.PatchMethod == postfix) return true;
            return false;
        }

        // Takes this mod's entry out (on unload, and on any path where the mod gives up after Add). The patch itself
        // goes with
        // the mod's own UnpatchAll.
        internal static void Remove()
        {
            try
            {
                List<Action<UIPanelBuilder>> list = Slot(false);
                if (list != null && _mine != null) lock (list) list.Remove(_mine);
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
                List<Action<UIPanelBuilder>> list = Slot(false);
                Action<UIPanelBuilder>[] entries;
                if (list == null) return;
                lock (list) entries = list.ToArray();
                foreach (Action<UIPanelBuilder> entry in entries)
                {
                    try { entry(s); }
                    catch (Exception e)
                    {
                        if (!_loggedEntry) { _loggedEntry = true; Log("a mod's settings section failed to draw: " + PathClean.CleanText(e.Message)); }
                    }
                }
            });
        }

        private static void Log(string message)
        {
            try { _log?.Invoke(message); } catch { }
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
