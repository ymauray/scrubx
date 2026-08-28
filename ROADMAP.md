# ROADMAP — Mode relecture (commentaires et révisions Word)

Suivi du travail en cours sur la fonctionnalité « relecture ». Ce fichier
existe pour permettre une reprise à froid : il consigne les **décisions
déjà prises** et les **questions encore ouvertes**, pas l'architecture
existante (voir [`SPECIFICATION.md`](SPECIFICATION.md)) ni le mode d'emploi
(voir [`README.md`](README.md)).

À tenir à jour au fil des étapes : cocher, et compléter le journal en fin
de fichier.

---

## 1. Objectif

Produire une **copie annotée** du `.docx` analysé, comme si un relecteur
humain était passé dessus : commentaires Word, et éventuellement marques de
révision, que l'utilisateur accepte ou refuse **une par une** dans Word.

**Ce n'est pas une correction automatique.** Décision explicite : l'outil
suggère, l'humain tranche. Le point 6 de `SPECIFICATION.md` §6 (« une
fonctionnalité de correction automatique `--fix` serait une extension
naturelle ») est donc écarté au profit de cette approche.

Le validateur actuel n'est pas remis en cause : la détection ne change pas,
on ajoute une passe d'écriture par-dessus.

---

## 2. Décisions déjà prises

| Décision | Raison |
|---|---|
| Les **commentaires** sont le livrable principal, les révisions un complément | Ils couvrent les 12 règles sans exception et ne touchent à aucun `w:r` existant : aucun risque d'abîmer le document |
| Les règles heuristiques (`GDROIT`, `STYLEINV`) sont **proposées**, pas retenues | Une suggestion fausse coûte un clic sur « Refuser » ; l'arbitrage est humain, donc l'imprécision est acceptable |
| `w:author = "Scrubx"` sur toutes les marques | Permet de filtrer le volet de révision par relecteur dans Word, et de distinguer l'outil d'un humain passé avant |
| Jamais de modification en place | Toujours réécrire une nouvelle archive vers `<nom>-relu.docx` ; l'original reste intact |
| Le portage macOS reste hors sujet | Indépendant, cf. `SPECIFICATION.md` §8 |

### Couverture par règle (cible)

| Code | Commentaire | Marque de révision |
|---|---|---|
| `APOS` | oui | `'` → `’` |
| `TIRET` (texte) | oui | `-`/`–` → `—` |
| `EFINPAR` | oui | suppression de l'espace finale |
| `EIMANQ`, `EIPONC`, `EGUIL` | oui | insertion / remplacement par U+00A0 |
| `DESPACE` | oui | collapse en une espace — **choisir laquelle garder** (insécable prioritaire ?) |
| `SAUTPAGE` | oui | suppression du `w:br`, ou `w:pPrChange` pour `pageBreakBefore` |
| `STYLEINV` | oui | `w:pPrChange` proposant `Normal` |
| `GDROIT` | oui | `"` → `«`/`»` par alternance ouvrant/fermant |
| `VIRGET` | oui | — (jugement éditorial) |
| `TITRE1` | oui (en tête de document) | — |
| `TIRET` (puce de liste) | oui | — (la correction est dans `numbering.xml`, hors périmètre des révisions) |

---

## 3. Question ouverte bloquante

**À trancher avant d'entamer l'étape 3 — ne pas coder les révisions avant.**

Une substitution s'écrit `w:del` (ancien texte) suivi de `w:ins` (nouveau).
Ce sont deux révisions distinctes dans le fichier. Si Word laisse accepter
l'une et refuser l'autre, on obtient un résultat cassé (`l'l’`).

En principe Word les regroupe quand elles sont adjacentes et partagent
auteur + date, mais **ce n'est pas vérifié**.

Protocole de test :

1. Fabriquer à la main un `.docx` minimal contenant un couple
   `w:del`/`w:ins` adjacent, même `w:author`, même `w:date`.
2. L'ouvrir dans un vrai Word (pas LibreOffice : le comportement du volet
   de révision diffère).
