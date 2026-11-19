# ForgeScope investigation

Java 21 + .NET 10 · sample

## Review this workflow privilege boundary

patchscope · .github/workflows/ci.yml:1

Broad permissions or pull_request_target added

Check token scope and whether untrusted code can run.

## Dynamic code or process execution was added

patchscope · java/src/shop/Pricing.java:3

Execution boundary changed

Trace the input, argument boundaries and trust assumptions.

## Dynamic code or process execution was added

patchscope · csharp/Pricing.cs:4

Execution boundary changed

Trace the input, argument boundaries and trust assumptions.

## GitHub Actions behavior changed

patchscope · .github/workflows/ci.yml:

Workflow file changed

Review triggers, permissions and action pinning.

## Confirm behavior is covered

patchscope · java/src/shop/Pricing.java:

Source changed without changed test files in this patch

Check existing tests or add a regression case. This does not measure coverage.

## Confirm behavior is covered

patchscope · csharp/Pricing.cs:

Source changed without changed test files in this patch

Check existing tests or add a regression case. This does not measure coverage.

## Review a module without a matching test name

reporelief · csharp/Legacy.cs:

No conventional test filename match

Find existing coverage or add a behavior test; this is not measured coverage.

## Review a module without a matching test name

reporelief · csharp/Pricing.cs:

No conventional test filename match

Find existing coverage or add a behavior test; this is not measured coverage.

## Review a module without a matching test name

reporelief · java/src/shop/Api.java:

No conventional test filename match

Find existing coverage or add a behavior test; this is not measured coverage.

## Review a module without a matching test name

reporelief · java/src/shop/Legacy.java:

No conventional test filename match

Find existing coverage or add a behavior test; this is not measured coverage.

## Review a module without a matching test name

reporelief · java/src/shop/Pricing.java:

No conventional test filename match

Find existing coverage or add a behavior test; this is not measured coverage.

## Java compilation failed

cifuse · java/src/shop/Legacy.java:4

java/src/shop/Legacy.java:4: error: cannot find symbol

Use the file and line diagnostics. Check package paths, declarations and the JDK/toolchain configured in Maven or Gradle.

## C# or MSBuild diagnostic

cifuse · csharp/Legacy.cs:5

csharp/Legacy.cs(5,43): error CS0117: &#39;Pricing&#39; does not contain a definition for &#39;TotalV1&#39;

Inspect the compiler diagnostic, target framework and references. Reproduce using the same .NET SDK as CI.

## Java compilation failed

devrescue · java/src/shop/Legacy.java:4

java/src/shop/Legacy.java:4: error: cannot find symbol

Use the file and line diagnostics. Check package paths, declarations and the JDK/toolchain configured in Maven or Gradle.

## C# or MSBuild diagnostic

devrescue · csharp/Legacy.cs:5

csharp/Legacy.cs(5,43): error CS0117: &#39;Pricing&#39; does not contain a definition for &#39;TotalV1&#39;

Inspect the compiler diagnostic, target framework and references. Reproduce using the same .NET SDK as CI.

## Review an unresolved local reference

impact · java/src/shop/Legacy.java:4

Pricing.totalV1: Invocation did not resolve against a supplied local source type; missing dependencies can affect binding.

Inspect the declaration, signature, imports and missing dependency diagnostics. This is not a proof of root cause.

## Review an unresolved local reference

impact · csharp/Legacy.cs:5

Pricing.TotalV1: Call did not resolve against a supplied local C# type. Review overloads, signatures and missing references.

Inspect the declaration, signature, imports and missing dependency diagnostics. This is not a proof of root cause.

## Make checks repeatable

reporelief · :

No GitHub workflow found

Add a workflow for the real test/build commands.

## Document usage

reporelief · README.md:

Section keywords not found

Add commands verified by a maintainer; keyword matching is heuristic.

## Clarify reuse permissions

reporelief · :

No root license file found

Choose and document an appropriate license.

## Repair a missing local Markdown target

reporelief · README.md:5

docs/setup.md -&gt; docs/setup.md

Restore the target or correct the relative link.
