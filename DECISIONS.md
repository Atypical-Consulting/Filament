# DECISIONS.md — Journal d'arbitrages

Un paragraphe par arbitrage, avec sa raison et sa conséquence assumée.
On consigne ici les choix qui **engagent l'interprétation des chiffres** : quiconque conteste un
résultat de `BENCH.md` doit pouvoir trouver ici *pourquoi* il a été produit ainsi.

---

## 1. .NET 10 au lieu de .NET 11

**Décision.** La Phase 0 cible `net10.0`, alors que la spec dit .NET 11.

**Raison.** .NET 11 n'est pas GA et n'est pas installé. `dotnet --list-sdks` ne retourne que
6.0.418, 7.0.200, 8.0.203, 9.0.301 et **10.0.301** (avec `wasm-tools` 10.0.109). Il n'y avait donc
aucun moyen de mesurer une baseline .NET 11 aujourd'hui. Le choix réel n'était pas « .NET 10 contre
.NET 11 » mais « mesurer maintenant contre attendre ». Une baseline mesurée sur le SDK réellement
installé, entièrement documentée quant à sa version, vaut mieux qu'une absence de baseline : elle
est falsifiable et rejouable dès aujourd'hui, et le POC peut avancer.

**Conséquence assumée, à ne pas oublier.** Si .NET 11 modifie matériellement le payload Blazor
(taille du runtime, trimming, format webcil, AOT), **la baseline devra être re-mesurée intégralement**
et une nouvelle entrée ajoutée à `BENCH.md`. Les cibles C2 (37 784 o gzip) et C4 (23,45 ms sur
`create`) sont donc **provisoires et attachées à .NET 10.0.301** — jamais à citer sans leur SDK.

---

## 2. Mesurer Blazor avec ET sans AOT

**Décision.** Quatre configs plutôt que deux : Counter et Rows, chacune en AOT et non-AOT.

**Raison.** Une seule config aurait été un homme de paille, et l'échappatoire est ici parfaitement
symétrique parce que **C2 et C4 tirent en sens opposés** : l'AOT est 1,5 à 4× plus rapide mais
**2,57× plus lourd** (3,6 Mio gzip de `dotnet.native.wasm` contre 0,56 Mio interprété). Il aurait
donc été trivial — et malhonnête — de revendiquer un gain de 50× sur le poids contre la config AOT
(cible permissive : 97 176 o) tout en comparant la vitesse à la config non-AOT (cible molle :
35,40 ms). Ce serait choisir la moitié facile de chaque critère.

**Ce que cela fixe.** Filament doit battre le **plus petit** bundle de Blazor pour C2 (non-AOT :
1 889 184 o gzip → cible 37 784 o) **et** le **run le plus rapide** de Blazor pour C4 (AOT :
`create` 23,45 ms, `update` 3,20 ms, `swap` 3,55 ms, `clear` 2,70 ms, `increment` 14,40 ms). C2 et
C4 nomment délibérément des configs différentes. C'est le cadrage le plus hostile à Filament que les
données permettent, et c'est celui qu'on retient.

---

## 3. Base gzip retenue, base brotli mesurée et conservée

**Décision.** Les chiffres de tête sont en **gzip** (`--max-encoding gzip`), la base brotli est
mesurée et archivée comme référence secondaire.

**Raison.** La section 7 dit « gzip » et demande de vérifier `Content-Encoding: gzip` sur le fil ;
or le serveur du harness négociait **brotli** par défaut auprès de Chrome (`dotnet publish` émet des
siblings `.br` et Chrome envoie toujours `gzip, deflate, br, zstd`). Tourner tel quel aurait produit
des octets brotli **étiquetés gzip** — c'est-à-dire un mensonge sur le chiffre exact qui fixe C2. Du
vrai gzip a donc été servi et vérifié sur le fil (39/39 réponses framework, `index.html` 922 o,
`dotnet.native.wasm` 582 769 o, identiques aux siblings `.gz`). L'écart a ensuite été **mesuré** et
non estimé : −17,71 % (non-AOT) à −31,00 % (AOT), soit **plus** que les « ~22 % » supposés par
l'auteur du harness.

