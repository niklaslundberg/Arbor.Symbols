# Copilot Instructions

Behavioral guidelines to reduce common LLM coding mistakes. Merge with project-specific instructions as needed.

**Tradeoff:** These guidelines bias toward caution over speed. For trivial tasks, use judgment.

## 1. Think Before Coding

**Don't assume. Don't hide confusion. Surface tradeoffs.**

Before implementing:
- State your assumptions explicitly. If uncertain, ask.
- If multiple interpretations exist, present them - don't pick silently.
- If a simpler approach exists, say so. Push back when warranted.
- If something is unclear, stop. Name what's confusing. Ask.

## 2. Simplicity First

**Minimum code that solves the problem. Nothing speculative.**

- No features beyond what was asked.
- No abstractions for single-use code.
- No "flexibility" or "configurability" that wasn't requested.
- No error handling for impossible scenarios.
- If you write 200 lines and it could be 50, rewrite it.

Ask yourself: "Would a senior engineer say this is overcomplicated?" If yes, simplify.

## 3. Surgical Changes

**Touch only what you must. Clean up only your own mess.**

When editing existing code:
- Don't "improve" adjacent code, comments, or formatting.
- Don't refactor things that aren't broken.
- Match existing style, even if you'd do it differently.
- If you notice unrelated dead code, mention it - don't delete it.

When your changes create orphans:
- Remove imports/variables/functions that YOUR changes made unused.
- Don't remove pre-existing dead code unless asked.

The test: Every changed line should trace directly to the user's request.

## 4. Goal-Driven Execution

**Define success criteria. Loop until verified.**

Transform tasks into verifiable goals:
- "Add validation" → "Write tests for invalid inputs, then make them pass"
- "Fix the bug" → "Write a test that reproduces it, then make it pass"
- "Refactor X" → "Ensure tests pass before and after"

For multi-step tasks, state a brief plan:
```
1. [Step] → verify: [check]
2. [Step] → verify: [check]
3. [Step] → verify: [check]
```

Strong success criteria let you loop independently. Weak criteria ("make it work") require constant clarification.

## 5. All Tests Must Pass Before Committing

**Never commit code that breaks the test suite.**

- Run `dotnet test Arbor.Symbols.slnx` and confirm it exits with no failures before every commit.
- A pre-commit Git hook is available to automate this check. Set it up once after cloning:
  ```
  ./scripts/install-hooks.sh
  ```
- `git commit --no-verify` bypasses the hook. Use it only in genuinely exceptional circumstances (e.g. committing a work-in-progress branch where tests are intentionally broken and you will fix them in the next commit). Never push to `main` with failing tests.
- If a pre-existing test was already failing before your changes, note it explicitly in the PR description rather than silently ignoring it.

## 6. Code Quality Requirements for New Work

- Treat compiler warnings, analyzer warnings, and runtime errors as real defects. Do not ignore or suppress them unless there is a documented and justified reason.
- Any new or changed production code must include test coverage. Prefer isolated unit tests first, then integration/E2E tests when unit tests are not sufficient.
- Maintain reasonably high coverage in the changed area. If code can be tested, add tests.
- For feature work, generate coverage reports and review them. CI must publish test and coverage outputs so they are visible during code review.
- Profiling-oriented validation is required when changing request execution hot paths, scheduled/background job loops, data-processing loops, or code that introduces disposable/resource-heavy objects.
- Treat code as a hot path when it runs on every request, for each item in a collection, or on a recurring timer. Profiling is optional for isolated admin/one-off flows.
- Use JetBrains dotMemory Unit or equivalent tools (for example `dotnet-counters` or BenchmarkDotNet) to catch memory leaks, performance bottlenecks, or resource leaks. Attach profiling evidence in the PR when this requirement applies.
- For UI-related changes, attach updated screenshots in the pull request so reviewers can verify visual impact.

---

