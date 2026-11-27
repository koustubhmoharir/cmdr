# mdedit

A terminal viewer and editor for Markdown files. It shows the file rendered: headings, lists, quotes, tables, code blocks
and links look as they will read, and the source is one key away (Ctrl+T), in the same window with the same undo history.
You move a cursor through the rendered text, select, search and copy, and you edit the source. Ctrl+S saves the file, and
Ctrl+Q and Ctrl+O ask before they leave unsaved changes (see Saving).

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

`mdedit <file>` opens the file. A path that does not exist asks to create it (see New files). `mdedit` alone shows the
recently used files: Enter opens the selected one, a number selects by position, Ctrl+O opens a file through a file dialog,
and Ctrl+Q quits. A file that cannot be read is reported as `mdedit: <path>: <message>` and mdedit exits with status 1; a
usage error exits with status 2.

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
^T Source  ^S Save  ^F Find  ^C Copy  Alt+F Fold   ^Q Quit
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
| Ctrl+Home/End | First cell of the document / the end of the last paragraph |
| Shift with a movement key | Extend the selection. The selection is the text from where the movement started up to the caret, not including the character the caret is before: Shift+Right three times selects three characters |
| Ctrl+F | Find in the rendered text. Enter goes to the first match after the cursor; Esc closes the bar |
| F3 / Shift+F3 | Next / previous match, wrapping at the ends with a message |
| Ctrl+C / Alt+C | Copy the selection as plain text, or the block under the cursor when nothing is selected; Alt+C copies it as Markdown. The text also goes to the application clipboard when there is one, so Ctrl+V pastes it back |
| Ctrl+X | Cut the selection: copy it as Markdown, then delete it, as one undo step. With no selection, says so (see Editing in the view) |
| Ctrl+V | Paste the application clipboard's text as Markdown source, at the caret or over the selection. Says so when there is no clipboard here, or it holds no text |
| Terminal paste | The text that the terminal pastes (bracketed paste) is inserted as Markdown source, as Ctrl+V inserts the clipboard's. In edit mode the editor takes it |
| Alt+F | Fold or unfold the section or code block under the caret (see Folding) |
| Alt+1–Alt+6 / Alt+0 | Show the headings down to that level, folding the deeper sections; Alt+0 unfolds everything |
| Letters, digits, punctuation, Space | Type the character at the caret (see Editing in the view). Letters and digits are no longer commands |
| Enter | Split the block at the caret, or start a new block below or above it, or replace the selection (see Editing in the view). In a table cell, Enter moves the caret down a row, or adds a row below the last one |
| F2 | In a table cell, open the cell's field (see Tables) |
| Backspace / Delete | Delete the character before / after the caret, or the selection; at a block's start / end they join or unwrap blocks (see Editing in the view) |
| Ctrl+Enter | Follow the link under the caret (see Links). Ctrl+J does the same |
| Shift+F10 | Open the menu at the cursor |
| Ctrl+T | Edit mode, with the caret on the source character under the view's caret (see Edit mode) |
| Ctrl+S | Save the file (see Saving) |
| Ctrl+Z / Ctrl+Y | Undo / redo the source, in either mode (see Edit mode) |
| Esc | Close the search bar |
| Ctrl+O | Open another file |
| Alt+O | Open the outline of the headings (see Outline) |
| Ctrl+Q | Quit |

Any movement without Shift clears the selection. Copying writes the text to the clipboard through the terminal (OSC 52),
so the terminal must allow it. A copy over 1,000,000 bytes is not written, and the message says so.

Ctrl+V and Ctrl+X use the application clipboard. On Windows, macOS and WSL it is the system clipboard; on Linux it is the
`xclip` clipboard when `xclip` is installed. Without xclip, the application keeps its own clipboard: Ctrl+V pastes what this
session copied or cut, and text copied in another program comes in through the terminal's paste. When the clipboard cannot
be read, Ctrl+V says that the clipboard is not available here.

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
| Right-click | Open the menu: Copy text, Copy Markdown, Cut (when there is a selection), Paste (when the clipboard holds text), and Fold or Unfold, Unfold all inside, and Fold section, for the section or code block at the mouse and the section around the caret (when there are any). Choose with the mouse, or with the arrow keys and Enter |

A right-click inside the selection keeps it; outside, it moves the cursor there, as a click does.

