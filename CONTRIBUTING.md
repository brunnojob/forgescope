# Contributing

Active application logic is Java 21 and C#/.NET 10. Keep source examples, package boundaries and compiler diagnostics reproducible. Do not reintroduce a Python/Node subprocess or custom browser-side business logic.

Run the worker/workbench self-tests, `java tools/Dependencies.java`, and `java tools/Build.java test`. Set `FORGESCOPE_CSHARP_WORKER` to the built worker DLL for cross-runtime tests. CI checks both Windows and Linux.

Normal scans must not execute target builds, annotation processors, analyzers, source generators or code. Add negative cases for shadowing, invalid syntax, external references, ambiguous locations, malformed JSON, redaction and path traversal. Keep limits visible. Do not turn heuristic evidence into a correctness or security claim.

Preserve original project licenses and rule provenance when changing ports. Never commit private repositories, actual credentials or proprietary logs into the public examples.
