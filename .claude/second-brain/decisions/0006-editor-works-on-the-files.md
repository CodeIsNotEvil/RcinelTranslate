---
tags: [decision, editor]
created: 2026-10-31
updated: 2026-10-31
status: active
supersedes:
---
# 0006: The editor works on the project files; users in a JSON file

**Context.** Translations must stay versioned with the code. The editor is used by a handful of
people.

**Decision.** The editor (Blazor with server interactivity) loads the project folder of a git
checkout and writes to it through Core's canonical writer; committing is left to people. One lock
serializes access (`ProjectStore`); it reloads when files change on disk and refuses to save over a
file it can't read (a merge conflict). Users and sign-in keys are in a separate data folder, so
password hashes stay out of the app's repository. The first admin comes from configuration or a
one-time setup link printed in the log.

**Alternatives.** *A database as the source of truth with export*: two sources of truth and an
export step to forget. *Blazor WebAssembly plus an API*: twice the code for one small team's tool.
*ASP.NET Core Identity with EF*: a database for five users.

**Consequences.** One editor instance per project folder. Changes appear as file changes in git,
reviewable like code. Scaling to many concurrent teams would need something else.

Related: [[0007-ai-drafts-never-confirm]]
