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
/ Find  y Copy  ^O Open                                ^Q Quit
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
| Shift+F10 | Open the menu at the cursor |
| Ctrl+O | Open another file |
| Ctrl+Q | Quit |

Any movement without Shift clears the selection. Copying writes the text to the clipboard through the terminal (OSC 52),
so the terminal must allow it. A copy over 1,000,000 bytes is not written, and the message says so.

## Mouse

| Action | |
|---|---|
| Wheel | Scroll three lines; the cursor stays where it is |
| Click | Place the caret before the character under the mouse, and clear the selection. A click past the text of a paragraph's last line puts the caret at its end |
| Drag | Select from the press to the caret under the mouse; the selection stops before that character. Above or below the view, the text scrolls one line for each move |
| Double-click | Select the word under the mouse, from its first character to its last (nothing on a space) |
| Right-click | Open the menu: Copy text, Copy Markdown. Choose with the mouse, or with the arrow keys and Enter |

A right-click inside the selection keeps it; outside, it moves the cursor there, as a click does.