3. Vérifier que « Refuser » sur la marque restaure exactement le texte
   d'origine, et « Accepter » exactement le texte corrigé.

Si le regroupement ne tient pas → abandonner les marques de révision et
tout livrer en commentaires (l'étape 3 saute, les étapes 1-2-4 suffisent).

**Second point à vérifier dans Word, celui-là dès l'étape 1** : un
commentaire ancré dans `word/footnotes.xml` (anomalie détectée dans une note
de bas de page) s'affiche-t-il correctement, et Word ouvre-t-il le fichier
sans réclamer de réparation ? Si non, replier ces anomalies sur le paragraphe
du corps qui porte l'appel de note.

---

## 4. Étapes

### Étape 1 — Commentaires ancrés au paragraphe ✅

*Terminée le 2026-08-28, branche `feature/relecture-commentaires`.*

Première version utile, et de loin la moins chère : un commentaire ancré sur
le **paragraphe entier** ne demande aucune correspondance offset → run, donc
tout le découpage de runs (étape 2) est évité.

- [x] Ajouter à `ValidationError` de quoi identifier le paragraphe :
      `EntryName` + `ParagraphIndex` (index du `w:p` dans l'ordre du
      document). Renseignés par estampillage en fin de boucle de paragraphe
      dans `DocxValidator`, sans toucher aux 13 sites d'ajout d'erreur —
      toute règle future en hérite gratuitement.
- [x] `src/Scrubx.Core/DocxReviewer.cs` : recopie l'archive ZIP en
      réécrivant les parties concernées.
- [x] Générer `word/comments.xml` (message préfixé du code de règle, puis le
      `Context` produit par `GetContext`).
- [x] Déclarer la partie : `Override` dans `[Content_Types].xml` +
      `Relationship` dans `word/_rels/<partie>.xml.rels`.
- [x] Poser les ancres `w:commentRangeStart` / `w:commentRangeEnd` /
      `w:r > w:commentReference` dans chaque `w:p` concerné.
- [x] Tests (17 nouveaux dans `DocxReviewerTests.cs`, 3 dans
      `DocxValidatorTests.cs`) — 106 au total, tous verts.

Choix faits en cours de route, non prévus par la roadmap initiale :

- **Les commentaires déjà présents sont conservés.** Un manuscrit peut porter
  les commentaires d'un relecteur humain : `DocxReviewer` repart au-delà du
  `w:id` maximum existant et ajoute ses `w:comment` à la suite, au lieu de
  remplacer la partie.
- **Ancrage dans les notes.** Une anomalie détectée dans
  `word/footnotes.xml` / `endnotes.xml` est ancrée dans cette partie-là, qui
  reçoit sa propre relation vers `comments.xml`. **À valider dans Word**
  (cf. §3, deuxième point de vérification).
- **`Review` refuse d'écrire sur le fichier source** (`ArgumentException`).
- **Chargement `PreserveWhitespace` + sauvegarde `DisableFormatting`** pour
  que la recopie ne réindente rien : une indentation ajoutée dans un `w:p`
  modifierait le texte rendu par Word.

**Critère de fin — atteint côté structure, reste la vérification Word :**
sur `mon-roman-2026-05-21.docx` (5 anomalies), le fichier produit contient
5 commentaires ancrés sur les bons paragraphes, `word/comments.xml` est la
seule partie ajoutée, et le texte de `document.xml` est identique caractère
pour caractère à l'original. **Ouverture dans Word non encore faite.**

### Étape 2 — Ancrage précis au caractère

- [ ] `ParagraphTextMap` : pendant la concaténation des `w:t`, construire la
      table `(élément w:t, offset de début dans le paragraphe, longueur)`.
- [ ] Ajouter `Offset` / `Length` à `ValidationError` et les renseigner dans
      chaque règle (les offsets existent déjà localement, ils sont
      aujourd'hui uniquement passés à `GetContext`).
- [ ] Conversion offset paragraphe → (run, offset local), avec découpage
      d'un `w:r` en trois (avant / visé / après) en recopiant son `w:rPr`.
- [ ] Déplacer les ancres de commentaires sur la plage exacte.

**Critère de fin** : le commentaire surligne l'apostrophe fautive, pas le
paragraphe entier.

### Étape 3 — Marques de révision

**Conditionnée à la levée de la question du §3.**

- [ ] `w:del` (avec `w:delText`) + `w:ins`, `w:id` unique sur tout le
      document, `w:date` en ISO 8601.
- [ ] `w:pPrChange` pour `STYLEINV` et `SAUTPAGE`/`pageBreakBefore`
      (l'ancien `w:pPr` est enregistré à l'intérieur du nouveau).
- [ ] Arbitrer les chevauchements entre règles (voir §5).
- [ ] Décider si une règle produit commentaire **et** révision, ou seulement
      l'une des deux (risque de volet de révision illisible).

### Étape 4 — Surface utilisateur

- [ ] CLI : `--review <sortie.docx>` (nommage à confirmer ; ne pas réutiliser
      `--fix`, qui véhicule l'idée de correction automatique écartée au §1).
- [ ] Web : nouvel endpoint renvoyant le binaire — `/api/validate`
      (`WebAppFactory.cs:37`) renvoie du JSON et doit rester tel quel.
- [ ] `wwwroot/` : bouton « télécharger le document relu ». Rappel
      `CLAUDE.md` : l'UI se modifie à cet unique endroit, et se teste dans un
      navigateur avant de pousser.
- [ ] Desktop : hérite automatiquement du Web, vérifier quand même.
- [ ] Mettre à jour `README.md` et `SPECIFICATION.md` (dont le §6 point 3,
      qui annonce encore `--fix`).

---

## 5. Pièges techniques identifiés

À ne pas redécouvrir par essai-erreur :

- **`xml:space="preserve"`** obligatoire sur les `w:t` et `w:delText` dont le
  texte commence ou finit par une espace. Critique ici : la moitié des règles
  portent justement sur des espaces.
- **Appliquer les modifications de droite à gauche** dans un paragraphe, sinon
  la première invalide les offsets des suivantes.
- **`w:id` unique** sur l'ensemble du document (révisions et commentaires ont
  des espaces de nommage distincts, mais rester prudent).
- **Chevauchements entre règles** : `DESPACE` et `EFINPAR` visent souvent les
  mêmes caractères, `EIPONC` et `DESPACE` aussi. Décider : fusionner,
  prioriser, ou laisser les deux marques.
- **Documents contenant déjà des révisions** : le texte supprimé est dans
  `w:delText`, pas `w:t`, donc il est déjà exclu de la concaténation — rien à
  faire. En revanche, imbriquer un `w:del` dans un `w:ins` existant est légal
  mais délicat, à tester.
- **`commentsExtended.xml`** (fils de discussion, état « résolu ») est
  optionnel : Word ouvre le document sans. À n'ajouter que si le besoin
  apparaît.
- **LibreOffice n'est pas une référence** pour valider le rendu des
  révisions ou des commentaires : tester dans Word.

---

## 6. Contraintes de projet à respecter

- Branche `feature/...` par étape, PR via `gh pr create`, jamais de commit
  direct sur `main`.
- `dotnet test` doit passer (86 tests au démarrage de ce chantier) avant tout
  commit touchant `Scrubx.Core`.
- Commits en Conventional Commits, description en français, trailer
  `Co-Authored-By: Claude <noreply@anthropic.com>`.

---

## 7. Journal

| Date | Étape | Fait |
|---|---|---|
| 2026-08-28 | — | Cadrage : mode relecture (suggestions) retenu, correction automatique écartée. Rédaction de cette roadmap. Aucun code écrit. |
| 2026-08-28 | 1 | Étape 1 terminée : `DocxReviewer`, position des anomalies dans `ValidationError`, 20 tests (106 au total, verts). Fichier d'essai produit à partir de `mon-roman-2026-05-21.docx`. Reste à ouvrir dans Word. |
