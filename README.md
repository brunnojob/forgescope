<div align="center">

# ForgeScope

### Evidence before edits.

**Java 21 core. C#/.NET 10 workbench and Roslyn analysis.**

[Static example report](https://brunnojob.github.io/forgescope/) · [Português](README.pt-BR.md) · [Architecture](docs/ARCHITECTURE.md) · [Migration notes](docs/MIGRATION-0.3.md)

[![CI](https://github.com/brunnojob/forgescope/actions/workflows/ci.yml/badge.svg)](https://github.com/brunnojob/forgescope/actions/workflows/ci.yml)
![Java](https://img.shields.io/badge/Java-21-ed8b00)
![.NET](https://img.shields.io/badge/.NET-10-512bd4)
![MIT](https://img.shields.io/badge/license-MIT-a5edb1)

</div>

Changing unfamiliar code means reconstructing dependencies, reviewing patches, reading CI logs and finding maintenance work across separate tools. ForgeScope puts those evidence streams into one local investigation report.

**Version 0.3 migrates application logic to Java and C#. Python and Node are no longer runtime or build requirements.** HTML/CSS remain presentation formats. The portable report uses no JavaScript; the interactive localhost workbench runs its application logic in C# on ASP.NET Core.

## What is implemented

| Capability | Implementation | Boundary |
| --- | --- | --- |
| Java impact | JDK compiler AST and source symbol bindings, with annotation processing disabled | Isolated source compilation; not the target project's build graph |
| C# impact | Roslyn syntax and semantic models in a .NET worker | No MSBuild evaluation, project analyzers or source generators |
| Review prompts | Java ports of PatchScope rules | Heuristic diff review, not a security audit |
| CI diagnostics | Java ports of CIFuse/DevRescue rules, plus Java, C#, MSBuild, .NET SDK and NuGet diagnostics | Pattern matches and source links, not proof of root cause |
| Maintenance | Java implementation of repository essentials, relative Markdown targets, maintenance markers and conventional test naming | Heuristics; no measured test coverage |
| Analysis guidance | RevScope catalog snapshot and artifact signals | No binary execution, decompiler or firmware extractor |
| Reports and history | Java HTML/JSON/Markdown/SARIF exporters and optional SQLite JDBC history | Reports retain derived evidence; review them before sharing |
| Local interface | C# ASP.NET Core, server-rendered filters, dependency paths, EN/PT, light/dark themes, exports and a local scan form | Binds only to 127.0.0.1; configured repository only; no cloud account |

An import/reference path shows potential impact. A shared file or missing symbol does not independently prove that a change caused a failure. External dependencies absent from the isolated compiler environment produce visible diagnostics rather than invented certainty.

## Build and run

Requires a **JDK 21** and **.NET 10 SDK**. Git is required for `--base` and patch validation. The JDK source launcher is sufficient; Maven is optional.

```sh
git clone https://github.com/brunnojob/forgescope.git
cd forgescope

dotnet build dotnet/ForgeScope.Worker -c Release
dotnet build dotnet/ForgeScope.Workbench -c Release

# Optional SQLite dependencies: explicit download with pinned SHA-256 checks
java tools/Dependencies.java
java tools/Build.java test
```

The .NET worker references Roslyn shipped with the SDK. No third-party NuGet packages are needed. The explicit Java dependency step downloads SQLite JDBC and SLF4J from Maven Central into `build/lib`; a normal scan never downloads anything. The Java compiler and report features work without those JARs; `--history` requires them.

Generate the reproducible Java/C# fixture report:

```sh
java -jar build/forgescope.jar examples/sample --changed java/src/shop/Pricing.java --changed csharp/Pricing.cs --diff examples/change.diff --log examples/build.log --csharp-worker dotnet/ForgeScope.Worker/bin/Release/net10.0/ForgeScope.Worker.dll --output .forgescope --fail-on engine-error
```

Open `.forgescope/index.html` directly for a portable, offline report. For the full local interface:

```sh
dotnet dotnet/ForgeScope.Workbench/bin/Release/net10.0/ForgeScope.Workbench.dll --report .forgescope/report.json --repo examples/sample --jar build/forgescope.jar --worker dotnet/ForgeScope.Worker/bin/Release/net10.0/ForgeScope.Worker.dll
```

Visit **http://127.0.0.1:8765**. Use the GET form to filter findings, select a source file, choose English/Portuguese or switch theme. The POST scan form uses a random nonce, checks browser origins and invokes the Java CLI with explicit arguments, without a shell. It can scan only the repository configured at startup. Omit `--repo` and `--jar` for read-only report viewing. To scan the non-Git fixture, clear the Git base field and provide one of its source paths.

GitHub Pages hosts only the static fixture report. It cannot run Java, ASP.NET Core, Roslyn or SQLite. All interactive scans happen locally.

### Your repository

```sh
java -jar build/forgescope.jar /path/to/project --base HEAD~1 --csharp-worker dotnet/ForgeScope.Worker/bin/Release/net10.0/ForgeScope.Worker.dll --output .forgescope
java -jar build/forgescope.jar /path/to/project --changed src/shop/Pricing.java --symbol shop.Pricing.total --output .forgescope
java -jar build/forgescope.jar /path/to/project --baseline previous/report.json --history history.sqlite --output next-report
```

`--base` compares a local commit to tracked working-tree content, including staged changes, relative to the selected directory. It is not a merge-base comparison. Untracked files require `--changed`. External diff/textconv drivers and fsmonitor hooks are disabled. `--diff` accepts a supplied unified patch and is mutually exclusive with `--base`.

Each run writes `report.json`, `report.md`, `report.sarif` and `index.html`. `--fail-on high` returns 1 for high-priority findings; `--fail-on engine-error` returns 2 when a required analyzer fails or is unavailable. C# files without an explicit worker are reported as unavailable. Python files use limited lexical import compatibility and are labeled accordingly.

### Optional Maven build

```sh
mvn package
java -jar target/forgescope-0.3.0.jar /path/to/project --history history.sqlite
```

Maven runs the Java regression checks and produces a shaded JAR with SQLite dependencies. The source-launcher build and CI also verify Java-to-Roslyn integration. No Maven invocation is required when using `tools/Build.java`.

## Validate a reviewed patch

Use a clean, committed **trusted** Git repository. This separate command intentionally executes your explicit test argv before/after applying a supplied patch in a disposable detached worktree:

```sh
java -jar build/forgescope.jar validate /path/to/trusted/project /path/to/reviewed.patch --test-command '["dotnet","test","--no-restore"]' --output validation.json
```

Success requires failing tests before and passing tests after. The original checkout is preserved. The temporary checkout is cleaned between runs. This isolates edits, **not execution privileges**: tests can access files and networks, and process-tree termination is best effort. No log-derived commands, generated repairs or automatic PR publication are used.

## Honest migration boundaries

- Java and C# analysis now resolves symbols through their compiler APIs. Runtime dispatch, reflection, external project references and generated code remain incomplete.
- The former Python AST implementation is preserved in Git history. Version 0.3 offers import-only lexical Python compatibility; it does not claim equivalent Python function/scope analysis. See [migration notes](docs/MIGRATION-0.3.md).
- Ports preserve the investigation workflow, not every upstream implementation detail or byte-identical output. Rule wording and fingerprints can differ across the 0.2/0.3 boundary.
- SQLite retains the existing `runs` table schema. Existing history is not replaced, migrated or deleted. Old JSON reports remain readable by the local workbench.
- Inventory skips symlinks, `.env` names, common generated directories, binary/invalid UTF-8 and files over 512 KB; text scans are capped at 10,000 files / 20 MB. Logs: 20 files / 2 MB total. Patches: 2 MB / 500 sections / 60,000 lines.
- Redaction is pattern based and incomplete. Reports can contain sensitive filenames and evidence snippets. No quality, security or accuracy percentage is invented.

## Verify

```sh
dotnet dotnet/ForgeScope.Worker/bin/Release/net10.0/ForgeScope.Worker.dll --self-test
dotnet dotnet/ForgeScope.Workbench/bin/Release/net10.0/ForgeScope.Workbench.dll --self-test
java tools/Build.java test
```

To include cross-runtime protocol checks in the Java test process, set `FORGESCOPE_CSHARP_WORKER` to the built worker DLL. CI does this on Windows and Linux, with JDK 21, .NET 10 and SQLite dependencies. The CI artifacts contain the Java JAR, JDBC libraries, C# worker/workbench outputs and derived reports.

## Credits

Original project references and pinned commits are in [provenance.json](src/main/resources/provenance.json). Original MIT notices are retained in [third_party/licenses](third_party/licenses); dependency notices are in [third_party/README.md](third_party/README.md). PatchScope, CIFuse, RepoRelief, DevRescue and RevScope remain independent repositories.

God first. Build useful things, and make the evidence inspectable.
