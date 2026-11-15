<p align="right">
  <a href="FRAMEWORK.md">English</a> | <b>Français</b>
</p>

# ProcessForge — Spécification & Documentation IA du Framework

Ce document est le manifeste technique destiné à l'agent IA et aux développeurs. Il récapitule l'ensemble des capacités, composants et contrats publics exposés par **ProcessForge**.

---

## 🎯 Périmètre & Responsabilité

ProcessForge est le framework partagé .NET d'**inspection système, d'analyse de métadonnées de processus et de résolution fine des cibles d'exécution réelles** (wrappers Java, scripts CMD, PowerShell, Bash, Python).

---

## 🚀 Capacités Clés

* **Inspection multiplateforme** : Résolution du chemin absolu de l'exécutable, ligne de commande (`/proc/<pid>/cmdline` sous Linux, `NtQueryInformationProcess(ProcessCommandLineInformation)` sous Windows 8.1 et ultérieur, qui ne demande que `PROCESS_QUERY_LIMITED_INFORMATION` ; vide pour un processus protégé ou sans ce droit ; rendue sur une seule ligne, chaque suite de séparateurs NUL, retours à la ligne, tabulations, autres caractères de contrôle ou blancs autres que l'espace devenant une espace), et compte qui exécute le processus (`UserName`) : jeton d'accès du processus sous Windows (`DOMAINE\compte`, ou le SID quand le compte n'a plus de nom), UID réel de `/proc/<pid>/status` traduit par `/etc/passwd` sous Linux (l'UID numérique quand le compte n'y figure pas). Sous Windows, le SID de ce compte est aussi exposé (`UserSid`, par exemple `S-1-5-18`), car les noms de comptes sont traduits dans la langue d'affichage du système. Le compte reste vide quand le processus ne peut pas être ouvert (processus protégé, droits insuffisants).
* **Cache entre les scans** : `CachingProcessInspector` décore tout `IProcessInspector` (`new CachingProcessInspector(new CrossPlatformProcessInspector(), capacity: 4096)`) et conserve les métadonnées résolues, indexées par PID et date de démarrage du processus (date de création lue par `GetProcessTimes` sous Windows, champ 22 de `/proc/<pid>/stat` sous Linux), si bien qu'un PID réutilisé par un nouveau processus est résolu à nouveau. Un processus dont la date de démarrage est illisible (terminé, protégé ou non ouvrable sans élévation, masqué par `/proc`, autres systèmes) est résolu à chaque appel et jamais mis en cache ; la date de démarrage est relue après la résolution et le résultat n'est mis en cache que si elle n'a pas changé. Les métadonnées restent celles de la première résolution (un compte renommé ensuite garde son ancien nom, un propriétaire ou une ligne de commande illisible reste vide jusqu'à la réutilisation du PID) ; le cache est vidé quand il atteint sa capacité, qui doit dépasser le nombre de processus vivants. Sûr entre threads ; garder une seule instance pendant la vie du scanner et dédupliquer quand même les PID au sein d'un scan. Mesure sous Windows (non élevé, 48 PID distincts) : 13,7 ms par scan avec un cache limité au scan, 2,1 ms en régime établi avec le décorateur. Le gain sous Linux n'a pas été mesuré. Un cache des traductions SID vers compte a été mesuré et non retenu : en régime établi aucune traduction n'a lieu, car les processus dont la date de démarrage est illisible n'ont pas non plus de propriétaire lisible.
* **Résolution de wrappers** : Extraction de la cible applicative sous-jacente (ex: fichier `.jar` exécuté via `java -jar` : l'élément se terminant par `.jar` qui suit `-jar` ou un autre `.jar`, si bien qu'un JAR passé à `-cp` n'est pas la cible et un chemin de JAR contenant une espace n'est pas résolu, premier élément de la ligne de commande qui se termine par une extension de l'hôte : `.bat`/`.cmd`/`.ps1`/`.sh`/`.py` pour `cmd`, `powershell`, `pwsh`, `bash` et `sh` ; `.py` pour `py` et tout processus dont le nom commence par `python` ; `.js`/`.mjs`/`.cjs` pour `node` ; `.dll` pour `dotnet` ; l'extension doit terminer l'élément, si bien que `foo.shared` n'est pas un script `.sh`). Les éléments sont séparés par les blancs et les guillemets : un chemin contenant une espace ne garde que sa partie après la dernière espace, et la valeur d'une option comme `node --require hook.js app.js` est prise pour la cible.
* **Filtrage multicritère** : Moteur d'évaluation (`ProcessExclusionFilter`) par PID, nom exact, préfixe, suffixe, sous-chaîne (`Contains`) et filtrage des comptes système (SID connus `S-1-5-18`, `S-1-5-19`, `S-1-5-20` quelle que soit la langue du système, et noms `SYSTEM`, `NT AUTHORITY`, `root`), en tenant compte du nom de processus, du fichier exécutable et de la cible réelle (`TargetExecutable`).
* **Exclusion dynamique thread-safe** : Méthode `ExcludeExactName(string processName)` permettant d'ajouter des exclusions de processus ou de cibles à chaud au cours de l'exécution sans recréer le filtre.

---

## 📋 Protocole de Maintenance par l'Agent IA

Toute modification de ProcessForge doit respecter :
1. **Rétrocompatibilité :** Conserver tous les contrats publics existants. Utiliser des surcharges ou des paramètres optionnels.
2. **Qualité :** Zéro warning (`<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`), zéro pragma/suppress.
3. **Tests :** Nommage `MethodeCible_ScenarioOuCondition_ResultatAttendu`.
4. **Mise à jour :** Compléter immédiatement ce fichier `FRAMEWORK.md` en cas d'ajout d'une nouvelle capacité.
5. **Validation :** Valider par `./verify.ps1`.
