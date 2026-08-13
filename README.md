# Scrubx

Outil de vérification de mise en forme typographique de documents Word
(`.docx`) : apostrophes droites, tirets invalides, espaces insécables
manquantes, styles de paragraphe non autorisés, sauts de page, etc.

Disponible sous plusieurs formes, partageant la même logique de validation
(`src/Scrubx.Core`) :

- **`Scrubx.Cli`** — outil en ligne de commande.
- **`Scrubx.Web`** — API + interface Web permettant de téléverser un
  document, de choisir les règles à appliquer, et d'obtenir le rapport.
- **`Scrubx.Desktop`** — application Windows native (WPF + WebView2) : même
  interface que `Scrubx.Web`, mais servie en local depuis un exécutable
  unique, sans serveur ni réseau à configurer.

Portage macOS (`Scrubx.Mac`) envisagé mais non démarré — voir
[`SPECIFICATION.md`](SPECIFICATION.md) §8 pour le plan de reprise.

Voir [`SPECIFICATION.md`](SPECIFICATION.md) pour le détail des règles et de
l'architecture.

## Prérequis

- [.NET SDK 10](https://dotnet.microsoft.com/download) (`dotnet --version`
  doit afficher `10.x`).

## Développement

### Lancer les tests

```bash
dotnet test
```

(exécute `tests/Scrubx.Tests`, qui couvre `Scrubx.Core` via `Scrubx.Cli`).

### CLI

```bash
dotnet run --project src/Scrubx.Cli -- mon-document.docx
dotnet run --project src/Scrubx.Cli -- mon-document.docx -v -w   # verbose + avertissements détaillés
dotnet run --project src/Scrubx.Cli -- mon-document.docx -i VIRGET,STYLEINV   # ignorer des règles par leur code
dotnet run --project src/Scrubx.Cli -- mon-document.docx -f VIRGET   # forcer une règle désactivée dans scrubx.json
dotnet run --project src/Scrubx.Cli -- --show-rules   # lister les codes de règles disponibles
dotnet run --project src/Scrubx.Cli -- --create-config   # créer/compléter scrubx.json (règles activées par défaut)
dotnet run --project src/Scrubx.Cli -- --help
```

`scrubx.json`, s'il existe dans le répertoire courant, définit l'état
activé/désactivé de chaque règle (par son code) au lancement ; `-i/--ignore`
s'applique ensuite par-dessus. Détails : [`SPECIFICATION.md`](SPECIFICATION.md) §4.

### Application Web

```bash
dotnet run --project src/Scrubx.Web
```

Par défaut, Kestrel écoute sur `http://127.0.0.1:5099` (configuré dans
`src/Scrubx.Web/appsettings.json`, pensé pour être derrière un reverse
proxy — voir plus bas). En développement local, ouvrez simplement
<http://127.0.0.1:5099> dans un navigateur.

Pour écouter sur un autre port en local sans toucher `appsettings.json` :

```bash
ASPNETCORE_URLS=http://127.0.0.1:5000 dotnet run --project src/Scrubx.Web
```

Le frontend (`src/Scrubx.Web/wwwroot/`) est statique (HTML/CSS/JS vanilla,
sans étape de build) et servi directement par ASP.NET Core. Il est
partagé tel quel avec `Scrubx.Desktop`.

### Application desktop (Windows)

```bash
dotnet run --project src/Scrubx.Desktop
```

Ouvre directement une fenêtre avec l'interface (pas besoin de navigateur).
Le serveur ASP.NET Core tourne en mémoire sur un port local dynamique — rien
à configurer.

Pour produire un exécutable autonome à distribuer :