## Tables

A table wider than the window scrolls sideways on its own: the rest of the document stays where it is. Its first columns
show, and a dim `▸` in the last column says that more follows to the right (a `◂` in the first column says so for the left).
Each table keeps its own offset, so scrolling one table does not move another.

- Shift+wheel, or the horizontal wheel, scrolls the table under the mouse by eight columns.
- A click on a `◂` or `▸` scrolls the table by eight columns toward that side.
- Moving the caret into a column that is out of view scrolls the table by the least it takes to show the caret's cell.
- Tab and Shift+Tab in view mode move the caret to the next or previous cell, without opening a field. After the last cell,
  Tab goes on to the next link, as it does outside a table.
- A click or a drag on a scrolled table selects the cells that are drawn under the mouse.

The container of a table in a quote or a list stays in place while the table scrolls. A table that fits has no offset.
Code blocks do not scroll: a long line in a code block wraps.

### Editing a table

In view mode, F2 with the caret in a cell opens a field over the cell, with the cell's source
text (an escaped `\|` shows as `|`). Enter writes the cell, Esc cancels, and Tab or Shift+Tab writes it and opens the next or
previous cell: the next row's first cell after the last column, and the previous row's last cell before the first. After the
last cell, Tab writes the cell and closes the field. A write is one undo step (Ctrl+Z), and the caret goes to the cell.

- Enter in a cell moves the caret to the same column of the next row. On the last row it adds a row below and moves there,
  as one undo step. Typing, Backspace and Delete change a cell's text directly, without a field (see Editing in the view).
- Alt+Up and Alt+Down move the caret's row up and down; Alt+Left and Alt+Right move its column left and right. The caret
  moves with the row or column. Outside a table Alt+Left is still Back (see Links).
- Alt+T opens the table menu at the caret, and the right-click menu over a table has the same items: Edit cell, Insert row
  above and below, Duplicate row, Delete row, Move row up and down, a Column submenu (Insert column left and right, Delete
  column, Move column left and right), and an Align column submenu (left, centre, right or none). An item that cannot be
  done is still shown; choosing it shows why.
