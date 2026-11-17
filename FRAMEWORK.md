<p align="right">
  <b>English</b> | <a href="FRAMEWORK.fr.md">Français</a>
</p>

# ProcessForge — Framework Specification & AI Documentation

This document is the technical manifest intended for AI agents and software engineers. It outlines all capabilities, architectural components, and public contracts exposed by **ProcessForge**.

---

## 🎯 Scope & Responsibility

ProcessForge is a cross-platform .NET system engineering library dedicated to **process inspection, execution metadata analysis, and fine-grained resolution of underlying execution targets** (Java wrappers, script interpreters including CMD, PowerShell, Bash, and Python).

---

## 🚀 Key Capabilities

* **Cross-Platform Inspection**: Resolution of executable absolute paths (on Windows through `NtQuerySystemInformation(SystemProcessIdInformation)`, which needs no handle on the process: the path is known for a service running under another account and for protected processes, whatever the elevation of the caller; the NT device path is translated to its drive letter, a UNC path for the network redirector, or a `\\?\GLOBALROOT\Device\…` path for a volume without letter; empty for the processes without image file: Idle, System, Secure System, Registry; `/proc/<pid>/exe` on Linux, empty for another user's process without root), complete command lines (`/proc/<pid>/cmdline` on Linux, `NtQueryInformationProcess(ProcessCommandLineInformation)` on Windows 8.1 and later, which only needs `PROCESS_QUERY_LIMITED_INFORMATION`; empty for protected processes or without the right; returned on a single line, each run of NUL separators, line breaks, tabs, other control characters or whitespace other than the space becoming one space), and the account running the process (`UserName`): the access token of the process on Windows (`DOMAIN\account`, the SID when the account no longer has a name), the real UID of `/proc/<pid>/status` translated with `/etc/passwd` on Linux (the numeric UID when the account is not listed). On Windows the security identifier of that account is also exposed (`UserSid`, for example `S-1-5-18`), since account names are translated with the display language of the system. The account stays empty when the process cannot be opened (protected process, insufficient rights).
* **Cross-Scan Caching**: `CachingProcessInspector` decorates any `IProcessInspector` (`new CachingProcessInspector(new CrossPlatformProcessInspector(), capacity: 4096)`) and keeps the resolved metadata keyed by PID and process start time (creation time from `GetProcessTimes` on Windows, field 22 of `/proc/<pid>/stat` on Linux), so a PID reused by a new process is resolved again. A process whose start time cannot be read (exited, protected or not openable without elevation, hidden by `/proc`, other operating systems) is resolved on every call and never cached; the start time is read again after resolution and the result is cached only if it is unchanged. Metadata is kept as first resolved (an account renamed afterwards keeps its old name, an owner or command line that could not be read stays empty until the PID is reused); the cache is cleared when it reaches its capacity, which should exceed the number of live processes. Thread-safe; keep one instance for the scanner's lifetime and still deduplicate PIDs within a scan. Measured on Windows (not elevated, 48 distinct PIDs): 13.7 ms per scan with a per-scan cache only, 2.1 ms at steady state with the decorator. The gain on Linux has not been measured. A cache of SID-to-account translations was measured and not retained: at steady state no translation runs, since the processes whose start time cannot be read have no readable owner either.
* **Wrapper Resolution**: Extracts underlying application targets (e.g., `.jar` archives executed via `java -jar`: the token ending with `.jar` that follows `-jar` or another `.jar`, so a JAR given to `-cp` is not the target and a JAR path containing a space is not resolved, the first command-line token ending with an extension of the host: `.bat`/`.cmd`/`.ps1`/`.sh`/`.py` for `cmd`, `powershell`, `pwsh`, `bash` and `sh`; `.py` for `py` and any process whose name starts with `python`; `.js`/`.mjs`/`.cjs` for `node`; `.dll` for `dotnet`; the extension must end the token, so `foo.shared` is not a `.sh` script). Tokens are separated by blanks and quotes, so a path containing a space only keeps its part after the last space, and an option value such as `node --require hook.js app.js` is taken for the target.
* **Multi-Criteria Filtering**: Evaluation engine (`ProcessExclusionFilter`) matching by PID, exact name, prefix, suffix, substring (`Contains`), and system execution accounts (well-known SIDs `S-1-5-18`, `S-1-5-19`, `S-1-5-20` whatever the system language, and the names `SYSTEM`, `NT AUTHORITY`, `root`), considering process name, executable path, and underlying target (`TargetExecutable`).
* **Thread-Safe Dynamic Exclusion**: Method `ExcludeExactName(string processName)` allows dynamic runtime additions of process or target exclusions without rebuilding the filter.

---

## 📋 Maintenance Protocol for AI Agents

All enhancements to ProcessForge must comply with:
1. **Backward Compatibility:** Preserve all existing public contracts. Use overloads or optional parameters.
2. **Code Cleanliness:** Zero warnings (`<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`), zero pragmas/suppressions.
3. **Testing Standards:** Test naming convention `TargetMethod_ScenarioOrCondition_ExpectedResult`.
4. **Documentation:** Update both `FRAMEWORK.md` and `FRAMEWORK.fr.md` immediately upon introducing or modifying capabilities.
5. **Validation:** Full pass of `./verify.ps1`.