**Position assumée.** gzip est la base **conservatrice** : elle donne une baseline Blazor *plus
grosse*, donc **plus facile à battre par 50×** — le choix généreux envers Filament, pas envers nous.
Un hébergeur statique réel servirait le chiffre brotli. **Point non tranché et remonté comme tel** :
la cible C2 bouge de jusqu'à 31 % selon la base (Rows : 37 784 o gzip contre 31 092 o brotli). Quelle
que soit la base finalement retenue, **Filament doit être mesuré sous le même plafond** — sinon le
ratio de 50× n'est pas une comparaison, c'est un artefact d'encodage.

---

## 4. Modification du harness pour permettre le plafond d'encodage

**Décision.** Ajout d'un drapeau optionnel `--max-encoding <br|gzip|identity>` à `server.mjs` et
`bench.mjs`, dans la session même qui a produit les chiffres.

**Raison.** Le harness n'avait aucun moyen de servir du gzip ; sans cela, la décision n°3 était
inapplicable. Le changement est **additif et préserve le défaut** (`br`), donc tous les appelants
existants sont inchangés et les **249 assertions du selftest passent toujours** (re-vérifié
indépendamment : 249 passed, 0 failed). Le plafonnement ne peut que **faire monter** le poids
rapporté : il ne peut flatter aucun framework. Un client br-only sous plafond gzip reçoit
correctement de l'`identity` plutôt que de contourner silencieusement le plafond.

**Réserve assumée.** Modifier l'instrument dans la session qui produit la mesure est un risque
reconnu. Il est mitigé par : défaut préservé, selftest re-passé, `config.maxEncoding` et
`config.encodingNote` enregistrés dans chaque JSON. Il n'est **pas** éliminé : résultats et outil
sont tous deux non versionnés (voir décision n°8).

---

## 5. Contrat DOM partagé + LCG Park-Miller (graine 42, arithmétique en `double`)

**Décision.** Les deux apps produisent leur flux de labels via un LCG Park-Miller de graine 42, en
arithmétique **`double`**, contre un contrat DOM partagé piloté par un harness unique.

**Raison.** La section 7 exige un « harness identique ». Un contrat DOM partagé permet à un seul
harness, **agnostique du framework** (aucune branche sur l'identité du framework, injection in-page
via `addInitScript`, donc zéro octet réseau ajouté), de piloter Blazor et Filament sans code
spécifique. Le LCG garantit que les deux frameworks font **le même travail** : 3 000 opérations
multiply/modulo plus 1 000 concaténations de chaînes en trois parties par `#run`.

**Pourquoi `double` et pas `int`/`long`.** C'est le **seul type numérique dont l'arithmétique est
exactement reproductible dans les deux langages ici**. JavaScript n'a pas d'entier 64 bits natif dans
les opérateurs arithmétiques, et un `int` 32 bits déborderait : `16807 * 2^31 ≈ 3,6e13`, ce qui reste
**sous 2^53** — donc chaque produit intermédiaire est exactement représentable en `double`, en C#
comme en JS, sans arrondi. Le flux de labels est ainsi **identique octet pour octet** des deux côtés.

**Garde effective.** La fixture `expected-labels.json`
(sha256 `72733c72c80a1a6d5c984d0468ce421bd294babcbfe218a100f3290d38006168`) est vérifiée **en page**
contre les lignes 0–4 et 999. Sans cela, une app émettant un label constant ferait
drastiquement moins d'allocations de chaînes, passerait tous les prédicats et serait rapportée comme
plus rapide : les chiffres ne vaudraient rien. Cette garde doit tourner pour **chaque** framework,
baseline comprise.

---

## 6. Baseline écrite dans le sous-ensemble Filament v0

**Décision.** Les apps baseline Blazor n'utilisent que des constructions présentes dans le
sous-ensemble Filament v0.

**Raison.** La même logique de composant doit rester **compilable en Phase 2/3**. Si la baseline
Blazor utilisait des fonctionnalités hors du sous-ensemble v0, l'app Filament devrait les contourner,
et l'on ne comparerait plus deux implémentations de la même chose mais deux problèmes différents. La
comparaison reste ainsi **sémantiquement honnête** : mêmes données, même sortie DOM, même travail.
Concrètement : ni Router, ni HeadOutlet, ni HttpClient ; `@key="row.Id"` présent pour que la
réconciliation de liste soit celle qu'on prétend mesurer.

---

## 7. La primitive de chronométrage attend un prédicat DOM, jamais un rAF nu

**Décision.** L'horloge s'arrête **dans le callback du `MutationObserver`**, à l'instant où le
prédicat DOM est observé vrai (`msToMutation`). `msToPaint` est rapporté séparément et ne sert
jamais à comparer.

