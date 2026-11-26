# mdedit

A terminal viewer for Markdown files. It shows the file rendered: headings, lists, quotes, tables, code blocks and links
look as they will read, and the source is one step away. You move a cursor through the rendered text, select, search and
copy. Editing comes later; for now mdedit only reads the file.

Runs on Linux and Windows. Built from this repository alongside cmdr; see the main [README](../README.md#install) for the .NET 10
SDK and the Terminal.Gui submodule.

```sh
dotnet run --project src/Mdedit -- path/to/notes.md
dotnet publish src/Mdedit -c Release -r win-x64      # or linux-x64 → mdedit(.exe)
```

`./publish.ps1` publishes cmdr, logvu, cfgedit and mdedit for the current platform and puts them on the PATH (see the README).

## Usage

```sh
mdedit <file>
mdedit
mdedit --version
```

`mdedit <file>` opens the file. `mdedit` alone shows the recently used files: Enter opens the selected one, a number
selects by position, Ctrl+O opens a file through a file dialog, and Ctrl+Q quits. A file that cannot be read is reported
as `mdedit: <path>: <message>` and mdedit exits with status 1; a usage error exits with status 2.

Every file opened is remembered in `%LOCALAPPDATA%\mdedit\recent.json` on Windows and `~/.local/share/mdedit/recent.json`
on Linux (`$XDG_DATA_HOME/mdedit/recent.json` when set).

## Screen

```
╭┤install.md├──────────────────────────────────────────────╮
│▾ Install                                                 │
│                                                          │
│  Run the installer, then open a new terminal.            │
│                                                          │
│▾ Linux                                                   │
│  • Install with the package manager                      │
│  • Add the binary to your PATH                           │
╰──────────────────────────────────────────────────────────╯
Install › Linux                                          All
/ Find  y Copy  Space Fold  ^O Open                    ^Q Quit
```

The message line shows, on the left, the path of the headings above the top line, and, on the right, the scroll
position: `Top`, `Bot`, `All` or a percentage. A message (such as `Copied text`) replaces the path for a few seconds
or until the next key. The last line lists the main keys.

## Keys

| Key | |
|---|---|
| Up/Down, Left/Right | Move the caret through the rendered text. The caret is a bar before a character. Right from a paragraph's last character goes to the end of the paragraph, after that character, and Right again to the next paragraph; Left goes back the same way |
| Home/End | First cell of the line / last cell of the line. On the last line of a paragraph, End goes to the end of the paragraph, after its last character |
| PgUp/PgDn | Page up / down, with the caret |
| g / G, Ctrl+Home/End | First cell of the document / the end of the last paragraph |
| Shift with a movement key | Extend the selection. The selection is the text from where the movement started up to the caret, not including the character the caret is before: Shift+Right three times selects three characters |
| `/`, Ctrl+F | Find in the rendered text. Enter goes to the first match after the cursor; Esc closes the bar |
| n, F3 / N, Shift+F3 | Next / previous match, wrapping at the ends with a message |
| `y`, Ctrl+C | Copy the selection as plain text, or the block under the cursor when nothing is selected |
| `Y` | Copy the selection, or the block under the cursor, as Markdown |
| Space | Fold or unfold the section or code block under the caret (see Folding) |
| `*` / `-` | `*` unfolds the section or code block under the caret and everything inside it; `-` folds the innermost section around the caret |
| `1`–`6` / `0` | Show the headings down to that level, folding the deeper sections; `0` unfolds everything |
| `<` / `>` | In a table, scroll the table by eight columns to the left / right (see Tables) |
| Shift+F10 | Open the menu at the cursor |
| Ctrl+O | Open another file |
| Ctrl+Q | Quit |

Any movement without Shift clears the selection. Copying writes the text to the clipboard through the terminal (OSC 52),
so the terminal must allow it. A copy over 1,000,000 bytes is not written, and the message says so.

## Mouse

| Action | |
|---|---|
| Wheel | Scroll three lines; the cursor stays where it is |
| Shift+wheel, or the horizontal wheel | Over a table, scroll it by eight columns (see Tables); elsewhere, the wheel |
| Click on a table's `◂` / `▸` | Scroll the table by eight columns toward that side; the caret stays where it is |
| Click on a `▾` / `▸` glyph, or on a `… n lines` summary | Fold or unfold that section or code block; a click on the heading's text places the caret as usual |
| Click | Place the caret before the character under the mouse, and clear the selection. A click past the text of a paragraph's last line puts the caret at its end |
| Drag | Select from the press to the caret under the mouse; the selection stops before that character. Above or below the view, the text scrolls one line for each move |
| Double-click | Select the word under the mouse, from its first character to its last (nothing on a space) |
| Right-click | Open the menu: Copy text, Copy Markdown, and Fold or Unfold for the section or code block at the mouse (when there is one). Choose with the mouse, or with the arrow keys and Enter |

A right-click inside the selection keeps it; outside, it moves the cursor there, as a click does.

## Tables

A table wider than the window scrolls sideways on its own: the rest of the document stays where it is. Its first columns
show, and a dim `▸` in the last column says that more follows to the right (a `◂` in the first column says so for the left).
Each table keeps its own offset, so scrolling one table does not move another.

- `>` and `<` scroll the table under the caret by eight columns.
- Shift+wheel, or the horizontal wheel, scrolls the table under the mouse by eight columns.
- A click on a `◂` or `▸` scrolls the table by eight columns toward that side.
- Moving the caret into a column that is out of view scrolls the table by the least it takes to show the caret's cell.
- A click or a drag on a scrolled table selects the cells that are drawn under the mouse.

The container of a table in a quote or a list stays in place while the table scrolls. A table that fits has no offset.
Code blocks do not scroll: a long line in a code block wraps.

## Code

Fenced code blocks are coloured by token: keywords, strings, numbers, comments, types, function names, operators and
so on. The language comes from the first word of the fence's info string, case-insensitively, and the aliases are:

| Language | Info strings |
|---|---|
| C# | `csharp`, `cs`, `c#` |
| Go | `go`, `golang` |
| Python | `python`, `py` |
| TypeScript | `typescript`, `ts`, `tsx` |
| JavaScript | `javascript`, `js`, `jsx`, `mjs`, `cjs` |
| HTML | `html`, `htm` |
| CSS | `css` |
| XML | `xml`, `xaml`, `csproj`, `svg` |
| JSON | `json`, `jsonc`, `json5` |
| Shell | `shell`, `sh`, `bash`, `zsh` |
| PowerShell | `powershell`, `ps1`, `pwsh` |

Other languages are coloured when the highlighter has a grammar for their name; a block with an unknown language is shown
plain. Colouring uses the TextMate grammars of the Terminal.Gui fork, which need the native Oniguruma library
(`onigwrap`) that ships with the application: without it, all code is shown plain. The code keeps its colours when a
line wraps.

## Folding

Sections (a heading and everything up to the next heading of the same or a higher level) and code blocks fold. A folded
heading shows `▸` and `… n lines`, the number of source lines hidden; a folded code block shows its language and the
number of its code lines. Unfolded headings show `▾`.

| Key | |
|---|---|
| Space | Fold or unfold the section or code block whose heading or header line the caret is on. Elsewhere, the innermost section (or code block) around the caret. A caret inside what folds moves to the heading |
| `*` | Unfold the section or code block at the caret and every section and code block inside it |
| `-` | Fold the innermost section around the caret |
| `1`–`6` | Fold every section whose heading is deeper than the digit and unfold the others, so that the headings down to that level show. Code blocks keep their state |
| `0` | Unfold everything |

Folding keeps the caret's row on the screen when the fold is above it. Search unfolds what hides a match before it selects
the match, and the search still finds text in folded content. Folds stay when the window is resized.

With the mouse, a click on a `▾` or `▸` glyph, or on a `… n lines` summary, folds or unfolds that unit, and the right-click
menu has Fold or Unfold for the unit at the mouse.
