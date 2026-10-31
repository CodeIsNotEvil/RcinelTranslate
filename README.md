# RcinelTranslate

Translations for apps, keyed by UUIDv7, for C#, Java and JavaScript. You write the English text where
it's used:

```csharp
saveButton.Text = Translate("01a192e3-5bd4-7005-b3d0-b98f0a9cfe3d", "Save");
```

```java
saveButton.setText(translate("01a192e3-5bd4-7005-b3d0-b98f0a9cfe3d", "Save"));
```

```js
saveButton.textContent = translate('01a192e3-5bd4-7005-b3d0-b98f0a9cfe3d', 'Save');
```

and the app shows "Speichern" when German is loaded, or "Save" when there's no translation yet. The
call has the same shape in all three languages; only the first letter follows each language's
convention.

Around that one call there is:

- **Small clients** for .NET (netstandard2.0), Java (8+, Android included) and JavaScript (browsers
  and Node). They load the translation files you ship with the app. None of them has dependencies.
- **Plain JSON files**, one per language, in the key-value format i18next, Crowdin, Lokalise and
  Weblate already read. Sorted by id, and since a UUIDv7 starts with its creation time, new strings
  always land at the end: a release's translations show up as one block in the diff.
- **`rcinel`**, a command-line tool: it collects the translate calls from your code (and
  fills in ids for you), shows how far each language is, checks the files in CI and exports them.
- **A web editor** for translators: AI drafts every missing translation (with Claude), people
  confirm or correct them, and screenshots of the app show each text in place with a live preview of
  the new wording, so "too long for the button" shows up before the release.

| | |
|---|---|
| ![The language view: strings on the left, the translation and a live preview of the screen on the right](docs/images/editor-language.png) | ![A screenshot with every marked text in German](docs/images/editor-screenshot.png) |

## How it fits together

```
your app's repository
├── src/                              Translate / translate("id", "English text") calls
└── i18n/
    ├── rcinel.json                   the project: languages, code folders, AI settings
    ├── translations/   ← ship this   en.json (from `rcinel scan`), de.json, fr.json, ...
    ├── review/                       who confirmed what, and for which English text
    └── context/                      notes and screenshots for translators
```

1. Write `Translate("", "English text")` (C#) or `translate("", "English text")` (Java,
   JavaScript) in the code.
2. `rcinel scan --assign` gives every new call its id (in your code) and writes the English texts to
   `translations/en.json`.
3. In the editor, start "Draft with AI" for each language. Translators go through the drafts,
   confirm or correct them, and see them on the screenshots.
4. Commit `i18n/` with the code. The translations ship with the release they belong to.
5. In CI, `rcinel check` keeps broken placeholders and malformed files out, and
   `rcinel check --require-confirmed` can block a release until everything is reviewed.

The format is described in [docs/format.md](docs/format.md).

## The clients

All three behave the same: load a language (with its fallback chain: `de-AT` reads `de.json` and then
`de-AT.json` on top), then translate. An id without a translation shows the English text from the
call, so nothing breaks while a language is incomplete. They run the same test cases
([spec/fixtures](spec/fixtures)), so a file reads the same everywhere.

### .NET

```
dotnet add package Rcinel.Translate
```

```csharp
// No using needed on .NET 6 and newer: the package imports the Rcinel class for the whole project.
// Reads translations/<language>.json next to the app.
TryLoad(CultureInfo.CurrentUICulture.Name);

title.Text = Translate("01a192e3-5bcc-7000-a8d2-327b1cb0f737", "Settings");
toast.Text = Translate("01a192e3-5bd4-7007-bb84-212f497c373e", "Saved {0} settings", count);

LanguageChanged += (_, _) => RefreshTexts();
```

The methods belong to the class `Rcinel` in the namespace `Rcinel.Translate`. On .NET 6 and newer
the package adds `global using static Rcinel.Translate.Rcinel;` to your project, so `Translate`,
`TryLoad`, `Load`, `Use` and `LanguageChanged` work in every file without a prefix or a using.