**Raison.** Le `@onclick` de Blazor est **asynchrone** : un `requestAnimationFrame` naïf posé après
le clic arrêterait l'horloge **avant le rendu**, et fabriquerait un gain pour le framework qui
dispatche le plus lentement — exactement l'inverse de ce qu'on veut mesurer. Pire, la panne serait
**silencieuse** et ressemblerait à un résultat légitime.

**Ce que cela évite précisément.** Un rAF en fin d'échantillon porte un décalage additif uniformément
distribué de 0 à 16,7 ms (phase vsync) plus un saut de tâche. Pour `create` (~35 ms) c'est du bruit ;
pour `swap`, `clear` et `increment` — où le travail DOM réel est sub-milliseconde en Filament — **le
nombre rapporté SERAIT l'intervalle de frame et l'IQR rapporté SERAIT le jitter vsync**. Trois des
cinq scénarios auraient été structurellement incapables de montrer la différence que le POC existe
pour démontrer. C'est visible dans les données conservées : chaque `msToPaint` est ~10–16 ms au-dessus
de son `msToMutation`. On garde `msToPaint` justement pour que ce quantum de ~16 ms soit **visiblement
celui du harness**, et non celui d'un framework.

**Corollaires appliqués.** Un timeout est un **échec**, jamais un nombre. Un prédicat déjà vrai avant
le clic **lève** au lieu de rapporter 0 (garde anti-vacuité). Les prédicats sont **totaux** : `update`
vérifie les 100 lignes ciblées **et** 10 lignes non ciblées restées intactes ; `swap` vérifie la
réciprocité **dans les deux sens**, de sorte qu'un duplicata (`rows[1] = rows[998]` sans l'affectation
inverse — un bug parfaitement ordinaire) ne peut pas passer pour une victoire de performance.

---

## 8. Statistiques : médiane et IQR, jamais la moyenne

**Décision.** Toute valeur de tête est une **médiane** accompagnée de son **IQR (p75−p25)**, `n = 10`.

**Raison.** Une moyenne sur 10 échantillons de quelques millisecondes est à la merci d'un seul
outlier (une pause GC, un réveil de démon), et elle n'a aucune robustesse à annoncer. La médiane
résiste ; l'IQR **publie la dispersion au lieu de la cacher**, ce qui rend la contestation possible.
C'est ce qui permet d'affirmer que 8 scénarios sur 10 ont un IQR ≤ 1,5 ms — et, surtout, de **signaler
soi-même** que `rows-nojit/create` a un IQR de 5,55 ms (16 % de sa médiane) et qu'il est le chiffre le
plus mou de la matrice, alors même que c'est la baseline de C4.

---

## 9. Publications AOT en échec : purger `obj/` et `bin/`, ne jamais contourner

**Décision.** Face à deux échecs de publication, la réponse a été de **purger les intermédiaires et
republier**, jamais d'assouplir les options ni de récupérer un chiffre d'une publication douteuse.

**Raison et diagnostic.** `blazor-rows-nojit` est mort sur `MSB3073` (`wasm-opt` : « expected more
elements in list / Fatal: error parsing wasm ») et `blazor-rows-aot` sur ~40× `MSB3030` (fichiers
`compressed/publish/<hash>-{0}-<hash>.gz` introuvables — noter le `{0}` **littéral non substitué**).
Ni l'un ni l'autre n'était une chaîne d'outils cassée ni un échec d'AOT : c'était un cache
`obj/` périmé, hérité d'un build antérieur (horodaté 11:16). `wasm-opt` réécrit `dotnet.native.wasm`
**sur place**, donc toute écriture partielle empoisonne les publications suivantes ; et les quatre
configs partagent deux répertoires projet, la bascule AOT/non-AOT étant précisément ce qui corrompt
le cache static-web-assets.

**Règle retenue.** `rm -rf obj bin` **entre chaque config**. C'est consigné dans le bloc « Commande de
rejeu » de `BENCH.md`, parce qu'un rejeu naïf retombera sur la même panne.

**Ce qui n'a pas été fait, et pourquoi.** Aucun `.csproj` n'a été modifié pour forcer l'AOT :
`RunAOTCompilation` n'est passé **qu'en ligne de commande**, afin que la **même source** produise les
deux configs. Un drapeau gravé dans le `.csproj` aurait rendu les deux configs non comparables et
aurait fait mentir l'étiquette `aot` du JSON.

