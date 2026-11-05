// PathClean.cs (Revision 8): the path cleaner from SettingsSection.cs, on its own, for a mod with no settings editor.
// Copy this file into your mod and change only the namespace line. A mod that carries SettingsSection.cs already has
// this class, so don't add both (two in one namespace won't compile). It needs only System and System.Text. A change to
// it is a new SettingsSection Revision.

using System;
using System.Text;

namespace SETTINGS_NAMESPACE
{
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