*Behavioral guidelines adapted from [vlad-ko/claude-wizard](https://github.com/vlad-ko/claude-wizard), used under the [MIT License](https://github.com/vlad-ko/claude-wizard/blob/main/LICENSE) (Copyright 2026 Vlad Ko).*

## License Compatibility

This project is licensed under the **MIT License**. When adding new NuGet packages or any other third-party dependencies, you **must** verify that their licenses are compatible with MIT before including them.

### Compatible licenses (permitted)

The following license families are compatible with MIT and may be used freely:

- **MIT** — fully compatible
- **Apache-2.0** — compatible; requires attribution and inclusion of the Apache license notice when distributing
- **BSD-2-Clause / BSD-3-Clause** — compatible
- **ISC** — compatible
- **SIL Open Font License 1.1 (OFL-1.1)** — compatible for fonts embedded in software

### Incompatible or restricted licenses (requires review before use)

Do **not** add packages under these licenses without explicit approval:

- **GPL-2.0 / GPL-3.0** — copyleft; incompatible with MIT distribution
- **LGPL-2.1 / LGPL-3.0** — conditionally compatible only; requires careful evaluation
- **AGPL-3.0** — network copyleft; incompatible
- **SSPL** — incompatible
- **Proprietary / Commercial** — requires explicit legal approval
- **No license specified** — treat as "all rights reserved"; requires explicit approval

### Steps when adding a new NuGet package

1. Check the package's license on [NuGet.org](https://www.nuget.org) (the "License" field) or its GitHub/source repository.
2. Confirm the license is in the **Compatible** list above.
3. Add an entry to **`THIRD_PARTY_NOTICES.md`** in the repository root with:
   - Package name and version
   - Authors and copyright statement
   - Project URL
   - License identifier (SPDX expression preferred, e.g. `MIT`, `Apache-2.0`)
   - A note if the dependency is test-only or debug-only (not redistributed)
4. Update the package version in `Directory.Packages.props` (this project uses [Central Package Management](https://learn.microsoft.com/en-us/nuget/consume-packages/central-package-management)).
5. Do **not** add a `Version` attribute directly in any `.csproj` file.

### Example THIRD_PARTY_NOTICES.md entry

```markdown
## ExamplePackage

**Package:** ExamplePackage
**Version:** 1.2.3
**Authors:** Example Author
**Copyright:** Copyright © 2024 Example Author
**Project URL:** https://github.com/example/example
**License:** MIT
```

## MSIX Packaging and Releases

The desktop application (`Arbor.HttpClient.Desktop`) is distributed as a signed MSIX package.

### How it works

- The MSIX manifest template lives at `src/Arbor.HttpClient.Desktop/packaging/AppxManifest.xml`.
- `VERSION_PLACEHOLDER` in the manifest is substituted at build time with the 4-part version derived from the GitHub Actions run number (`1.0.{run_number}.0`).
- The release workflow (`.github/workflows/release.yml`) runs automatically on every push to `main`. It:
  1. Builds and runs unit tests on `windows-latest`.
  2. Publishes a `win-x64` self-contained executable via `dotnet publish`.
  3. Generates required MSIX logo assets using ImageMagick.
  4. Packages the publish output with `makeappx.exe` (Windows SDK, pre-installed on the runner).
  5. Creates a self-signed certificate and signs the MSIX with `signtool.exe`.
  6. Creates a GitHub Release with auto-generated release notes and uploads the MSIX and the `.cer` sideloading certificate as assets.

### When modifying the desktop app

- Keep the `AppxManifest.xml` consistent with the app identity (`NiklasLundberg.ArborHttpClient`, Publisher `CN=Arbor.HttpClient`).
- Do **not** change `ProcessorArchitecture` in the manifest without also changing the `-r` runtime identifier in the release workflow's `dotnet publish` step.
- The `Publisher` value in `AppxManifest.xml` must exactly match the `Subject` of the signing certificate. If you change the publisher, update both.
- Required MSIX logo sizes: `Square44x44Logo` (44×44), `Square150x150Logo` (150×150), `Wide310x150Logo` (310×150), `StoreLogo` (50×50), `SplashScreen` (620×300). Update the workflow if real brand assets replace the generated placeholders.

## Accessibility

All UI changes involving human interaction must consider accessibility from the start — not as an afterthought.

### Requirements

- **Color contrast**: Every foreground/background color pair used for text or interactive elements must meet [WCAG 2.1](https://www.w3.org/WAI/standards-guidelines/wcag/) Level AA:
  - ≥ 4.5:1 for normal text
  - ≥ 3:1 for large text (bold text ≥ 14 pt, or regular text ≥ 18 pt) and graphical/UI components
- **Theme consistency**: Colors must be defined per-theme (Dark/Light) in the `ResourceDictionary.ThemeDictionaries` section of `App.axaml` so that each variant meets the above ratios against its own backgrounds.
- **Contrast tests**: Any new color pair introduced in `App.axaml` must be covered by a corresponding test case in `AccessibilityContrastTests.cs` that asserts the WCAG contrast ratio.
- **Keyboard navigation**: Interactive controls (buttons, list items, text boxes) must be reachable and operable by keyboard alone.
- **Screen reader labels**: All non-decorative icons and images must carry an accessible name (e.g., `AutomationProperties.Name`).

### Verification checklist for UI pull requests

Before merging any PR that touches UI code or theme resources:

- [ ] All new/changed color pairs have been verified with the contrast-ratio formula in `AccessibilityContrastTests.cs` and meet WCAG AA.
- [ ] Interactive elements remain keyboard-accessible (Tab, Enter/Space, arrow keys where applicable).
- [ ] No purely visual text label has been replaced by an icon without an accessible name.

## Central Package Management

All NuGet package versions are managed centrally in `Directory.Packages.props`.  
- Always declare new packages with `<PackageVersion Include="PackageName" Version="x.y.z" />` in `Directory.Packages.props`.  
- Reference packages in `.csproj` files using `<PackageReference Include="PackageName" />` **without** a `Version` attribute.  
- Shared MSBuild properties (`TargetFramework`, `Nullable`, `ImplicitUsings`, etc.) live in `Directory.Build.props` and are inherited by all projects automatically.
