# cfgedit

A terminal editor for configuration files that shows the file as a tree and edits one key, value, element or
attribute at a time. You type the value as it should be; cfgedit writes it with the right quoting and escaping. Everything
you did not edit stays byte-for-byte as it was: comments, blank lines, indentation, quote styles, number spelling,
encoding, byte order mark and line endings.

Phase 1 reads and writes XML, JSON, JSON with comments (JSONC) and JSON5. Runs on Linux and Windows. Built from this
repository alongside cmdr; see the main [README](../README.md#install) for the .NET 10 SDK and the Terminal.Gui submodule.

```sh
dotnet run --project src/Cfgedit -- path/to/appsettings.json
dotnet publish src/Cfgedit -c Release -r win-x64      # or linux-x64 → cfgedit(.exe)
```

`./publish.ps1` publishes cmdr, logvu and cfgedit for the current platform and puts them on the PATH (see the README).

## Usage

```sh
cfgedit <file> [--format xml|json|jsonc|json5]
cfgedit --check <file> [--format …]
cfgedit
cfgedit --version
```

The format comes from the extension: `.json` and `.jsonc` are JSONC (comments and trailing commas are accepted when the
file has them, and never added), `.json5` is JSON5, and `.xml .config .csproj .props .targets .xaml .resx .nuspec .xsd
.xslt .svg` are XML. Any other file is recognised by its first character (`<` is XML, `{` or `[` is JSON5). `--format`
overrides this; `--format json` is strict JSON and refuses comments.

A path that does not exist asks whether to create it; a new XML file asks for the name of its root element. The file is
written on the first save.

`cfgedit --check <file>` only parses the file: it prints nothing and exits 0 when the file is valid, and prints
`path:line:col: message` and exits 1 when it is not.

Every file opened is remembered in `%LOCALAPPDATA%\cfgedit\recent.json` on Windows and `~/.local/share/cfgedit/recent.json`
on Linux (`$XDG_DATA_HOME/cfgedit/recent.json` when set). `cfgedit` without arguments shows that list: Enter opens the
selected file, a number selects by position, Del removes an entry, `o` or Ctrl+O opens a file through a file dialog, Esc
or Ctrl+Q quits.

## Screen

```
╭┤web.config [xml]├───────────────────────────────────────────────╮
│  <?xml version="1.0" encoding="utf-8"?>                         │
│▾ <configuration>                                                │
│    <!-- application settings -->                                │
│  ▾ <appSettings>                                                │
│      <add key="Environment" value="Production"/>                │
│      <add key="ConnectionTimeout" value="30"/>                  │
│    </appSettings>                                               │
│  ▾ <system.web>                                                 │
│      <compilation debug="false" targetFramework="4.8"           │
│                   batch="true"/>                                │
╰─────────────────────────────────────────────────────────────────╯
/configuration/appSettings/add[1]/@value  attribute  Production
Enter Edit  ^S Save  / Find  a/i Add  d Del  t Type  ^Z Undo  …^Q Quit
```

Each row has one or more parts: a JSON key and its value, an XML element name and each attribute's name and value, a
text value, a comment. The cursor is on one part at a time, drawn in reverse video. The line under the tree shows the
part's path (`$.Servers[1].port` for JSON, an XPath-like path for XML), its kind and its full value; messages such as
"Saved" or an error appear there too. The last line lists the main keys.

Values are shown as they are, without quotes or escapes: a line break shows as `⏎`, a tab as `→`, other control
characters as `·`. An empty JSON string shows as a dim `""`. XML attributes that do not fit on the line wrap under the
first attribute. A title ending in `*` means there are unsaved changes.

## Keys

| Key | |
|---|---|
| Up/Down, k/j | Previous / next row |
| Left/Right, h/l | Previous / next part on the row; Left on the first part goes to the parent |
| Home/End, 0/$, PgUp/PgDn, g/G | First / last part, page, first / last row |
| Space | Fold or unfold; on a leaf, fold its parent |
| `*` / `-` | Unfold the whole subtree / fold the parent |
| Enter, F2 | Edit the part. On `true`, `false` or `null`, choose the type instead |
| Tab, Shift+Tab | Next / previous editable part |
| `a` / `i` | Add after / before. On a container's `{n}` or `[n]`, add inside it (`a` last, `i` first). An empty XML element shows `{0}` after its tag, and `a` / `i` on it add a child. On the `$` marker of a JSON document, add a comment after / before the root. Next to a similar sibling the new entry copies its shape (`<add key="" value=""/>`); otherwise a dialog asks what to add |
| `A` / `I` | Add a child last / first, from any part of an XML element or a JSON object or array |
| `+` | Add an attribute to the element |
| `d`, Delete | Delete the node, or the attribute under the cursor (undo restores it) |
| Alt+Up/Down, K/J | Move the node among its siblings, with its comments |
| Alt+Left/Right | Move the attribute |
| `t` | Change a JSON value's type |
| Ctrl+D | Duplicate the node |
| `T` | Open the table editor |
| `/`, Ctrl+F | Find in keys, names and values; n / F3 next, N / Shift+F3 previous. Enter goes to the next match; Esc closes the search |
| `y` / `Y` | Copy the value / the path to the clipboard |
| Ctrl+Z, `u` / Ctrl+Y, Ctrl+R | Undo / redo |
| Ctrl+T | Switch between the tree and the text |
| Ctrl+S | Save |
| Ctrl+O | Open another file |
| Ctrl+Q | Quit |

**Editing a value.** Enter opens a field over the part with its current value. Enter saves the change into the document,
Esc cancels, Tab saves and moves to the next part. If the value is not valid (a number that is not a number, a key that
already exists, a name XML does not allow), the field stays open and the message says why. A value with line breaks opens
in a multi-line editor (F4 switches to it from the field); OK or F2 commits, Esc cancels. Nothing is written to disk
until Ctrl+S.

**Saving.** Ctrl+S writes the file in place, keeping its permissions and owner. If the file was changed by someone else
since it was opened, cfgedit asks whether to overwrite it, reload it, or cancel. Quitting or opening another file with
unsaved changes asks whether to save, discard or cancel; the default is Save.

## Table editor

`T` on a list shows it as a table: a JSON array whose items are all objects (one column per key) or all plain values, or
an XML element with repeated child elements of the same name (one column per attribute, per single text-only child
element, and for the text). Esc returns to the tree.

| Key | |
|---|---|
| Arrows, hjkl | Move between cells |
| Enter, F2 | Edit the cell; editing an empty cell (`·`) adds that key or attribute to the row |
| `d` | Clear the cell (remove the key or attribute); on the `#` column, delete the row |
| `a` / `i` | Add a row after / before, shaped like the current one |
| Alt+Up/Down | Move the row |
| Enter on `{n}`, `[n]`, `<…>` | Open that nested value in the tree |

## Text mode

A file that does not parse opens as plain text, with the cursor at the error and the error shown under it. Fix it and
press F5 to see the tree. Ctrl+T switches between the tree and the text at any time, keeping the cursor on the same place.
Ctrl+S on text that does not parse asks before saving it anyway.

Text mode is read-only when the file mixes line endings (for example CRLF and LF), because the text editor would change
them.

## What is kept

cfgedit edits the file's text, not a reformatted copy of it. Saving without changes writes the same bytes. An edit
changes only the edited value or name; a new entry copies the indentation, quote style and separators of its neighbours;
a deleted entry takes its comments with it. Every edit is checked: the new text is parsed again and must contain exactly
the intended change, so a fault in cfgedit can refuse an edit but never corrupt the file.

XML is checked by .NET's XML reader. A value containing an entity declared in the file's DOCTYPE is shown and edited as
raw text (marked `raw`). Files whose entities contain markup open in text mode only.
