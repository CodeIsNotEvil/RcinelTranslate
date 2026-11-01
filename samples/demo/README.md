# Demo

A small notes app to try RcinelTranslate with. `src/SettingsPage.cs` (C#) and `web/app.js`
(JavaScript) hold the calls; `i18n/` is the project `rtr scan --assign` made from them:

- German partly done (confirmed, drafts, one outdated, three missing);
- French barely started, Brazilian Portuguese empty;
- one screenshot of the settings page with its texts marked.

```fish
cd ../../dotnet
dotnet run --project src/Cli -- status --project ../samples/demo/i18n
set -x Rcinel__Project ../samples/demo/i18n
set -x Rcinel__AdminUser admin
set -x Rcinel__AdminPassword a-long-password
dotnet run --project src/Editor      # http://localhost:5280
```
