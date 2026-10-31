# Demo

A small notes app to try RcinelTranslate with. `src/SettingsPage.cs` and `web/app.js` call
`Translate` (C#) / `translate` (JavaScript); `i18n/` is the project those calls produced with
`rcinel scan --assign`, with:

- German partly done: some confirmed, some AI drafts, one outdated because the English changed after
  review, three missing;
- French barely started, Brazilian Portuguese empty;
- one screenshot of the settings page with every text on it marked.

```fish
cd ../../dotnet
dotnet run --project src/Cli -- status --project ../samples/demo/i18n
set -x Rcinel__Project ../samples/demo/i18n
set -x Rcinel__AdminUser admin
set -x Rcinel__AdminPassword a-long-password
dotnet run --project src/Editor      # http://localhost:5280
```

The ids in the code were filled in by `rcinel scan --assign` on 2026-10-31, which is the date their
UUIDv7s carry.
