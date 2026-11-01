# Changelog

What changed in each RcinelTranslate release, newest first. The release notes are built from this
file, so write for the people who use the libraries and the editor: what they notice, not how it was
done.

Format: a `## <version> - <yyyy-mm-dd>` heading per release (the version must match `<Version>` in
`dotnet/Directory.Build.props`, `java/gradle.properties` and `javascript/package.json`), then
`### Added`, `### Changed` or `### Fixed` with one bullet per change.

## 0.1.0 - Unreleased

### Added
- Clients for .NET (netstandard2.0), Java (8+), JavaScript, Python (3.9+) and Rust (1.70+) that load
  a language with its fallback chain and translate with the same call in each:
  `Translate("UUIDv7", "English text")` in C#, `translate(...)` in the others. None has dependencies. On .NET 6 and newer the NuGet package imports the
  `Rcinel` class for the whole project, so C# code needs no using; `RcinelTranslateGlobalUsing`
  set to `false` switches that off.
- The file format: one flat JSON file per language, sorted by UUIDv7 so new strings land at the end,
  plus review and context files for the editor.
- The `rtr` command (Rcinel TRanslate, from the `Rcinel.Translate.Cli` tool package): `init`,
  `scan` (with `--assign` to fill in ids), `status`, `check`, `format`, `prune`, `add-language`,
  `export` and `draft`.
- The web editor: progress per language, review with confirm and draft states, outdated detection
  when the English changes, placeholder checks, AI drafts with Claude, screenshots with marked texts
  and a live preview of each translation in place, and users with admin and translator roles.
- A container image and compose file for the editor that work with Podman and Docker.
