---
tags: [resource, gotcha]
created: 2026-10-31
updated: 2026-10-31
status: active
---
# Gotchas

- **Blazor in a container lost `_framework/blazor.web.js`.** With SDK 10.0.4xx, a `dotnet restore`
  of only the `.csproj` files (the usual layer-caching trick) doesn't add Blazor's framework scripts,
  and `dotnet publish --no-restore` then ships pages that never become interactive. The Dockerfile
  publishes without `--no-restore`. SDK 10.0.1xx didn't show it. See `dotnet/src/Editor/Dockerfile`.
- **Minimal API endpoints don't enforce antiforgery by themselves.** `UseAntiforgery()` only checks
  and records the result; an endpoint without form binding must call
  `IAntiforgery.IsRequestValidAsync` (the logout endpoint does, and a test covers it).
- **`scrollWidth` is a whole number.** Comparing it with a fractional width made every text "not fit"
  in the preview. `app.js` compares `scrollWidth` with the element's own `clientWidth` instead.
- **`[SupplyParameterFromForm]` with an initializer** fails the build (BL0008); initialise in
  `OnInitialized` with `??=`.
- **`@code` in Razor markup** is the directive, even as `@code` inside an attribute value. Don't name
  loop variables `code`.
- **Maven Central rate-limits** (HTTP 429) from shared CI or sandbox IPs; retry with back-off.

Related: [[rcineltranslate]]
