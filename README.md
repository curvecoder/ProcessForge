# ProcessForge

Cross-platform .NET process inspection framework for extracting command-line metadata, resolving underlying execution targets (Java wrappers, script interpreters), and evaluating exclusion rules.

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Framework](https://img.shields.io/badge/.NET-8.0-purple.svg)](https://dotnet.microsoft.com/)
[![Documentation: English](https://img.shields.io/badge/Docs-English-blue.svg)](FRAMEWORK.md)
[![Documentation: Français](https://img.shields.io/badge/Docs-Français-green.svg)](FRAMEWORK.fr.md)

> 📖 **Framework Documentation**: [English Specification](FRAMEWORK.md) | [Spécification en français](FRAMEWORK.fr.md)

---

## Key Capabilities

* **Cross-Platform Process Inspection**: Inspects active processes across Windows (`NtQueryInformationProcess`, Windows 8.1 and later) and Linux (`/proc/<pid>/cmdline`), resolving executable paths, process names, command lines, and owner accounts (name and, on Windows, SID).
* **Execution Wrapper Target Resolution**: Automatically inspects command-line arguments to determine the true underlying application target:
  * Java wrappers: extracts target `.jar` files executed via `java -jar app.jar`, quoted or not (a JAR given to `-cp` is a library, not the target).
  * Script interpreters: extracts the script or assembly run by a shell (`cmd`, `powershell`, `pwsh`, `bash`, `sh`: `.bat`, `.cmd`, `.ps1`, `.sh`, `.py`), Python (`python*`, `py`: `.py`), `node` (`.js`, `.mjs`, `.cjs`) or `dotnet` (`.dll`).
* **Multi-Criteria Dynamic Filtering (`ProcessExclusionFilter`)**:
  * Evaluates exclusions by process numeric ID (PID), exact name, prefix, suffix, and substring matches.
  * Filters out operating system service accounts (well-known SIDs `S-1-5-18`, `S-1-5-19`, `S-1-5-20` on any Windows language, `SYSTEM`, `NT AUTHORITY`, `root`).
  * Thread-safe on-the-fly rule additions (`ExcludeExactName`).

---

## Installation

Add the package via the .NET CLI:

```powershell
dotnet add package ProcessForge --version 1.1.2
```

---

## Code Example

```csharp
using ProcessForge.Filtering;
using ProcessForge.Inspectors;

var inspector = new CrossPlatformProcessInspector();
var filter = new ProcessExclusionFilter(new ProcessExclusionOptions());
filter.ExcludeExactName("svchost");

var details = await inspector.ResolveProcessAsync(4812);
if (!filter.ShouldExclude(details))
{
    Console.WriteLine($"Process: {details.ProcessName}");
    Console.WriteLine($"Command Line: {details.CommandLine}");
    Console.WriteLine($"Target Executable: {details.TargetExecutable}");
}
```

---

## License

This project is licensed under the [MIT License](LICENSE).
