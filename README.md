# Scrubx

Outil de vérification de mise en forme typographique de documents Word
(`.docx`) : apostrophes droites, tirets invalides, espaces insécables
manquantes, styles de paragraphe non autorisés, sauts de page, etc.

Disponible sous deux formes, partageant la même logique de validation
(`src/Scrubx.Core`) :

- **`Scrubx.Cli`** — outil en ligne de commande.
- **`Scrubx.Web`** — API + interface Web permettant de téléverser un
  document, de choisir les règles à appliquer, et d'obtenir le rapport.

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
dotnet run --project src/Scrubx.Cli -- -i mon-document.docx
dotnet run --project src/Scrubx.Cli -- -i mon-document.docx -v -w   # verbose + avertissements détaillés
dotnet run --project src/Scrubx.Cli -- --help
```

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
sans étape de build) et servi directement par ASP.NET Core.

### Structure du dépôt

```
src/Scrubx.Core/   Logique de validation partagée (DocxValidator, RuleCatalog)
src/Scrubx.Cli/     Application en ligne de commande
src/Scrubx.Web/     API ASP.NET Core + frontend statique (wwwroot/)
tests/Scrubx.Tests/ Tests xUnit
deploy/             Exemples de config pour la mise en production (nginx, systemd)
```

## Déploiement en production

L'application Web est prévue pour tourner **derrière un reverse proxy
nginx**, avec Kestrel qui n'écoute qu'en local (`127.0.0.1:5099`) — jamais
exposé directement sur le réseau.

### 1. Publier un binaire standalone

```bash
dotnet publish src/Scrubx.Web/Scrubx.Web.csproj -c Release -r linux-x64 --self-contained
```

Le résultat est dans
`src/Scrubx.Web/bin/Release/net10.0/linux-x64/publish/`. Copiez ce dossier
sur le serveur, par exemple dans `/opt/scrubx-web/`.

> Note : `publish.sh`/`publish.ps1` à la racine ne publient aujourd'hui que
> `Scrubx.Cli`. Pour `Scrubx.Web`, utilisez la commande `dotnet publish`
> ci-dessus (ou étendez ces scripts si vous voulez automatiser les deux).

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
