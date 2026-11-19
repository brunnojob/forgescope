# Migration from 0.2 to 0.3

## Application languages

Python CLI, Python analysis modules, JavaScript bridges and Node engines have been removed from the active source tree. Their prior implementations remain in Git history. All active application logic is Java or C#/.NET. HTML/CSS and JSON remain data/presentation formats.

## Command mapping

| 0.2 | 0.3 |
| --- | --- |
| `python -m forgescope REPO` | `java -jar build/forgescope.jar REPO` |
| `python -m forgescope.validation REPO PATCH --test-command JSON` | `java -jar build/forgescope.jar validate REPO PATCH --test-command JSON` |
| Node bridge | Java rule ports; no Node process |
| JavaScript report filters | Local C# workbench, server-rendered GET forms |
| Interactive Pages report | Portable static HTML on Pages; interactive .NET app in localhost |

## Analysis boundaries

Java and C# have native compiler symbol analysis. External build references are not loaded. Python compatibility is deliberately limited to lexical imports: no claim of parity with the former Python AST/scope implementation is made. Keep using a historical checkout if that specific Python analysis is required until a JVM parser implementation is added.

Review rule ports are not byte-compatible with upstream Node engines. Findings can receive different wording and IDs, so a first 0.3 baseline comparison can show new/absent entries without a corresponding code change.

## Existing data

JSON keeps `schema_version: 1` and the common report fields. Existing reports remain viewable. SQLite keeps the `runs(id, created_at, project, report)` table: no destructive migration or replacement is performed. JDBC libraries are needed only when using `--history`.

## Distribution

Build with JDK 21 and .NET 10. CI publishes Java and .NET build outputs on both Windows and Linux. The .NET worker path is explicit; Java reports unavailable analysis when C# sources are present without it. No dependency is downloaded by a normal scan.
