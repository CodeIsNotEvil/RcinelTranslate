---
tags: [decision, dotnet, conventions]
created: 2026-10-31
updated: 2026-10-31
status: active
supersedes:
---
# 0008: Namespaces `CINE.Translate.*`, packages `Rcinel.Translate.*`

**Context.** All of the owner's .NET code lives under the `CINE` root namespace (MyMonoRepo's
decision 0004). The public call is `Rcinel.Translate(...)`, a static class named `Rcinel`.

**Decision.** `Directory.Build.props` replaces the `Rcinel.` prefix of the project name with `CINE.`:
`Rcinel.Translate.Core` gets the namespace `CINE.Translate.Core`. Package ids and assembly names stay
`Rcinel.Translate.*`, which is what NuGet users see.

**Alternatives.** *`CINE.$(MSBuildProjectName)`*, which gives `CINE.Rcinel.Translate`: inside any
namespace under `CINE` (all of the owner's apps), `Rcinel` would bind to the namespace `CINE.Rcinel`
before the using directive is considered, and `Rcinel.Translate(...)` would not compile there.

**Consequences.** Users write `using CINE.Translate;`. The Java package is
`io.github.codeisnotevil.rcinel` and the npm package `rcinel-translate`, by those ecosystems' conventions.

Related: [[0004-dependency-free-clients-with-shared-fixtures]]
