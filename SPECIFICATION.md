# Scrubx — Spécification technique

## 1. Objectif

Scrubx.Cli est un outil en ligne de commande .NET qui analyse un fichier `.docx`
(Word/OOXML) et détecte des problèmes de mise en forme typographique et
structurelle (apostrophes droites, tirets invalides, espaces insécables
manquantes, styles de paragraphe non autorisés, sauts de page, etc.).

Il retourne un rapport texte en console avec un code de sortie reflétant le
résultat de la validation, pour un usage en CLI ou en intégration CI (ex. :
vérification qualité avant publication d'un document).

## 2. Architecture

Projet mono-solution, deux projets :

```
src/Scrubx.Cli/
  Program.cs           — point d'entrée, affichage du rapport
  ArgumentParser.cs     — parsing des arguments CLI
  DocxValidator.cs      — cœur de la logique de validation
tests/Scrubx.Tests/
  DocxValidatorTests.cs — tests du validateur (xUnit)
  CommandLineTests.cs   — tests du parseur d'arguments
```

- Cible : `net10.0`, nullable activé, publication en binaire standalone
  (`PublishSingleFile`, `PublishReadyToRun`, `SelfContained`) via
  `publish.sh` / `publish.ps1` (runtimes : `osx-arm64`, `win-x64`, `linux-x64`).
- Pas de dépendance externe hors BCL (`System.IO.Compression`,
  `System.Xml.Linq`) : le `.docx` est lu directement comme une archive ZIP
  OOXML, sans passer par `DocumentFormat.OpenXml`.

## 3. Fonctionnement de `DocxValidator`

### 3.1 Entrée
`Validate(string path)` ou `Validate(Stream)` → ouvre le `.docx` comme
`ZipArchive` et parcourt :
- `word/document.xml` (corps du document)
- `word/footnotes.xml`
- `word/endnotes.xml`

`word/numbering.xml` est chargé séparément pour résoudre le préfixe visuel
des listes à puces/numérotées (`NumberingMap`), utilisé pour détecter les
tirets invalides en tête de liste.

### 3.2 Modèle de sortie
- `ValidationReport { List<ValidationError> Errors; bool IsValid }`
  `IsValid` est vrai si aucune erreur n'est présente (`IsWarning == false`) ;
  les avertissements n'invalident pas le document.
- `ValidationError { RuleName, Message, Context, IsWarning }`
  `Context` est un extrait textuel généré par `GetContext(...)` : 5 mots
  avant/après l'anomalie, avec l'anomalie encadrée par `>>>...<<<`. Les
  séquences d'espaces sont réécrites en description lisible
  (ex. `[2 espaces standards]`).

### 3.3 Règles implémentées (par paragraphe, sur le texte concaténé des `w:t`)

| RuleName | Type | Description |
|---|---|---|
| `ApostropheDroite` | erreur | Apostrophe droite `'` détectée (attendu : `’`) |
| `GuillemetDroit` | erreur | Guillemet droit `"` détecté (attendu : `«`/`»`) |
| `TiretDebutInvalide` | erreur | Paragraphe/puce commençant par `-` ou `–` (attendu : `—`) |
| `EspaceInsecableManquante` | erreur | `—` en début de ligne non suivi d'une espace insécable (` ` ou ` `) |
| `EspaceInsecablePonctuation` | erreur | Espace insécable manquante avant `!` ou `?` |
| `EspaceGuillemet` | erreur | Espace insécable manquante autour de `«`/`»` |
| `EspaceFinParagraphe` | erreur | Paragraphe se terminant par un espace/tabulation |
| `DoubleEspace` | erreur | 2+ espaces consécutives (standard, insécable, ou fine, mélangeables) |
| `StyleParagrapheInvalide` | erreur | Style de paragraphe hors de `Normal`/`Titre1`/`Ellipse` (uniquement dans `word/document.xml`) |
| `StyleTitre1Manquant` | erreur | Aucun paragraphe `Titre1` dans tout le document (vérif. globale, une seule fois) |
| `SautDePageDetecte` | erreur | Saut de page manuel (`w:br w:type="page"`) ou `w:pageBreakBefore` |
| `VirguleAvantEt` | avertissement | Virgule juste avant "et" (`,\s*et\b`, insensible à la casse) |
| `LectureFichier` / `LectureDocument` | erreur | Erreur d'E/S ou de parsing XML (try/catch englobant) |

Notes d'implémentation notables :
- Les vérifications de style et `StyleTitre1Manquant` **ne s'appliquent
  qu'à `word/document.xml`** (pas aux notes de bas de page/fin).
  `Validate_StyleChecksDoNotApplyToFootnotesOrEndnotes_ReturnsValid` documente ce comportement.
- `EspaceInsecablePonctuation` ne se déclenche que si le caractère précédent
  est alphanumérique ou une ponctuation fermante (`)`, `]`, `}`, `»`, `”`, `’`, `'`) —
  pas en tout début de paragraphe.
- `TiretDebutInvalide` gère deux cas : texte brut commençant par `-`/`–`,
  et puce de liste (`w:numPr`) dont le `lvlText` résolu via `numbering.xml`
  commence par `-`/`–`.

### 3.4 Chaînes de rendu (`Program.cs`)
`GetRuleTitle` mappe chaque `RuleName` vers un libellé humain en français,
affiché groupé par règle avec compteur d'occurrences. Le mode `--verbose`
affiche le `Context` de chaque occurrence individuelle.

## 4. CLI

```
Scrubx.Cli -i|--input <fichier.docx> [-v|--verbose] [-w|--warning]
Scrubx.Cli -h|--help
```

- `-i/--input` (requis) : chemin du fichier `.docx`.
- `-v/--verbose` : affiche le contexte détaillé de chaque occurrence.
- `-w/--warning` : affiche le détail des avertissements (sinon juste le
  titre groupé, sans compteur/contexte).
- `-h/--help` : affiche l'usage et quitte (code 0).

### Codes de sortie
| Code | Signification |
|---|---|
| 0 | Aide affichée, OU document valide (avec ou sans avertissements) |
| 1 | Erreur d'arguments CLI |
| 2 | Fichier introuvable |
| 3 | Extension ≠ `.docx` |
| 4 | Erreurs de validation détectées |

## 5. Tests

xUnit, `tests/Scrubx.Tests/`. Les `.docx` de test sont construits en mémoire
(`ZipArchive` + XML brut écrit à la main), pas de fichiers `.docx` fixtures
sur disque. Couverture actuelle : chaque règle a au moins un cas valide et
un cas invalide ; cas limites testés (espace insécable vs standard, fine
insécable, tabulation, listes numérotées, notes de bas de page vs corps
du document).

`tests/Scrubx.Tests/UnitTest1.cs` semble être un fichier de test généré par
défaut par le template — à vérifier/nettoyer si vide ou obsolète.

## 6. Points d'attention pour la suite du développement

1. **Portée des styles** : seul `word/document.xml` est vérifié pour les
   styles de paragraphe ; si les notes doivent un jour être contraintes,
   il faudra étendre `entryName == "word/document.xml"` dans `DocxValidator.cs:222`.
2. **Détection par paragraphe uniquement** : les vérifications opèrent sur
   le texte concaténé de chaque `w:p`, donc les problèmes à cheval sur deux
   paragraphes (ex. un tiret de fin de paragraphe suivi d'un autre) ne sont
   pas couverts.
3. **Pas de correction automatique** : l'outil ne fait que détecter et
   rapporter, il ne modifie jamais le `.docx`. Une fonctionnalité de
   correction automatique (`--fix`) serait une extension naturelle.
4. **Regex `VirguleAvantEt`** : ne gère que l'énumération simple avant "et"
   sur un mot ; ne détecte pas "ou" ni d'autres conjonctions similaires,
   à étendre si le besoin métier grandit.
5. **`numbering.xml` non trouvé** : si absent, `NumberingMap` reste vide
   silencieusement (pas d'erreur), donc les listes à puces ne sont alors
   plus vérifiées pour `TiretDebutInvalide` — comportement à confirmer
   comme voulu.
6. **Pas de configuration externe** : les règles (styles autorisés, etc.)
   sont codées en dur dans `DocxValidator.cs`. Si plusieurs profils de
   validation sont nécessaires (ex. gabarits différents), il faudra
   externaliser ces constantes (fichier de config ou options CLI).
7. **`Scrubx.Cli-linux-x64`** : un binaire compilé est présent à la racine
   du dépôt (fichier non suivi par git au moment de l'audit) — à vérifier
   s'il doit être committé, ignoré, ou publié en release séparément.
8. **Localisation** : tous les messages sont en français, en dur dans le
   code (`Program.cs` et `DocxValidator.cs`). Pas d'abstraction i18n pour
   l'instant.

## 7. Application desktop native (`Scrubx.Desktop`, Windows)

### 7.1 Principe

`Scrubx.Desktop` est une coquille WPF (`net10.0-windows`) qui héberge le
même backend que `Scrubx.Web` (routes API + `wwwroot` statique)
**en process**, via un contrôle WebView2 plein écran, sans exposer aucun
port réseau externe :

- Au chargement de la fenêtre (`MainWindow_Loaded`), l'appli construit et
  démarre une `WebApplication` via `WebAppFactory.Create(...)` (voir
  `src/Scrubx.Web/WebAppFactory.cs`), liée sur `http://127.0.0.1:0` (port
  dynamique attribué par l'OS — jamais de port fixe, jamais de conflit
  possible avec une instance `Scrubx.Web` déployée sur la même machine).
- Le WebView2 (`Microsoft.Web.WebView2.Wpf`, package NuGet
  `Microsoft.Web.WebView2`) navigue ensuite vers l'adresse réellement
  attribuée (`app.Urls.First()`).
- Aucun arrêt explicite du serveur à la fermeture de la fenêtre : le
  process entier disparaît, ce qui suffit pour un hôte purement local
  sans état persistant à nettoyer.

### 7.2 Extraction de `WebAppFactory`

`src/Scrubx.Web/Program.cs` a été réduit à deux lignes ; toute la
logique (routes, fichiers statiques, `ForwardedHeaders`) vit dans
`WebAppFactory.Create(string[] args, Action<WebApplicationBuilder>?
configure = null)`, réutilisable par n'importe quel hôte .NET (Web,
Desktop, ou un futur shell macOS). Point important :
`ContentRootPath` y est fixé explicitement à `AppContext.BaseDirectory`
(et non laissé au défaut basé sur le répertoire courant), car un hôte
embarqué comme `Scrubx.Desktop` ne contrôle pas forcément son
`CurrentDirectory` au démarrage.

### 7.3 Pièges rencontrés (à anticiper pour tout nouveau shell, y compris macOS)

1. **Propagation transitive des `Content` items via `ProjectReference`** :
   quand `Scrubx.Desktop` référence `Scrubx.Web`, MSBuild recopie
   automatiquement dans la sortie de `Scrubx.Desktop` tous les fichiers
   `Content` de `Scrubx.Web` marqués `CopyToOutputDirectory` — y compris
   `appsettings.json`, qui fixe `Kestrel:Endpoints:Http:Url` sur
   `127.0.0.1:5099`. Cette config, chargée par `IConfiguration`, prend le
   pas sur un simple `WebHost.UseUrls(...)` appelé en code. **Solution
   retenue** : dans le `configure` passé à `WebAppFactory.Create`, écraser
   directement la clé de configuration
   (`builder.Configuration["Kestrel:Endpoints:Http:Url"] = "http://127.0.0.1:0"`,
   voir `MainWindow.xaml.cs`) plutôt que de compter sur l'absence
   d'`appsettings.json`.
2. **`wwwroot` non propagé automatiquement** : contrairement à
   `appsettings.json`, les fichiers de `wwwroot` (assets statiques du SDK
   Web) ne se propagent pas vers un projet non-Web (WPF ici). Il faut les
   inclure explicitement en `Content` dans le csproj du shell — voir le
   bloc `<Content Include="..\Scrubx.Web\wwwroot\**">` dans
   `Scrubx.Desktop.csproj`.
3. **`UseUrls` seul est insuffisant** en présence d'un `appsettings.json`
   comportant une section `Kestrel:Endpoints` (cf. point 1) : le message
   de log `warn: ... Overriding address(es) '...'. Binding to endpoints
   defined via IConfiguration...` en est le symptôme.

### 7.4 Distribution

```bash
dotnet publish src/Scrubx.Desktop/Scrubx.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

Produit un dossier autonome (~160 Mo, WPF + .NET + ASP.NET Core
embarqués) contenant `Scrubx.Desktop.exe` + quelques DLL natives non
embarquables dans le single-file (`WebView2Loader.dll`,
`D3DCompiler_47_cor3.dll`, etc.) + `wwwroot/`. Nécessite le WebView2
Runtime, quasi toujours déjà présent avec Edge sur Windows 10/11 (sinon,
prévoir le redistribuable Evergreen).

## 8. Portage macOS (`Scrubx.Mac`) — non démarré, plan de reprise

Décision prise le 2026-08-13 : reporté à plus tard, à développer et
tester directement sur une machine macOS — impossible à faire depuis un
environnement Windows (WPF ne cible que Windows, et la compilation Mac
Catalyst nécessite un Mac avec Xcode).

### 8.1 Approche recommandée : .NET MAUI, cible Mac Catalyst

Même principe que `Scrubx.Desktop` (§7) : un nouveau shell
**`Scrubx.Mac`** (ou nom au choix), projet MAUI ciblant uniquement
`net10.0-maccatalyst`, avec :

- Une fenêtre unique contenant un `WebView` (contrôle MAUI natif,
  `Microsoft.Maui.Controls.WebView`) occupant tout l'espace — pas besoin
  de barre d'adresse ni de chrome navigateur, comme pour `Scrubx.Desktop`.
- Une référence de projet vers `Scrubx.Web` (donc transitivement
  `Scrubx.Core`), exactement comme `Scrubx.Desktop`.
- Un démarrage de serveur identique : appeler `WebAppFactory.Create(...)`
  avec le même override de `Kestrel:Endpoints:Http:Url` sur un port
  dynamique (`http://127.0.0.1:0`), déclenché depuis l'événement
  d'apparition de la page/fenêtre (`OnAppearing` ou équivalent MAUI), puis
  affecter l'URL réelle (`app.Urls.First()`) à `webView.Source`.
- **Vérifier si les deux pièges du §7.3 se reproduisent** : la
  propagation de `Content` via `ProjectReference` et l'inclusion de
  `wwwroot` fonctionnent différemment sous le SDK MAUI (mécanisme
  `MauiAsset` notamment) — à valider en pratique, pas à supposer
  identique à WPF.

### 8.2 Alternative : Avalonia (si Windows + macOS + Linux dans un seul projet shell)

Avalonia permettrait un seul projet shell cross-platform au lieu de
`Scrubx.Desktop` (WPF) + `Scrubx.Mac` (MAUI) séparés, via un contrôle
WebView tiers (`Avalonia.WebView` ou un pack CEF). Non retenu pour
l'instant : moins mature/officiel que MAUI pour la brique WebView, et
impliquerait de réécrire le shell Windows déjà fonctionnel. À
reconsidérer seulement si la duplication WPF/MAUI devient un problème
réel (maintenance de deux shells).

### 8.3 Prérequis pour reprendre ce travail

- Un Mac avec Xcode installé (requis par la toolchain Mac Catalyst, même
  en pilotant le build depuis .NET).
- SDK .NET 10 avec la charge de travail MAUI :
  `dotnet workload install maui`.
- Aucun compte développeur Apple nécessaire pour build/run en local (non
  signé), mais **requis pour toute distribution en dehors de la machine
  de build** (signature de code + notarisation Apple, Gatekeeper) — à
  anticiper si l'appli doit être partagée avec des beta-testeurs, comme
  `Scrubx.Web` l'a été.

### 8.4 Aucun portage nécessaire côté logique métier

`Scrubx.Core`, `Scrubx.Cli` et `Scrubx.Web` (dont `WebAppFactory`) sont
déjà 100 % multiplateformes (`net10.0`, aucune dépendance Windows) et
tournent sans modification sur macOS/Linux — seul le shell UI natif
manque pour macOS. Le travail de portage se limite donc au nouveau
projet shell décrit en §8.1, sans toucher à `src/Scrubx.Core` ni
`src/Scrubx.Web`.