- An aligned table (every line's pipes at the same columns) is written aligned again after an edit. Any other table has
  only the cells and lines that change written.
- Some changes are refused, with a message, and nothing changes: deleting or duplicating the header row, moving the header
  row or a row onto it, moving a row or column past either end, deleting the last column, and a cell whose text would split
  the row (a line break, or a backslash before a `|`). A refused write in the field keeps the field open with its text.

Edit mode has none of these keys: its source is edited directly.

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

## Edit mode

Ctrl+T switches to edit mode: the source text in an editor, with the caret on the source character under the view's caret.
A selection in the view is the selection in the editor, and it comes back the same way. The editor is a plain text box with
the usual keys: typing, the arrows and Shift+arrows, Home and End, Tab inserts a tab, and Enter and paste write the file's
own line ending (a CRLF file gets CRLF).

| Key | |
|---|---|
| Ctrl+T, Esc | Back to view mode. The view is rendered again when the text changed. The caret goes to the rendered character of the source caret; when the caret is on markup that is not shown (`**`, `# `), the next visible character. Esc closes an open search first |
| Alt+Z | Word wrap on / off (on at the start) |
| Alt+F | Fold or unfold the section or code block at the caret |
| Alt+O | Open the outline of the headings, to go to one (see Outline) |
| `/` | Types a slash (in view mode it opens the search) |
| Ctrl+Z / Ctrl+Y | Undo / redo the editor's history |
| Ctrl+F | Find in the source (the search bar, as in view mode). F3 / Shift+F3 go to the next / previous match, with the same wrap and no-match messages as view mode. Enter goes to the first match after the caret; the match is selected, with the caret at its start. Esc closes the bar, and the focus goes back to the editor |
| Ctrl+S | Save the file (see Saving) |
| Ctrl+Q, Ctrl+O | As in view mode |

- **One history.** Undo and redo in view mode (Ctrl+Z / Ctrl+Y) undo the same history, and the view is rendered again. The
  caret goes to where the change was: where the removed text was, or after the restored text.
- **Title.** The file name, then ` *` while the text differs from the file as it was read, then ` [edit]` in edit mode.
- **Search.** The matches are the source text, markup included: `**b` finds the start of bold text. They follow the edits while
  the bar is open. The search text carries over when the mode changes, and the active match does not: the bar closes when the
  mode changes, and Ctrl+F, F3 or Shift+F3 in the other mode searches again with the same text.
- **Hints.** View mode shows `^T Source` and `^S Save` first. Edit mode shows `^T View`, `^S Save`, `^F Find`, `Alt+Z Wrap`, `Alt+F Fold`, `Alt+O Outline` and `^Q Quit`.
- **Folds and tables** keep their state through edits: a folded section stays folded where it moved when text is inserted
  above it, and a table keeps its scroll.
- **The caret's screen row** is not kept across Ctrl+T: the editor's scroll position cannot be set from outside, so the caret
  is kept on screen instead.
- **Large files.** A file over 2 MB on disk opens in edit mode, with `Large file: opened as text`. Ctrl+T to view mode asks
  `Render <n> MB?` (Render is the default) when the view has to be rendered: rendering the whole text takes time. Cancel
  stays in edit mode.

## Saving

Ctrl+S, in view mode or in edit mode, writes the text to the file in place, keeping its permissions and owner. Only the text
changes on disk: a CRLF file is written with CRLF, and a file that began with a byte order mark keeps it.

The title shows ` *` while the text differs from the file as it was read or last saved. A new file is marked until its first
save.

If the file was changed by someone else since it was read or last saved, Ctrl+S asks whether to overwrite it, reload it, or
cancel. Overwrite is the default. Reload replaces the text with the file's text as one undo step, so Ctrl+Z goes back to the
text before the reload, and it writes nothing. Cancel saves nothing.

Ctrl+Q, and Ctrl+O once a file is chosen from the picker, ask "Save changes to <name>?" with Save, Discard and Cancel when the
document has unsaved changes. Save is the default. A save that fails keeps the window open, with the error on the message
line; Cancel keeps it open too, and Discard leaves it without saving.

## New files

`mdedit <path>` for a path that does not exist asks "Create <name>?" with Create and Cancel. Create opens an empty document in
edit mode. Nothing is written until the first Ctrl+S, which creates the file. The folder must exist; a save into a folder that
does not exist shows the error and writes nothing. The file is added to the recent list after its first successful save.
Cancel exits without creating anything.

## Changes on disk while it is open

mdedit checks the file once a second, so a change made by another program shows up without a save. A change to a file with
no unsaved edits reloads it. The reload is an undo step: Ctrl+Z goes back to the text before it, and a message says the file
was reloaded (there is no message when there was nothing to undo). The caret, the selection and the folds stay where the text
around them did not change.

A change to a file with unsaved edits asks whether to reload it or keep your changes; the default is Keep mine. Reloading
keeps your edits as an undo step. Keep mine leaves your document as it is, and Ctrl+S then overwrites the file without
asking again. The document stays marked unsaved while it differs from the file on disk, even if you undo all your edits.

If the file is deleted or moved, mdedit asks whether to keep the document, which then becomes an unsaved new file that Ctrl+S
creates again, or to close it. Close discards unsaved changes and returns to the recent-files picker.

The check waits while a dialog, the context menu or the picker is open, and is done as soon as they close. A file that cannot
be read is reported once on the message line, and not again until the file changes state.

## Editing in the view

The rendered view is editable, and the view shows each change as it is made. What works now:

- Typing puts a character at the caret, in the style of the text before it: typed before the space after a bold word, it
  goes inside the bold. Typed at the end of a link, it does not extend the link. A character that would be Markdown markup
  is written escaped (a typed `*` is written `\*`).
- Backspace deletes the character before the caret, and Delete the one after it. A bold or italic span left with no text
  goes with its markers, and a link left with no text goes with its destination.
- A selection inside one block, such as a paragraph or a heading, is replaced by what is typed, or deleted by Backspace or Delete.
- A selection across blocks is replaced by what is typed, or deleted by Backspace or Delete; Enter over it splits the block
  where the selection began. The blocks that the selection covers whole go, and the first and last blocks keep the text
  outside it and are joined into the first block's kind, as Backspace at a block's start joins them. A selection that starts
  or ends in a code block or a table, without covering it whole, is refused.
- Typing, Backspace and Delete in a table cell change the cell's text. An aligned table is written aligned again; any other
  table changes only the cell's line. Backspace at a cell's start and Delete at its end do nothing.
- Paste inserts Markdown source: the text is written as it is, not escaped, so a pasted `**x**` is bold. Its lines end with
  the document's line ending, and each line after the first takes the container of the block it lands in (a quote's bars,
  a list item's indent). A paste over a selection replaces it. The terminal's paste and Ctrl+V both paste this way (see
  Keys). A paste is one undo step, and it ends a typing run. In a table cell a paste takes one line, with any `|` escaped.
- Ctrl+X copies the selection as Markdown, through the clipboard as Ctrl+C does, and deletes it as one undo step, so Ctrl+Z
  brings it back. A cut that is refused copies nothing.
- Enter splits the block at the caret: a paragraph, a heading or a list item becomes two of the same kind (a new list item is
  numbered after the old one, and a task item starts unchecked). At the end of a block, Enter holds a new block below it, a
  paragraph or, after a list item, a new item; at its start, it holds one above. The new block is drawn on a line of its
  own, and it is written when a character is typed. Enter on a new item that is still empty ends the list, and Enter on a
  new paragraph in a quote ends the quote. In a code block, Enter writes a line break. In a table cell it moves the caret
  down a row, or adds a row below the last one (see Tables).
- Backspace at a block's start unwraps a heading into a paragraph, unwraps the first item of a list, or joins the block to
  the text before it (the two keep the previous block's kind). Delete at a block's end joins the text after it. A line
  break inside a paragraph (a hard break) is removed by Backspace at the start of the next line or by Delete at the end of
  the line, which joins the two lines.
- A space that would not show (at the start or end of a paragraph) is held until the next character is typed, so that
  the file gets no empty space. Moving the caret drops it, and so does Backspace or Delete, which then deletes
  nothing else. The same goes for a new block that Enter holds.
- Ctrl+Z and Ctrl+Y undo and redo. Typed characters with no move between them are one undo step, so are consecutive
  Backspaces and consecutive Deletes, and a word with the space after it is one step. Each Enter is a step of its own, and
  so is a Backspace or Delete at the start or end of a line, since it joins or unwraps blocks.
- An edit that the view cannot write as it reads is refused with a message, and nothing changes. Typing on the heading of a
  folded section or code block is refused (unfold it first).

Some edits are still refused with a message, and Ctrl+T edits the source for them, in the same window and with the same
undo history:

- A join or unwrap next to a code block, a table, an HTML block, or a block nested differently is refused.
- Enter at the start or end of a line inside a paragraph (next to a hard break), and Enter inside a link's text, are refused.
- Backspace at the start of a list item nested in another item is refused; only a top-level item unwraps into a paragraph.
- Backspace at the start of a block inside a quote unwraps it only when the quote is the block's only container.
- A table cell takes one line: a paste with a line break into a cell is refused.
- A row shorter than the header has no caret place in its missing columns, so Enter there is refused.
- A cell has no caret place after its last character, so text cannot be typed at a cell's end: Right from the last character
  goes to the next cell. Typing at a cell's start writes into that cell.
- Deleting a selection that covers a bold or italic span's text and both its markers is refused ("Can't type here").
- After typing in an aligned table, Ctrl+Z puts the caret in the next cell, not in the cell that was typed in: the undo of a
  table's rewrite places the caret at the end of the restored text.
- Enter over a selection inside a table cell is refused when the Enter after the removal is.
- A join across blocks whose last block continues over lines with container prefixes (a quote or a list) is refused.
- What a paste renders (a list marker in the middle of a paragraph, say) is not checked: the paste writes the source as given.

## Folding

Sections (a heading and everything up to the next heading of the same or a higher level) and code blocks fold. A folded
heading shows `▸` and `… n lines`, the number of source lines hidden; a folded code block shows its language and the
number of its code lines. Unfolded headings show `▾`. The glyphs sit in a two-column gutter at the left edge, so the text starts in the third
column.

| Key | |
|---|---|
| Alt+F | Fold or unfold the section or code block whose heading or header line the caret is on. Elsewhere, the innermost section (or code block) around the caret. A caret inside what folds moves to the heading |
| Alt+1–Alt+6 | Fold every section whose heading is deeper than the digit and unfold the others, so that the headings down to that level show. Code blocks keep their state |
| Alt+0 | Unfold everything |

The right-click menu also has Unfold all inside, which unfolds the unit at the mouse and every unit inside it, and Fold section,
which folds the innermost section around the caret.

Folding keeps the caret's row on the screen when the fold is above it. Search unfolds what hides a match before it selects
the match, and the search still finds text in folded content. Folds stay when the window is resized.

**Edit mode.** The editor shows the same folds, in a fold gutter of its own: a folded section shows its heading line with
`▸` and `… n lines`, and a folded code block its fence line. Alt+F folds or unfolds the section or code block at the caret,
and a click on a `▾` or `▸` in the gutter does the same. Folds made in either mode carry over to the
other. Typing keeps a fold while its heading or fence line keeps its start, as in view
mode. The editor's folds are brought up to date a moment after a change that is not only letters (a space, a marker, a
line break), and before the view is shown again.

With the mouse, a click on a `▾` or `▸` glyph, or on a `… n lines` summary, folds or unfolds that unit, and the right-click
menu has Fold or Unfold for the unit at the mouse.

## Links

In view mode the links of the document are the places the caret goes to with Tab. The message line shows the URL of the link
under the caret in place of the heading path, with an arrow in front.

| Key | |
|---|---|
| Tab / Shift+Tab | The caret goes to the next / previous link, in the order of the text. Links that a fold hides are skipped. Past the last link, Tab wraps to the first one with a message, and Shift+Tab past the first wraps to the last. A document with no link says so. In a table, Tab moves to the next cell instead (see Tables) |
| Ctrl+Enter | Follow the link under the caret (see below). Terminals that send Ctrl+Enter as a line feed also follow it with Ctrl+J. Enter edits the text instead (see Editing in the view) |
| Alt+Left | Go back to the file that the last followed link left (see Back) |

| Mouse | |
|---|---|
| Ctrl+click on a link | Follow the link. The caret goes to the clicked link. A plain click on a link only places the caret |

A link goes where its destination says:

- **`#heading`**: the heading in this file. The section that hides the heading is unfolded, the caret goes to the heading, and
  the heading becomes the top line. A heading that does not exist is reported on the message line.
- **A relative path to a file** (`other.md`, `../notes/todo.md#plan`): the file is opened in mdedit, in a new window, at the heading
  of the fragment when there is one. A file that does not exist is reported as not found; a file that cannot be read shows its
  error and the window stays. Unsaved changes are asked about first, as Ctrl+O asks (Save, Discard or Cancel). The path is
  resolved against the folder of the current file.
- **Any other URL** (`https:`, `mailto:`, …): copied to the clipboard, through the terminal as Copy does, and the message says
  what was copied. mdedit opens nothing itself. A link with no destination says "Empty link".

**Back.** Each followed file link remembers where it was left: the file, the caret and the top line. Alt+Left goes back to
the last of them, with the caret and the top line where they were. The unsaved-changes question comes first, as it does for
a followed link. Going back takes the place off the list, so Alt+Left again goes to the one before, and with none left it says
"No previous file". Ctrl+O, when a file is chosen, clears the list. A file that cannot be read any more is reported, and its
place is dropped from the list.

## Outline

Alt+O, in either mode, opens the outline: a list of the headings of the document, with a filter field
above it. Each heading is indented by two spaces for each level below the first, so a level-2 heading sits under the level-1
heading before it. The list starts on the heading of the section the caret is in.

| Key | |
|---|---|
| Typing | Filters the list: a heading stays when its text contains what was typed, ignoring case. The indent stays. The list selects the first heading that is left |
| Up / Down, PgUp / PgDn, Home / End | Move the selection in the list, while the filter field has the focus |
| Enter | Go to the selected heading (nothing happens when the filter leaves none) |
| Esc | Close the outline and stay where the caret was |

| Mouse | |
|---|---|
| Click on a heading | Select it. A double-click goes to it, as Enter does |

- **In view mode**, the heading goes to the top of the view and the caret is on it, as a `#heading` link does: the section
  that hides the heading is unfolded first.
- **In edit mode**, the caret goes to the start of the heading's line, and the folds that hide the heading are unfolded.
  The outline is that of the text as it is now, including the changes not yet rendered.
- **No headings**: a document with no heading says "No headings" on the message line, and no list opens.
