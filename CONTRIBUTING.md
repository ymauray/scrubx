# Contribuer à Scrubx

Merci de vous intéresser à Scrubx ! Ce document résume comment proposer une contribution.

## Avant de commencer

Pour une nouvelle fonctionnalité ou un changement important, ouvrez d'abord une issue ou une [discussion](https://github.com/ymauray/scrubx/discussions) pour en parler avec le mainteneur.

## Environnement de développement

Prérequis : [.NET SDK 10](https://dotnet.microsoft.com/download) (`dotnet --version` doit afficher `10.x`). `Scrubx.Desktop` (WPF + WebView2) ne se construit que sous Windows.

```sh
dotnet build
dotnet test
```

Le [`README.md`](README.md) explique comment lancer chaque application (CLI, Web, Desktop), et [`SPECIFICATION.md`](SPECIFICATION.md) détaille les règles de validation et l'architecture.

## Proposer une modification

1. Créez une branche dédiée depuis `main` (ex. `fix/...`, `feature/...`).
2. Faites des commits atomiques, au format [Conventional Commits](https://www.conventionalcommits.org/) en minuscules, avec une description en français (`feat:`, `fix:`, `refactor:`, `docs:`...).
3. Vérifiez que `dotnet test` passe en local : le check « Unit tests » est obligatoire avant tout merge.
4. Ouvrez une pull request vers `main` en remplissant le modèle fourni. Les PR sont mergées en squash.

## Conventions du projet

- `src/Scrubx.Core` contient toute la logique de validation, partagée par la CLI, le Web et le Desktop.
- `src/Scrubx.Web/wwwroot/` est la seule interface utilisateur du projet, reprise telle quelle par `Scrubx.Desktop` : toute modification d'UI se fait à cet endroit, et se teste dans un navigateur.
- `README.md` et `SPECIFICATION.md` décrivent ce qui existe ; le travail à faire se suit dans les issues.
