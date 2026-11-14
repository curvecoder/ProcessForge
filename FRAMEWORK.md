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

* **Cross-Platform Inspection**: Resolution of executable absolute paths, complete command lines (`/proc/<pid>/cmdline` on Linux, `NtQueryInformationProcess(ProcessCommandLineInformation)` on Windows 8.1 and later, which only needs `PROCESS_QUERY_LIMITED_INFORMATION`; empty for protected processes or without the right), and the account running the process (`UserName`): the access token of the process on Windows (`DOMAIN\account`, the SID when the account no longer has a name), the real UID of `/proc/<pid>/status` translated with `/etc/passwd` on Linux (the numeric UID when the account is not listed). On Windows the security identifier of that account is also exposed (`UserSid`, for example `S-1-5-18`), since account names are translated with the display language of the system. The account stays empty when the process cannot be opened (protected process, insufficient rights).
* **Wrapper Resolution**: Extracts underlying application targets (e.g., `.jar` archives executed via `java -jar`, the first command-line token ending with an extension of the host: `.bat`/`.cmd`/`.ps1`/`.sh`/`.py` for `cmd`, `powershell`, `pwsh`, `bash` and `sh`; `.py` for `py` and any process whose name starts with `python`; `.js`/`.mjs`/`.cjs` for `node`; `.dll` for `dotnet`; the extension must end the token, so `foo.shared` is not a `.sh` script). Tokens are separated by blanks and quotes, so a path containing a space only keeps its part after the last space, and an option value such as `node --require hook.js app.js` is taken for the target.
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
