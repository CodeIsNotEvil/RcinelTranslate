---
tags: [project]
created: 2026-10-31
updated: 2026-10-31
status: active
---
# RcinelTranslate

A translation library the owner wants to publish (command `rtr`, project file
`rcinel-translate.json`; "Rcinel" alone is the prefix of all the owner's apps, see [[2026-11-01]]): `Rcinel.Translate("UUIDv7", "English text")` in
C#, Java and JavaScript, plain JSON files shipped with the app, the `rtr` tool, and a web editor
where AI drafts and people confirm. Version 0.1.0, not released yet.

## Open questions for the owner
- Package coordinates: NuGet `Rcinel.Translate*`, Maven `io.github.codeisnotevil:rcinel-translate`
  (Maven Central needs the `io.github.codeisnotevil` namespace verified), npm `rcinel-translate`
  (unscoped; a scope needs an npm org). Fine, or other names?
- Publishing: `.github/workflows/release.yml` publishes on a `vX.Y.Z` tag, once the owner has
  added the secrets listed in the README ("Releasing"). Not run for real yet.
- Should drafts ship? Today `translations/` holds drafts too, and `rtr export --confirmed-only`
  is the way to ship only reviewed text. The alternative is keeping drafts in `review/` until
  confirmed.

## Next steps
- The editor's container image is not published yet (GHCR would fit the release workflow).
- Plural forms: today a text is one string; ICU MessageFormat or per-count keys would need a format
  change ([[0002-flat-key-value-json]]).
- The scanner reads string literals only; C# raw strings (`"""`) and Kotlin raw strings aren't read.
- Preview: font weight isn't sampled, so bold texts preview in regular weight.
- Editor: per-language permissions for translators, and an export/import for external agencies.

Related: [[index]]
