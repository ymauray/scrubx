# CLAUDE.md

Guide de démarrage pour un agent reprenant ce dépôt à froid.

## Ordre de lecture

1. **[`README.md`](README.md)** — ce que fait le projet, comment lancer/tester
   chaque application (`Cli`, `Web`, `Desktop`), comment déployer `Scrubx.Web`
   en production.
2. **[`SPECIFICATION.md`](SPECIFICATION.md)** — architecture détaillée,
   liste des règles de validation, fonctionnement de `DocxValidator`,
   architecture de `Scrubx.Desktop` (§7, avec les pièges MSBuild déjà
   rencontrés — à ne pas redécouvrir par essai-erreur), et le plan de
   reprise pour un futur portage macOS (§8, non démarré).

Ne pas dupliquer le contenu de ces deux fichiers ici : les tenir à jour
plutôt que d'en écrire un troisième qui diverge.

## Structure du dépôt

```
src/Scrubx.Core/    Logique de validation partagée (DocxValidator, RuleCatalog)
src/Scrubx.Cli/     Application en ligne de commande
src/Scrubx.Web/     API ASP.NET Core (WebAppFactory) + frontend statique (wwwroot/)
src/Scrubx.Desktop/ Application Windows native (WPF + WebView2), héberge Scrubx.Web en process
tests/Scrubx.Tests/ Tests xUnit (couvre Scrubx.Core via Scrubx.Cli)
deploy/             Exemples de config pour la mise en production (nginx, systemd)
```

`Scrubx.Web/wwwroot/` (HTML/CSS/JS vanilla) est la seule interface
utilisateur du projet : elle est servie telle quelle par `Scrubx.Web` et
copiée telle quelle dans `Scrubx.Desktop`. Toute modification d'UI doit se
faire à cet unique endroit.

## Conventions

- Tout commit créé par Claude se termine par un trailer
  `Co-Authored-By: Claude <noreply@anthropic.com>`.
- Commits : [Conventional Commits](https://www.conventionalcommits.org/)
  en minuscules, description en français (`feat:`, `fix:`, `refactor:`,
  `style:`, `docs:`, `content:`). Petits commits fonctionnels et
  indépendants plutôt qu'un gros commit final.
- Workflow : nouvelle branche `feature/...` par sujet, PR via `gh pr create`,
  jamais de commit direct sur `main`.
- Avant de pousser une modification d'UI, la tester dans un navigateur
  (via les outils Claude in Chrome) — ne pas se fier uniquement à la
  compilation.
- `dotnet test` doit passer (86 tests actuellement) avant tout commit
  touchant `Scrubx.Core`.

## État du projet

- `Scrubx.Cli` et `Scrubx.Web` : en production, utilisés par des
  beta-testeurs. La CLI dispose désormais des mêmes possibilités de
  désactivation de règles que le Web/Desktop, via des codes courts
  (`-i/--ignore`, `-f/--force`, `-c/--create-config` → `scrubx.json`,
  `-r/--show-rules`) — cf. `SPECIFICATION.md` §4.
- `Scrubx.Desktop` : fonctionnel, publié en exécutable autonome Windows
  (`win-x64`, self-contained, single-file).
- Portage macOS (`Scrubx.Mac`) : **non démarré**, à faire sur une machine
  macOS réelle (Xcode + workload MAUI requis, cf. `SPECIFICATION.md` §8).
- Automatisation CI/CD (GitHub Actions pour build+publish+déploiement) :
  envisagée mais pas commencée — actuellement tout se fait via
  `dotnet publish` manuel + copie sur le serveur (voir `README.md`).
