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
