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
  reçoit sa propre relation vers `comments.xml`. **Toujours à valider dans
  Word** (cf. §3, deuxième point de vérification) : le document d'essai n'en
  contenait pas.
- **`Review` refuse d'écrire sur le fichier source** (`ArgumentException`).
- **Chargement `PreserveWhitespace` + sauvegarde `DisableFormatting`** pour
  que la recopie ne réindente rien : une indentation ajoutée dans un `w:p`
  modifierait le texte rendu par Word.

**Critère de fin — atteint.** Sur `mon-roman-2026-05-21.docx` (5 anomalies),
le fichier produit contient 5 commentaires ancrés sur les bons paragraphes,
`word/comments.xml` est la seule partie ajoutée, et le texte de
`document.xml` est identique caractère pour caractère à l'original.
Ouverture dans Word validée par l'utilisateur le 2026-08-28.

Réserve : le document d'essai ne contenait pas de note de bas de page, donc
l'ancrage dans `word/footnotes.xml` reste non vérifié dans Word (cf. §3).

### Étape 2 — Ancrage précis au caractère ✅

*Terminée le 2026-08-28, branche `feature/relecture-ancrage-precis`.*

- [x] `src/Scrubx.Core/ParagraphTextMap.cs` : pendant la concaténation des
      `w:t`, construire la table `(élément w:t, offset de début, longueur)`.
      C'est désormais la **définition unique du « texte d'un paragraphe »** —
      `DocxValidator` passe par elle, donc les offsets qu'il relève sont
      interprétables par `DocxReviewer`.
- [x] `Offset` / `Length` dans `ValidationError`, renseignés par chaque règle
      (contrairement à `EntryName`/`ParagraphIndex`, ça ne peut pas
      s'estampiller après coup : chaque règle est seule à connaître sa plage).
- [x] Conversion offset paragraphe → run, avec découpage d'un `w:r` en deux et
      recopie du `w:rPr` sur chaque moitié.
- [x] Ancres posées sur la plage exacte.
- [x] Tests : 22 nouveaux (10 sur `ParagraphTextMap`, 12 sur l'ancrage
      précis) — 128 au total, tous verts.

Choix faits en cours de route :

- **Les marques restent au niveau du paragraphe.** À l'intérieur d'un `w:ins`
  ou d'un `w:hyperlink`, le schéma OOXML n'accepte pas
  `w:commentRangeStart` (`EG_RangeMarkupElements` n'appartient pas à
  `EG_ContentRunContent`). Les marques encadrent donc l'ancêtre du run qui est
  enfant direct du `w:p`. Conséquence : une anomalie **à l'intérieur d'un lien
  hypertexte ou d'une révision existante** est surlignée en entier plutôt qu'au
  caractère près — imprécis mais toujours valide, jamais corrompu.
- **Repli systématique sur le paragraphe entier** quand `Offset` est nul
  (règles de style, saut de page, puce de liste) ou quand la plage ne se
  résout pas. Une anomalie n'est jamais perdue faute d'ancrage.
- **Découpage de la fin avant le début** : un découpage de run ne change pas le
  texte du paragraphe, donc les offsets restent valides d'une coupe à l'autre,
  y compris entre deux commentaires du même paragraphe.
- **`xml:space="preserve"` systématique** sur les `w:t` issus d'un découpage.

**Critère de fin — atteint.** Sur `mon-roman-2026-05-21.docx`, les
5 commentaires sont ancrés exactement sur les caractères fautifs (`' '`,
`'-'`, `'"'`, `'"'`, `' '`), le texte du document est inchangé, et le
découpage n'ajoute que 6 runs sur 537. **Ouverture dans Word non encore
faite.**

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
| 2026-08-28 | 1 | Étape 1 validée par l'utilisateur après ouverture dans Word. Reste en suspens : le cas des notes de bas de page, absent du document d'essai. |
| 2026-08-28 | 2 | Étape 2 terminée : `ParagraphTextMap`, offsets par règle, découpage de runs, ancrage au caractère près. 128 tests verts. Reste à ouvrir dans Word. |