---

## 10. Vérifier l'AOT depuis l'artefact, jamais depuis le drapeau

**Décision.** L'engagement réel de l'AOT a été prouvé par trois signaux indépendants du drapeau CLI.

**Raison.** `--aot` est **auto-déclaré** (`bench.mjs:1421`) et enregistré verbatim : le JSON
inscrirait volontiers `aot: true` pour un build non-AOT. Un fallback silencieux aurait produit une
baseline « AOT » qui n'en est pas. Preuves retenues : `dotnet.native.wasm` passe de 1 494 734 o à
11 362 554 o (×7,6) ; la chaîne `"Counter.Blazor"` est **présente** dans le wasm natif AOT et
**absente** du non-AOT ; les marqueurs `mono_aot` passent de 2 à 34 ; le log montre « AOT'ing 33
assemblies » puis 32/32 étapes `.bc → .o`. Signal complémentaire élégant : les deux configs non-AOT
partagent **une seule** empreinte de runtime (`kllr7zg72l`, indépendante de l'app) alors que les
configs AOT en ont une **par app** (`lz2nl4qo4f` contre `ogsd35n1u1`) — exactement ce qu'implique
l'AOT.

**Dette assumée.** Le harness ne fait toujours pas cette vérification lui-même. C'était correct ici
parce qu'un opérateur a tapé le bon drapeau. À verrouiller sur une empreinte du binaire.

---

## 11. Machine non quiescée, et divulgation plutôt que nettoyage

**Décision.** Ne pas tuer les démons de l'utilisateur (`OrbStack Helper`, `logioptionsplus_updater`,
~2,3 % de la capacité totale) et **divulguer** leur présence.

**Raison.** OrbStack est le runtime Docker de l'utilisateur : le tuer risquait d'interrompre un
travail en cours, et l'agent Logitech se relance de toute façon. Avec 16+ cœurs libres face à une
mesure mono-thread de quelques millisecondes, plus médiane et IQR sur 10 runs, le risque de
contamination est faible — et les IQR le confirment (8 scénarios sur 10 à ≤ 1,5 ms). Détruire
l'environnement de travail de l'utilisateur pour gagner ~2 % de capacité sur une mesure déjà robuste
était un mauvais échange.

**Réserve assumée.** `rows-nojit/create` (IQR 5,55 ms) est le seul chiffre méritant un re-run sur
machine réellement quiescée — et c'est précisément la baseline de C4. Consigné comme réserve ouverte
n°12 de `BENCH.md` plutôt que dissimulé.

---

## 12. Ce qui reste à trancher (dettes ouvertes, non arbitrées)

Consigné ici pour que ces points ne se règlent pas par défaut, en silence :

- **Base gzip ou brotli pour C2** — déplace la cible de jusqu'à 31 %. À trancher **avant** de geler
  la baseline. Filament doit subir le même plafond.
- **Valeur absolue de C1** — **manquante** : la spec n'est pas sur le disque (ni README, ni cahier
  des charges dans le dépôt). Impossible de reporter un plafond C1 sans l'inventer, donc il n'a pas
  été reporté.
- **Balisage exact des lignes** — le contrat n'exige que `cellsPerRow >= 2`. Blazor émet 4
  éléments/ligne (dont un `<a class="lbl">` décoratif), Filament pourrait n'en émettre que 3 et
  satisfaire le même contrat : ~25 % de nœuds DOM en moins, gratuitement. À épingler avant toute
  comparaison de `create`.
- **Shell `index.html` identique** — Counter utilise `<link rel="icon" href="data:,">`, Rows expédie
  un `favicon.png` de 1 148 o. Les apps Filament devront servir un shell octet pour octet identique.
- **`PublishTrimmed` explicite dans les deux `.csproj`** — Counter le déclare, Rows s'appuie sur le
  défaut SDK Release. Aucune différence aujourd'hui (vérifié : les deux trimmés), mais la baseline
  C2 dépend silencieusement d'un défaut.
- **Versionner le tout** — zéro commit git à ce jour ; `bench/results/` est dans `.gitignore`. Les
  commandes de publication et le harness doivent être commités et taggés avec les résultats.
- **Mesurer `create` hors coût de boot** — par un second `create` chronométré dans la même page,
  pour que C4 mesure la construction de lignes et non le premier appel.
