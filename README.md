# MediaBot

A FileBot-style desktop app (WPF, C#) that renames TV episodes, movies, and subtitles using TheMovieDB metadata.

## Building the installer

Targets .NET 10 (LTS). Requires the .NET 10 SDK (`global.json` pins 10.0.404) and the WiX tool (`dotnet tool install --global wix`).

```
powershell -ExecutionPolicy Bypass -File installer\build-msi.ps1 -Version 1.0.0
```

The installer is written to `dist\MediaBot-<version>-x64.msi`. The script deletes older `MediaBot-*` and `MediaRenamer-*` files in `dist` first, so only the newest build stays. It installs to `Program Files\MediaBot` with a Start menu shortcut and includes the .NET 10 runtime, so users don't need to install .NET.

The installer is unsigned, so Windows SmartScreen may warn users when they run it.

## Setup

1. Get a free API key at https://www.themoviedb.org/settings/api (use the "API Key", v3 auth).
2. Build and run:
   ```
   dotnet run --project MediaRenamer.csproj
   ```
3. Open **Settings > Preferences** and paste the key into **TMDB API key**. Settings are saved to `%AppData%\MediaRenamer\settings.json`.

## Usage

1. Drag video files, subtitle files, or whole folders onto the window (or use **Add Files**).
2. Click **Fetch Data**. In the Match window, choose **TV Episodes** or **Movies** (set automatically from your files), check the search box, click **Search**, then pick the right match and click **OK**. Results show TMDB posters, with a larger preview of the selected show or movie.
3. Click **Match** to preview the new names and destinations in the **New Names** panel, with the poster of the matched show or movie. Nothing is changed yet.
4. Click **Rename** to apply. Files that already exist at the target name are skipped.

Subtitles are matched to videos by season/episode (TV) or by filename prefix (movies). Language tags such as `.en` are kept.

## Output folder and folders

- **Output folder** is optional. Leave it empty to rename files in place. Set it to move files into folders under that location.
- **TV folder** and **Movie folder** set the subfolder structure. Use `/` for nested folders.
  - Defaults: TV `{n}/Season {s}`, Movie `{n} ({y})`.
  - Example result: `D:\Media\TV\Show Name\Season 01\Show Name - S01E02 - Title.mkv`
- Subtitles go into the same folder as their video.

## Dry run

Dry run is a debug option. Turn it on under **Preferences > Debug > Dry run**. When it's on, **Rename** reports what would be renamed and changes nothing.

## Plex naming

Open **Preferences** and click **Reset to Plex naming** to restore the Plex/Kodi/Jellyfin defaults:
- TV: `Show Name/Season 01/Show Name - S01E01 - Title.mkv` (needs an output folder)
- Movies: `Movie Name (2010)/Movie Name (2010).mkv` (needs an output folder)

## Artwork and NFO files

Turn on **Write NFO files and posters** in Preferences. When you click **Match**, the app fetches details from TMDB. When you click **Rename**, it writes:
- `<video name>.nfo` next to each video, with the title, plot, and air date (TV) or the movie details
- `tvshow.nfo` and `poster.jpg` in the show folder (TV), or `poster.jpg` in the movie folder (movies)

Posters and show folders are only written when an output folder is set. Undo removes the files the batch wrote.

## Subtitles

Click **Subtitles** in the left rail. The window lists your video files and what it found for each one.

- **Find Local** looks in each video's folder for subtitle files. It matches them by name, or by season and episode when the names differ. **Apply** renames each matched file to the video's name and keeps its language tag, for example `Show - S01E02.en.srt`.
- **Search Online** looks up subtitles on OpenSubtitles, first by file hash and then by title and episode. It needs an OpenSubtitles API key, plus your username and password, set in **Preferences**. **Apply** downloads the best match as `<video name>.<language>.srt`.
- **Convert to UTF-8** re-encodes subtitles that aren't UTF-8 (UTF-16 or older Western encodings). Files that can't be decoded as UTF-8 are read as Latin-1.

Subtitle renames made here aren't recorded in the undo history, so they can't be undone from the main window. The username and password are stored in plain text in `settings.json`.

## Episodes

Click **Episodes** in the left rail to browse a show's episode list from TMDB.

- Type a show name and click **Search**, then pick the show from the dropdown.
- Pick a season to list its episodes, with episode titles and air dates.
- The **Have file** column shows `yes` for episodes matching the files loaded in the Rename view.

This needs your TMDB API key. Nothing is renamed from here; it's a reference view.

## Matching scene file names

Search boxes accept whole file names, so `The.Office.US.S01E01.720p.BluRay.x264-GROUP` works as well as `The Office`.

MediaBot strips season/episode tags, years, quality and codec tokens, bracketed tags (such as `[SubsPlease]`), and trailing release groups like `-SPARKS` or `-NTb`. Normal hyphenated titles such as `Spider-Man` are left alone. If the cleaned title still finds nothing, the search retries with fewer trailing words, so `The Office US` falls back to `The Office`. The window shows which title was used when it differs from what you typed.

## Missing Episodes

Click **Missing** in the left rail. The results appear in the window, grouped into Anime and TV shows, and you can filter by show name.

It works in two modes, picked with the radio buttons at the top:

- **Sonarr library:** Click **Scan Sonarr** to find missing episodes across your whole library. The scan shows episodes that have aired, are monitored, and have no file. Episodes already in Sonarr's download queue are shown as downloading, not missing. Entirely missing seasons are listed separately.
- **Added files:** Add files or drag a folder in. Click **Check Files** to find:
  - **Gaps in numbering:** missing episode numbers between the first and last files you have for each season. Works without Sonarr.
  - **Missing from Sonarr:** aired episodes that Sonarr expects but your files don't cover. Needs Sonarr set up. Shows that Sonarr doesn't recognise are listed under "Not found in Sonarr".

Set up Sonarr in **Preferences > Sonarr**. If Sonarr is installed on the same PC, click **Detect from this PC** and the URL and API key are filled in from Sonarr's own settings file. Otherwise, enter the URL (for example `http://localhost:8989`) and the API key from Sonarr's **Settings > General > API Key**. If no URL or key is saved, Scan Sonarr tries detection automatically. The scan only reads from Sonarr and never changes anything there. Include specials (Season 0) and only-monitored options are in the same section.

## srrdb lookup

In **SFV**, click **Look up on srrdb** to identify media files against the srrDB scene-release database.

- MediaBot computes each file's CRC32 and searches srrDB for a release with that CRC. If that finds nothing, it tries the file name as a release name.
- When a release is found, MediaBot fetches its file list and checks whether one of its files has the same CRC. Results are shown as `MATCH`, `FOUND` (release found, but no file with that CRC), or `NOT FOUND`.
- Up to 40 files are looked up per run. Requests go to `api.srrdb.com` one at a time, only when you click the button. Nothing is uploaded.
- srrDB only knows scene releases, so most files won't be found. It never downloads anything.

## List

Click **List** in the left rail to export the loaded files as a list.

- **Text** mode writes one line per file, using the line template. The default is `{original} -> {new}`.
- **CSV** mode writes a header row and one row per file, with all the columns.

Tokens: `{original}`, `{new}`, `{path}`, `{folder}`, `{ext}`, `{size}`, `{status}`, `{title}`, `{season}`, `{episode}`, `{year}`. Title, year, season, and episode use the match you picked, when there is one.

Use **Copy** to put the list on the clipboard, or **Save...** to write a `.txt` or `.csv` file. CSV files are saved as UTF-8 with a byte-order mark so Excel reads them correctly.

## Undo

**Undo last rename** reverses the most recent rename batch. It moves files back and removes any folders the batch created, if they are empty. The history is saved in `%AppData%\MediaRenamer\undo.json` and keeps the last 50 batches. A file that has been moved or renamed since the batch can't be restored, and the app will tell you how many failed.

## Templates

| Token | Meaning |
|-------|---------|
| `{n}` | Show or movie title |
| `{s}` | Season, 2 digits |
| `{e}` | Episode, 2 digits |
| `{t}` | Episode title |
| `{y}` | Year |

Defaults: TV name `{n} - S{s}E{e} - {t}`, Movie name `{n} ({y})`.

## Licence

MIT. See [LICENSE](LICENSE).

MediaBot uses the TMDB and srrDB APIs but is not endorsed or certified by them.

## Limitations

- Only TMDB is supported. TheTVDB is not implemented.