```bash
dotnet publish src/Scrubx.Desktop/Scrubx.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

Le résultat (`Scrubx.Desktop.exe` + quelques DLL natives + `wwwroot/`) se
trouve dans
`src/Scrubx.Desktop/bin/Release/net10.0-windows/win-x64/publish/`.
Nécessite le WebView2 Runtime (déjà présent avec Edge sur Windows 10/11
dans la quasi-totalité des cas). Détails et pièges rencontrés :
[`SPECIFICATION.md`](SPECIFICATION.md) §7.

### Structure du dépôt

```
src/Scrubx.Core/    Logique de validation partagée (DocxValidator, RuleCatalog)
src/Scrubx.Cli/     Application en ligne de commande
src/Scrubx.Web/     API ASP.NET Core (WebAppFactory) + frontend statique (wwwroot/)
src/Scrubx.Desktop/ Application Windows native (WPF + WebView2), héberge Scrubx.Web en process
tests/Scrubx.Tests/ Tests xUnit
deploy/             Exemples de config pour la mise en production (nginx, systemd)
```

## Intégration continue et releases

Deux workflows GitHub Actions (`.github/workflows/`) :

- **`tests.yml`** — exécute `dotnet test` à chaque PR vers `main` et à
  chaque push sur `main`. La branche `main` est protégée : ce check doit
  passer avant de pouvoir merger une PR (y compris pour les
  administrateurs du dépôt).
- **`release.yml`** — build les trois applications distribuables
  (`Scrubx.Cli` en `osx-arm64`/`win-x64`/`linux-x64`, `Scrubx.Web` en
  `linux-x64` self-contained, `Scrubx.Desktop` en `win-x64` sur
  `windows-latest`) et publie les archives comme assets d'une [GitHub
  Release](https://github.com/ymauray/scrubx/releases). Déclenché :
  - automatiquement en poussant un tag `vX.Y.Z` (`git tag -a v1.2.3 -m "v1.2.3" && git push origin v1.2.3`) ;
  - manuellement depuis l'onglet **Actions** → **Release** → **Run
    workflow**, en choisissant un tag existant dans le sélecteur de ref.

Le dépôt est public — nécessaire pour que la protection de branche
(payante sur un dépôt privé en plan gratuit) soit disponible.

## Déploiement en production

L'application Web est prévue pour tourner **derrière un reverse proxy
nginx**, avec Kestrel qui n'écoute qu'en local (`127.0.0.1:5099`) — jamais
exposé directement sur le réseau.

### 1. Récupérer un binaire standalone

Le plus simple : télécharger `Scrubx.Web-linux-x64.zip` depuis la
[dernière release GitHub](https://github.com/ymauray/scrubx/releases/latest)
(généré automatiquement, voir « Intégration continue et releases »
ci-dessous) et le décompresser.

Ou publier manuellement :

```bash
dotnet publish src/Scrubx.Web/Scrubx.Web.csproj -c Release -r linux-x64 --self-contained
```

Le résultat est dans
`src/Scrubx.Web/bin/Release/net10.0/linux-x64/publish/`. Copiez ce dossier
sur le serveur, par exemple dans `/opt/scrubx-web/`.

> Note : `publish.sh`/`publish.ps1` à la racine ne publient que
> `Scrubx.Cli`. Pour `Scrubx.Web`, utilisez la release GitHub ou la
> commande `dotnet publish` ci-dessus.

### 2. Exécuter le service (systemd)

Un exemple d'unité systemd est fourni dans
[`deploy/scrubx-web.service.example`](deploy/scrubx-web.service.example).

```bash
sudo useradd --system --no-create-home scrubx
sudo cp -r <dossier-publish>/* /opt/scrubx-web/
sudo chown -R scrubx:scrubx /opt/scrubx-web
sudo chmod +x /opt/scrubx-web/Scrubx.Web

sudo cp deploy/scrubx-web.service.example /etc/systemd/system/scrubx-web.service
sudo systemctl daemon-reload
sudo systemctl enable --now scrubx-web
sudo systemctl status scrubx-web
```

Le service écoute alors sur `127.0.0.1:5099` (défini dans
`appsettings.json`, publié avec le binaire).

### 3. Configurer nginx

Un exemple de configuration est fourni dans
[`deploy/nginx.conf.example`](deploy/nginx.conf.example) — reverse proxy
vers `127.0.0.1:5099`, avec :
- `client_max_body_size 25m` (l'API refuse déjà les fichiers > 20 Mo, la
  marge évite que nginx coupe la requête avant l'appli) ;
- les en-têtes `X-Forwarded-For`/`X-Forwarded-Proto`, nécessaires pour que
  `Scrubx.Web` (middleware `ForwardedHeaders` déjà configuré) restitue le
  vrai client et le bon schéma (http/https) dans ses logs et redirections.

```bash
sudo cp deploy/nginx.conf.example /etc/nginx/sites-available/scrubx.conf
# adapter server_name et, le cas échéant, les chemins de certificats TLS
sudo ln -s /etc/nginx/sites-available/scrubx.conf /etc/nginx/sites-enabled/
sudo nginx -t
sudo systemctl reload nginx
```

Pour HTTPS, le plus simple est [Certbot](https://certbot.eff.org/)
(`sudo certbot --nginx -d scrubx.example.com`), qui complète automatiquement
la configuration TLS.

### 4. Mettre à jour une version déployée

```bash
sudo systemctl stop scrubx-web
# remplacer le contenu de /opt/scrubx-web par la nouvelle publication
sudo systemctl start scrubx-web
```

Aucune donnée persistante n'est stockée côté serveur (les documents
téléversés sont traités en mémoire et jamais écrits sur disque), donc une
mise à jour ne nécessite aucune migration ni sauvegarde particulière.