- **Older targets** (.NET Framework, netstandard, Unity and others on C# 7.3) don't know global
  usings and don't get it. Write `using static Rcinel.Translate.Rcinel;` at the top of each file
  instead.
- **Switch it off** if your project has its own `Translate` method, and use the per-file
  `using static` where you need it:

  ```xml
  <PropertyGroup>
    <RcinelTranslateGlobalUsing>false</RcinelTranslateGlobalUsing>
  </PropertyGroup>
  ```

A plain `using Rcinel.Translate;` imports only the types, not the methods. `Rcinel.Translate(...)`
doesn't compile in apps either, because C# reads `Rcinel` there as the namespace. Where a bare name
is ambiguous, the full name works: `Rcinel.Translate.Rcinel.Directory = ...` sets the folder the
files are read from.

Ship the files by copying the project's translations folder into the output:

```xml
<ItemGroup>
  <None Include="..\i18n\translations\*.json" LinkBase="translations" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

Or embed them and use `TranslationSet.FromStream`, then `Use(set)`.

### Java and Android

```kotlin
implementation("io.github.codeisnotevil:rcinel-translate:0.1.0")
```

```java
import static io.github.codeisnotevil.rcinel.Rcinel.translate;
import static io.github.codeisnotevil.rcinel.Rcinel.tryLoad;

// Reads translations/<language>.json from the class path (put the folder in src/main/resources).
tryLoad(Locale.getDefault().toLanguageTag());

title.setText(translate("01a192e3-5bcc-7000-a8d2-327b1cb0f737", "Settings"));
toast.setText(translate("01a192e3-5bd4-7007-bb84-212f497c373e", "Saved {0} settings", count));
```

Kotlin works the same way with `import io.github.codeisnotevil.rcinel.Rcinel.translate`. If you
prefer the class name in front, `Rcinel.translate(...)` works too.

On Android, put the files in `assets/translations/` and load them with
`TranslationSet.read(language, getAssets().open("translations/de.json"))` and `Rcinel.use(set)`.
Java 8 bytecode, no dependencies.

### JavaScript

```
npm install rcinel-translate
```

```js
import { translate, tryLoad } from 'rcinel-translate';

// Fetches translations/<language>.json relative to the page.
await tryLoad(navigator.language, { baseUrl: '/translations/' });

title.textContent = translate('01a192e3-5bcc-7000-a8d2-327b1cb0f737', 'Settings');
greeting.textContent = translate('01a192e3-5bd4-7008-9a54-036fafe0c7af', 'Welcome back, {name}!', { name });
```

Every function (`translate`, `load`, `tryLoad`, `use`, `useObject`, `reset`, `onChange`,
`language`) is a named export; the default export `Rcinel` groups the same functions for code that
prefers `Rcinel.translate(...)`. In Node, `import { useDirectory } from 'rcinel-translate/node'`
reads the files from disk. With a bundler you can also import the JSON and call
`useObject('de', de)`. TypeScript types are included.

## The `rcinel` command

```
dotnet tool install -g Rcinel.Translate.Cli
```

| Command | Does |
|---|---|
| `rcinel init i18n --languages de,fr --sources ../src` | Creates a project. |
| `rcinel scan [--assign]` | Collects the calls from the code into `en.json`. With `--assign`, calls with an empty id get a new UUIDv7, written into your code. |
| `rcinel status [--json]` | Progress per language: confirmed, draft, outdated, missing. |
| `rcinel check [--require-complete \| --require-confirmed] [--strict]` | Finds broken placeholders, invalid ids, translations of removed strings and files that aren't in canonical form. Exits with 1 on errors. |
| `rcinel format` | Rewrites the files in canonical form (after a hand edit or a merge). |
| `rcinel prune` | Removes translations of strings that are no longer in the code. |
| `rcinel add-language pt-BR --name "Português"` | Adds a language. |
| `rcinel export build/translations [--confirmed-only]` | Writes the files to ship, optionally without unreviewed text. |
| `rcinel draft de` | Drafts the missing and outdated strings of a language with Claude (needs `ANTHROPIC_API_KEY`). |
| `rcinel id [count]` | Prints new UUIDv7 ids. |

`rcinel` finds the project from any folder inside your app, like git finds `.git`.

```
$ rcinel status
Demo Notes: 14 strings in en
Language  Confirmed       Draft  Outdated  Missing
de        ###----...  35%      5         1        3
fr        -.........   0%      2         0       12
```

The scanner reads `Translate(...)` (C#) and `translate(...)` or `Rcinel.translate(...)` (Java,
Kotlin, JavaScript, TypeScript) calls whose id and text are plain string literals. Since other code
has functions called `translate` too, a bare call only counts when its first argument is `""` or a
UUID; the others are left alone. Anything it can't read, such as a
variable, `$"..."` or a template literal with `${}`, it reports, because a text the scanner can't
see can't be translated. If you wrap the call, add your wrapper's name to `functions` in
`rcinel.json`.

## The editor

A web app for the people who translate. It works directly on the `i18n` folder of a checkout of
your app, so its changes are ordinary file changes you review and commit.

- **Overview**: progress of every language, the results of `rcinel check`, adding languages.
- **Language view**: every string with its state (missing, draft, outdated, confirmed), filters and
  search. Confirm with Ctrl+Enter and go on to the next open string. Placeholders the translation
  lost are flagged before it can be confirmed.
- **AI drafts**: one click drafts every missing and outdated string of a language in the background.
  Strings that are marked on a screenshot are sent along with it, together with their notes, the
  language's style rules and the translations reviewers already confirmed (for consistent terms).
  Drafts are stored as drafts; only people confirm.
- **Screenshots**: upload screenshots of your app and drag a box over each text. The editor takes
  the background, text colour, size and alignment from the image. Every language can then be
  previewed on the screenshot, and a red outline marks text that needs a smaller font than the
  original to fit.
- **Users**: admins and translators, with passwords hashed with ASP.NET Core Identity's hasher.
  There's no self-registration.

### Run it with Podman (or Docker)

```fish
cp .env.example .env    # set RCINEL_PROJECT to your app's i18n folder, and an admin password
podman compose up -d --build
```

Then open http://localhost:8080. Without `RCINEL_ADMIN_USER`/`RCINEL_ADMIN_PASSWORD`, the first
account is created through a one-time setup link that `podman compose logs editor` prints. Under
rootless Podman the editor writes the project files as you. Under Docker, set `RCINEL_USER` to
`$(id -u):$(id -g)`. Always run `podman compose`, not the older `docker-compose`.

Put it behind a reverse proxy with HTTPS if it's reachable from outside your machine.

### Run it from source

```fish
cd dotnet
set -x Rcinel__Project ../samples/demo/i18n
set -x Rcinel__AdminUser admin
set -x Rcinel__AdminPassword a-long-password
set -x ANTHROPIC_API_KEY sk-ant-...   # optional, for AI drafts
dotnet run --project src/Editor       # http://localhost:5280
```

## Try it

[`samples/demo`](samples/demo) is a small notes app with a settings page in C# and a web page in
JavaScript, a project with German partly reviewed, French barely started and Brazilian Portuguese
empty, and a screenshot with all its texts marked. Point the editor at `samples/demo/i18n`.

## Building

| Part | Folder | Build and test |
|---|---|---|
| .NET client, core, CLI, editor | `dotnet/` | `dotnet test RcinelTranslate.slnx` (.NET 10 SDK) |
| Java client | `java/` | `./gradlew test` (JDK 17+) |
| JavaScript client | `javascript/` | `npm test` (Node 18+) |

The version is the same everywhere: `<Version>` in `dotnet/Directory.Build.props`,
`java/gradle.properties` and `javascript/package.json`, and CI checks they agree.

## Releasing

A `vX.Y.Z` tag publishes everything through the
[Release workflow](.github/workflows/release.yml). It first runs CI, which also checks that the tag
matches the three version numbers and that [CHANGELOG.md](CHANGELOG.md) has a dated section
(`## X.Y.Z - yyyy-mm-dd`). Then it publishes:

| Where | What | Secrets |
|---|---|---|
| NuGet | `Rcinel.Translate`, `Rcinel.Translate.Core`, the `Rcinel.Translate.Cli` tool | `NUGET_API_KEY`: an API key from nuget.org with push rights for `Rcinel.Translate*` |
| Maven Central | `io.github.codeisnotevil:rcinel-translate`, signed | `MAVEN_CENTRAL_USERNAME` and `MAVEN_CENTRAL_PASSWORD`: a user token from central.sonatype.com (the `io.github.codeisnotevil` namespace must be verified there, which GitHub accounts can do). `MAVEN_SIGNING_KEY`: an ASCII-armored GPG private key whose public key is on a keyserver, and `MAVEN_SIGNING_PASSWORD`: its passphrase |
| npm | `rcinel-translate`, with a provenance statement | `NPM_TOKEN`: an automation or granular token from npmjs.com |
| GitHub | a release with the changelog section as notes and the packages attached | none |

Add the secrets under the repository's Settings, Secrets and variables, Actions. A release whose
secret is missing stops with an error naming it. Re-running a release after a partial failure is
safe: versions that already reached NuGet or npm are skipped.

To release: set the version in the three files, date its CHANGELOG section, merge, then
`git tag v0.1.0 && git push origin v0.1.0`.

## License

MIT, see [LICENSE](LICENSE). The clients have no dependencies. The editor and the CLI use the
official [Anthropic C# SDK](https://github.com/anthropics/anthropic-sdk-csharp) (MIT) for AI drafts.
The Java build publishes with the
[Gradle Maven Publish Plugin](https://github.com/vanniktech/gradle-maven-publish-plugin)
(Apache 2.0), which is a build tool and not part of the published library.
