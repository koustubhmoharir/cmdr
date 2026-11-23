# cfgedit

A terminal editor for configuration files that shows the file as a tree and edits one key, value, element or
attribute at a time. You type the value as it should be; cfgedit writes it with the right quoting and escaping. Everything
you did not edit stays byte-for-byte as it was: comments, blank lines, indentation, quote styles, number spelling,
encoding, byte order mark and line endings.

Reads and writes XML, JSON, JSON with comments (JSONC), JSON5, `.env` files (dotenv), TOML and YAML files. Runs on Linux and Windows. Built from this
repository alongside cmdr; see the main [README](../README.md#install) for the .NET 10 SDK and the Terminal.Gui submodule.

```sh
dotnet run --project src/Cfgedit -- path/to/appsettings.json
dotnet publish src/Cfgedit -c Release -r win-x64      # or linux-x64 → cfgedit(.exe)
```

`./publish.ps1` publishes cmdr, logvu and cfgedit for the current platform and puts them on the PATH (see the README).

## Usage

```sh
cfgedit <file> [--format xml|json|jsonc|json5|env|toml|yaml]
cfgedit --check <file> [--format …]
cfgedit
cfgedit --version
```

The format comes from the extension: `.json` and `.jsonc` are JSONC (comments and trailing commas are accepted when the
file has them, and never added), `.json5` is JSON5, and `.xml .config .csproj .props .targets .xaml .resx .nuspec .xsd
.xslt .svg` are XML. A file named `.env`, `.env.<anything>` (`.env.local`) or `<anything>.env` is a `.env` file. A known
extension wins, so `.env.json` is JSONC. A `.toml` file is TOML 1.0 (`.env.toml` is TOML too, as the extension wins). A `.yaml`
or `.yml` file is YAML (see [YAML files](#yaml-files)). Any other file is recognised by its first character (`<` is XML, `{`
or `[` is JSON5). `--format` overrides this; `--format json` is strict JSON and refuses comments.

A path that does not exist asks whether to create it; a new XML file asks for the name of its root element, and a file
whose format is not known from its name asks for the format (`.env`, `.toml` and `.yaml` are among the choices). The file is written
on the first save.

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
a/i Add  d Dup  t Type  / Find  y Copy  ^T Text  Tab Next  ^O Open  ^Z U…^Q Quit
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
| Delete | Delete the node, or the attribute under the cursor (undo restores it) |
| Alt+Up/Down, K/J | Move the node among its siblings, with its comments |
| Alt+Shift+Up/Down | Move the node into the object, array or element just above it (as its last child) / just below it (as its first child). When the node is first / last in its parent, it moves out, before / after the parent. Next to a value that holds nothing, it moves one step, like Alt+Up/Down. An array item going into an object asks for its key; a member going into an array loses its key; a key already taken there gets the next number (`port2`) |
| Alt+Left/Right | Move the attribute |
| `t` | Change a JSON value's type. Changing to array wraps the value: `"a": 1` becomes `"a": [1]` (a null becomes `[]`) |
| `d` | Duplicate the node. The copy goes right after it, with its attached comments, and the cursor moves to the copy. A copied key gets a number: `port` gives `port2`, `server1` gives `server2`, then the next free number |
| `T` | Open the table editor |
| `/`, Ctrl+F | Find in keys, names and values; n / F3 next, N / Shift+F3 previous. Enter goes to the next match; Esc closes the search |
| `y` / `Y` | Copy the value / the path to the clipboard |
| Ctrl+Z, `u` / Ctrl+Y, Ctrl+R | Undo / redo |
| Ctrl+T | Switch between the tree and the text |
| Ctrl+S | Save |
| Ctrl+O | Open another file |
| Ctrl+Q | Quit |

**Editing a value.** Enter opens a field over the part with its current value, as wide as the value (at least 10
columns); it grows as you type. Enter saves the change into the document, Esc cancels, Tab saves and moves to the next part. If the value is not valid (a number that is not a number, a key that
already exists, a name XML does not allow), the field stays open and the message says why. A value with line breaks opens
in a multi-line editor (F4 switches to it from the field); OK or F2 commits, Esc cancels. Nothing is written to disk
until Ctrl+S.

**Saving.** Ctrl+S writes the file in place, keeping its permissions and owner. If the file was changed by someone else
since it was opened, cfgedit asks whether to overwrite it, reload it, or cancel. Quitting or opening another file with
unsaved changes asks whether to save, discard or cancel; the default is Save.

## Table editor

`T` on a list shows it as a table: a JSON array whose items are all objects (one column per key) or all plain values, a TOML
array of tables (`[[items]]`) or inline array whose items are all inline tables or all plain values, or an XML element
with repeated child elements of the same name (one column per attribute, per single text-only child element, and for the
text). In TOML, the rows are the items, and the columns are their keys in the order they first appear; a subtable or an
array in a cell shows its count, `{n}` or `[n]`. A YAML sequence whose items are all mappings, or all scalars, opens the same way:
the rows are the items, the columns are the keys in order of first appearance, and a nested mapping or sequence shows its count.
An alias in a cell shows its name (`*b`) and is read-only. Esc returns to the tree.

| Key | |
|---|---|
| Arrows, hjkl | Move between cells |
| Enter, F2 | Edit the cell; editing an empty cell (`·`) adds that key or attribute to the row |
| Delete | Clear the cell (remove the key or attribute); on the `#` column, delete the row |
| `d` | Duplicate the row: the copy goes right after it, and the cursor moves to the copy in the same column |
| `a` / `i` | Add a row after / before, shaped like the current one |
| Alt+Up/Down | Move the row |
| Enter on `{n}`, `[n]`, `<…>` | Open that nested value in the tree |

## Text mode

A file that does not parse opens as plain text, with the cursor at the error and the error shown under it. Fix it and
press F5 to see the tree. Ctrl+T switches between the tree and the text at any time, keeping the cursor on the same place.
Ctrl+S on text that does not parse asks before saving it anyway.

Text mode shows the file's text exactly as it is, line endings included, and saving writes back only what you changed.
Enter and paste insert the file's own line ending (CRLF in a CRLF file). A file that mixes line endings is editable
too: its other lines keep theirs, and new line breaks use the one the file uses most. Tab inserts a tab character, drawn
four columns wide.

## `.env` files

Each line is `NAME=value`, with the usual comments, `export` and quotes. A name is split on `__`, as ASP.NET Core splits
its environment configuration, so `Logging__LogLevel__Default` is shown as `Logging` › `LogLevel` › `Default`. A group is
every name that shares a prefix, and it shows where its first entry is, with all its entries below it in file order.
Grouping is case-sensitive, and the lines of a group do not have to be next to each other.

- **Values** are written in the plainest form that every common loader reads the same way: unquoted, else in single
  quotes, else in double quotes. An edit keeps the quote style when it still fits. `${VAR}` is shown and kept as written;
  cfgedit never expands it. A value in double quotes that contains `$` or `\` shows a note, because loaders may expand
  `$` or read `\` differently there.
- **Renaming** a group renames every key under it, as one undo step. A new name may not add or remove a `__`, and may not
  merge with a sibling.
- **Adding** inside a group writes `Group__Key=`. The dialog offers an entry (its key, with an empty value) or a comment.
- **Deleting** an entry deletes its line and the comments shown with it. Alt+Up/Down moves an entry among its siblings;
  the move is refused when either sibling's lines are not next to each other. Alt+Shift+Up/Down moves an entry or a group
  into the group above / below, or out of its group, by renaming its keys (`A__x` moving into `A__B` becomes `A__B__x`).
- **Types and tables:** every value is a string, so `t` says so and `T` has no table editor for `.env`.

## TOML files

A TOML table is shown by its meaning, not by how the text spells it: `[a]`, `[a.b]`, `a.b.c = 1` and an implicit `a` made by
`[a.b]` are one table `a` with its members, and a table's members are listed in the order they first appear. An array of
tables (`[[items]]`) is an array whose items are tables; an inline array or inline table is shown the same way. Scalars show
their value as JSON scalars do (strings without quotes), and the detail line names the TOML kind: `string`, `integer`,
`float`, `boolean`, `offset date-time`, `local date-time`, `local date` or `local time`.

- **Values** are written as typed for numbers and dates. A string keeps its form when that form can hold the new text
  (literal, or multi-line for a value with line breaks); otherwise it is written in basic quotes.
- **Renaming** a table renames its key in every place that names it, including headers below it (`[a.b]`) and dotted keys,
  as one undo step. A rename onto an existing sibling is refused.
- **Adding** `a` on a table, or on the root, offers string, integer, float, boolean, date-time (asks for its text), table,
  array and comment, and asks for a key. Where the text goes follows the table: a key/value after the table's last one in
  its section, a `[parent.key]` header for a new table in a header-defined table, a dotted key in a dotted table. `a` on an
  item of an array of tables copies its shape, with empty values.
- **Types:** `t` opens the type menu (string, integer, float, boolean, date-time, table, array). A date-time asks for its
  text. Changing to array wraps the value (`a = 1` becomes `a = [1]`); a table with children changed to anything else asks
  before they are dropped. A table defined by a header or by dotted keys cannot change type: delete it instead.
- **Deleting** a table deletes every piece that defines it, its subtables included. Alt+Up/Down moves a key/value, an item or
  a sibling header section; a move is refused when the units are not next to each other.
- **Moving into and out of tables:** Alt+Shift+Up/Down moves a key/value into the table above / below or out of its table, and
  items and members between inline arrays and inline tables. A key/value always goes before the table's subtable headers. A
  header table moves with its subtables, and their headers are renamed (`[a.b]` moving into `x` becomes `[x.b]`). Refused,
  with a message: tables defined by dotted keys or only by their subtables, a header table going into an inline table or an
  array, and an entry with comments going into an inline table or array.
- **Tables:** `T` on an array of tables, or on an inline array of inline tables or of plain values, opens it as a table
  (see Table editor). A table cell is edited like a value, and a missing cell is added with the most common kind of its
  column. `a`, `i`, `d` and Alt+Up/Down add, duplicate and move rows as they do in JSON, and Delete deletes a row.
- **Text mode:** a file that is not valid TOML 1.0 opens in text mode at the error. Two valid constructs that the second
  parser (Tomlyn) misreads are refused with a message and open in text mode: a `[x.sub]` header, or a new `[[x]]` item,
  when another table follows an array of tables `[[x]]`.

## YAML files

cfgedit reads the config subset of YAML 1.2: block and flow mappings and sequences (including sequences that are not indented
under their key), the five scalar styles (plain, single and double quoted, literal `|` and folded `>`), comments, `---` and `...` markers
with several documents, `%YAML` and `%TAG` directives, anchors, aliases, tags and merge keys (`<<: *base`). A file that uses
anything outside this subset, or that YAML itself rejects, opens in text mode at the error, with the reason. The reasons
include complex keys (`? `), keys that are collections, aliases or block scalars, and keys that span lines. A few valid
shapes that YamlDotNet, the second parser cfgedit checks against, reads differently are refused too: a tab on a blank line
between entries, a block scalar whose content ends in spaces or blank lines with no final line break, and a block scalar without
`-` chomping that ends the file
with no line break. The message for the last one says to add a line break at the end of the file, and adding it lets the
file open as a tree.

- **Rows:** a file with one document shows that document's root as `$`. A file with several documents shows each one as a
  row `---[0]`, `---[1]` and so on. A mapping member is `key: {n}` and a sequence member `key: [n]`, and an item is `0: …`.
  A scalar shows its value without quotes, an empty string as `""` and a null with no text as `null`. The detail line
  names the YAML kind: `null`, `boolean`, `integer`, `float` or `string` (quoted and block scalars are strings).
- **Anchors, tags and aliases** are shown on their row, between the key and the value (`base: &b {3}`, `port: !!str 80`,
  `use: *b`). They cannot be edited. An alias is shown as written and is not expanded. A merge key is an ordinary member
  whose value is an alias.
- **Values** keep their style when the style can hold the new text. A plain value that is changed to `true` is written
  `'true'` (single quotes), so that it stays a string. A value with line breaks is written as a literal block. A new empty
  string is written `''`, and the text typed into it keeps that style.
- **Keys** are written plain when that is safe and quoted when it is not. A rename onto an existing sibling is refused.
- **Adding:** `a` on a mapping adds a `key: value` member after the entry it follows, with the key the dialog asks for, and
  `a` on a sequence adds a `- value` item. The kinds are string, number, boolean, null, mapping, sequence and comment. A
  new item next to a sequence item copies its shape, so a sequence of mappings gets a compact `- name: ''` item. Adding to
  an empty `{}` or `[]` turns it into block style. An empty `{}` that is a sequence item becomes a compact mapping on the
  dash line. Flow collections take JSON-style commas.
- **Types:** `t` opens the type menu (string, number, boolean, null, mapping, sequence). Changing to sequence wraps the value:
  `a: 1` becomes `a:` with the item `- 1` below it (in a flow collection, `[1]`); a null becomes `[]`. A mapping or sequence
  with children changed to anything else asks before they are dropped. An alias can only be wrapped in a sequence. A block
  scalar with an indentation indicator (`|2`) cannot be wrapped.
- **Deleting** a node deletes it with its comments. An anchor that an alias still refers to cannot be deleted; the message
  names the alias, as in `*b refers to it`.
- **Moving:** Alt+Up/Down moves an entry or an item among its siblings. A document can be moved the same way, and a
  document can be deleted, but adding a document is refused (`adding a document is not supported`). Alt+Shift+Up/Down moves a
  node into the mapping or sequence above / below, or out of its parent, within its document; a member going into a sequence
  becomes a `- ` item and an item going into a mapping asks for its key. Refused, with a message: a block value going into a
  flow collection, a plain scalar that flow syntax would read differently (quote it first), a merge key (`<<`) going into a
  sequence, and a move after which an alias would come before its anchor or refer to another anchor.
- **Tables:** `T` on a sequence whose items are all mappings, or all scalars, opens it as a table (see Table editor). A missing
  cell is added with the most common kind of its column (integers and floats count as numbers; a tie gives a string), and a
  cell is edited like a value, keeping its style. `a`, `i`, `d` and Alt+Up/Down add, duplicate and move rows as in JSON, and Delete deletes a row. A sequence
  with an alias item, or with items of mixed kinds, is not a table. An alias cell cannot be edited or cleared in the table.

## What is kept

cfgedit edits the file's text, not a reformatted copy of it. Saving without changes writes the same bytes. An edit
changes only the edited value or name; a new entry copies the indentation, quote style and separators of its neighbours;
a deleted entry takes its comments with it. Every edit is checked: the new text is parsed again and must contain exactly
the intended change, so a fault in cfgedit can refuse an edit but never corrupt the file.

XML is checked by .NET's XML reader. A value containing an entity declared in the file's DOCTYPE is shown and edited as
raw text (marked `raw`). Files whose entities contain markup open in text mode only.
