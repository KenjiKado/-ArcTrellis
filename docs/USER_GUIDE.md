# ArcTrellis User Guide

## First launch

ArcTrellis opens a template gallery. Choose **The Glass Horizon** to explore a finished example, one of the structural templates to start from prompts, or **Blank project**. Save the project anywhere as an `.arctrellis` file.

## Dashboard

Set the series title, author, genre, premise, current word count, and goal. A project may contain any number of books. The dashboard shows live counts for books, chapters, scene cards, plotlines, characters, places, and drafted scenes.

## Timeline

Choose a book at the top. Columns are chapters or structural beats; rows are plotlines. Double-click an empty cell to add a scene there. Drag any scene card to another cell to change its chapter and plotline. Double-click a card to open its detailed editor.

Use the plotline selector and adjacent name/color fields to customize a row. Colors use hexadecimal notation such as `#5B7CFA`. Zoom changes card width; **File > Print Timeline** sends the current timeline to a Windows printer or PDF printer.

## Outline and scenes

The Outline tab manages chapter order, act/section names, summaries, and chapter word counts. Deleting a chapter also deletes its scenes after confirmation.

The Scenes tab stores title, workflow status, point of view, setting, word count, tags, summary, draft/working notes, editing notes, and any number of custom fields. Tags are comma-separated. Scene statuses are Planned, Drafted, Revised, Final, and Cut.

## Story bible

Characters, Places, and Notes all support a name, category, comma-separated tags, summary, long description, optional image-file path, and custom fields. Custom fields make it possible to build any sheet: goals and conflicts, magic rules, location climate, clue state, research source, and so on.

The Relationships tab connects any two bible entries. Use it for family links, alliances, rivalries, ownership, travel, or any other named connection.

## Series View and search

Series View places every book spine side-by-side for high-level continuity work. Search examines books, chapters, scenes, draft and editing notes, characters, places, notes, tags, and custom fields. Double-click a result to open its editor.

## Templates

Built-in templates are examples, not locked forms. Rename, reorder, add, and delete any element. **File > Save as reusable Template** stores the current project as a new template. New projects regenerate every internal identifier, so they never collide with the original.

## Saving, backups, and recovery

Press `Ctrl+S` to save. Before overwriting, ArcTrellis copies the prior version into an `ArcTrellis Backups` folder beside the project and retains the newest 20 copies. While a project has unsaved changes, a separate recovery copy is written every 45 seconds. When opening a project with a newer recovery copy, ArcTrellis offers to restore it.

The project file contains ordinary UTF-8 JSON and can be backed up with any file-copy tool. ArcTrellis never uploads it.

## Import and export

**Markdown import** recognizes this hierarchy:

```text
# Project
## Book
### Chapter
#### Scene
Scene notes follow the scene heading.
```

Exports include:

- **Word (.docx):** a standard Open XML outline/draft that opens in Microsoft Word and compatible editors.
- **Markdown:** portable, readable outline and scene text.
- **CSV:** one scene per row for spreadsheet analysis.
- **Scrivener project folder:** a `.scriv` folder with binder hierarchy and RTF scene documents. Open the `.scrivx` inside it from Scrivener.

## Keyboard shortcuts

- `Ctrl+N` — new from template
- `Ctrl+O` — open project
- `Ctrl+S` — save
- `Ctrl+Z` / `Ctrl+Y` — undo / redo structural changes

## Local file locations

- Recovery copies: `%LOCALAPPDATA%\ArcTrellis\Autosave`
- User templates: `%LOCALAPPDATA%\ArcTrellis\Templates`
- Rotating backups: `ArcTrellis Backups` beside each saved project

## Troubleshooting

If a project will not open, choose the newest file in its `ArcTrellis Backups` folder. If ArcTrellis was interrupted, reopen the original project and accept the recovery prompt. The application is self-contained; installing or repairing .NET is not required on an end-user computer.
