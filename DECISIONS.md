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

---

# Arbitrages du gate Phase 0 — 2026-07-16

Les décisions n°13 à n°19 ont été prises **au gate Phase 0**, après l'entrée n°1 de `BENCH.md` et
avant de geler les cibles de Filament. Elles **supersèdent** les chiffres de tête de l'entrée n°1
(voir entrée n°2). Les décisions n°1 à n°12 restent le registre de ce qui a été arbitré au round 1.

---

## 13. `create` est mesuré à froid ET à chaud ; le CHAUD est le chiffre de tête de C4

**Décision.** `create` et `increment` sont dédoublés en `-cold` et `-warm`. **`create-warm` est le
chiffre de tête de C4** ; `create-cold` est conservé, mesuré et publié, mais **n'est jamais un titre**.

**Raison.** Le `create` froid est le **premier clic sur une page fraîchement chargée** : il porte le
boot du runtime Blazor. Filament boote en ~0 ms et **gagnerait `create` sur le seul boot, sans afficher
une seule ligne plus vite** — C4 passerait pour la mauvaise raison, et la panne serait **silencieuse**
puisqu'elle ressemblerait à une victoire légitime. La réserve n°1 de l'entrée n°1 le signalait déjà et
demandait explicitement de « mesurer `create` hors coût de boot **par un second `create` chronométré
dans la même page** ». C'est fait.

**Ce que la mesure directe donne, et ce qu'elle corrige.** La décision du gate estimait le coût réel de
construction de lignes à **~9,85 ms interprété / ~9,05 ms AOT** (soit un ratio AOT de 1,09× : l'AOT et
l'interprété quasiment à égalité sur le rendu). **La mesure directe dit autre chose :**

| | Estimation du gate | Mesure directe (`create-warm`, brotli) | Écart |
|---|---:|---:|---|
| Interprété (`rows-nojit`) | ~9,85 ms | **13,70 ms** | estimation **28 % trop basse** |
| AOT (`rows-aot`) | ~9,05 ms | **7,35 ms** | estimation **23 % trop haute** |
| Ratio AOT | 1,09× | **1,86×** | l'AOT est **bien plus** en avance |

**La rationale de la décision est validée ; son arithmétique est superseded.** C4 se juge désormais
contre **7,35 ms (AOT, cible dure)** et **13,70 ms (non-AOT)**, et **la cible AOT est nettement plus
dure que le gate ne le supposait**.

**Les deux défauts sont réels, et aucun ne domine proprement.** Le rapport de mesure amont voulait
conclure que la méthode de soustraction (`create-cold − increment-cold`) est structurellement cassée.
Une première rédaction de cette décision a rejeté cette explication en citant un seul appariement —
base gzip, AOT : 23,65 − 16,10 = 7,55 contre 7,45 mesuré, soit 0,10 ms d'écart. **Ce chiffre est le
meilleur des quatre appariements disponibles ; le citer seul flattait la méthode.** Les quatre, tous
à entrées du même round :

| Appariement | Dérivé | Mesuré | Écart |
|---|---|---|---|
| `rows-nojit`, br | 17,00 | 13,70 | **+24,1 %** |
| `rows-nojit`, gzip | 16,00 | 13,70 | +16,8 % |
| `rows-aot`, br | 8,05 | 7,35 | +9,5 % |
| `rows-aot`, gzip | 7,55 | 7,45 | +1,3 % |

Deux conclusions, pas une. **(1) L'instrument est instable** : `increment` non-AOT vaut 25,55 ms à
l'entrée n°1 et 17,10 ms à l'entrée n°2, **sans le moindre recouvrement d'échantillons** (décision
n°18) — c'est ce qui a produit l'estimation 9,85 ms. **(2) L'algèbre est biaisée** : même à entrées
saines, le dérivé **surestime le chaud dans les quatre cas, jamais l'inverse**. Le signe systématique
n'est pas du bruit, il a une cause identifiée — voir la conséquence ci-dessous : `create-warm` hérite
d'un tas GC réchauffé, donc dérivé et chaud ne mesurent pas la même grandeur.

La leçon n'est donc pas « se méfier de l'instrument, pas de l'algèbre » : c'est **ne pas dériver quand
on peut mesurer**. C'est exactement ce qu'a fait le round n°2.

**Conséquence assumée.** `create-warm` bénéficie d'un tas GC réchauffé qu'un premier visiteur n'a
jamais : c'est une **borne basse** du coût de construction de lignes. C'est le prix à payer pour mesurer
du rendu et non du boot. Le harness impose la **même séquence à tout framework**, donc la comparaison
reste équitable — **mais Filament devra être poussé par le même chemin, sinon 13,70/7,35 ms ne veulent
rien dire.** `create-cold` reste publié parce qu'il est **réel et visible par l'utilisateur** : c'est le
coût d'un vrai premier chargement, il ne doit simplement jamais être confondu avec du rendu.

---

## 14. Base brotli en tête pour C2, gzip mesuré et conservé

**Décision.** Le ratio 50× de **C2 se juge en brotli**. La base gzip reste mesurée, publiée et
conservée dans la table. Ceci **tranche** le « point non tranché » de la décision n°3.

**Raison.** Un hébergeur statique réel **sert du brotli** : `dotnet publish` émet les siblings `.br`, et
Chrome annonce toujours `gzip, deflate, br, zstd`. Le chiffre brotli est donc **celui qu'un utilisateur
réel télécharge**. C'est aussi la **cible la plus dure** : Blazor est 17,7 % à 31,0 % plus petit en
brotli (mesuré : −17,72 % non-AOT, −31,02 % AOT), donc la cible C2 se resserre de 37 761 o à
**31 068 o**. Choisir gzip aurait été choisir la base **généreuse envers Filament** — exactement le
genre de facilité que ce POC existe pour refuser. La décision n°3 avait retenu gzip comme base
conservatrice **et remonté le point comme non tranché** ; il est ici tranché **dans le sens hostile à
Filament**.

**gzip reste dans la table pour une raison précise, pas par symétrie** : **C1 est exprimé en gzip**
(voir décision n°16). Supprimer la base gzip rendrait C1 invérifiable.

**Règle liante, sans exception.** **Filament doit être mesuré sous le MÊME `--max-encoding` que la
baseline qu'il affronte.** Comparer un Filament brotli à un Blazor gzip — ou l'inverse — **n'est pas un
ratio de 50×, c'est un artefact d'encodage**. Le harness enregistre `config.maxEncoding` et
`weight.serverEncodings` dans chaque JSON précisément pour que cette triche soit détectable après coup :
ce round vérifie **39/39 réponses en `br`** dans les runs brotli et **39/39 en `gzip`** dans les runs
gzip. Les chiffres gzip sont de **vrais octets gzip**, pas des octets brotli ré-étiquetés.

**Correction d'une citation erronée du rapport amont.** Le rapport parle de « the agreed BROTLI headline
basis » et attribue la règle de base identique à la « decision #2 ». **Les deux sont faux au moment où
il l'écrit** : la décision n°3 disait le contraire (« Les chiffres de tête sont en gzip ») et marquait
explicitement le point comme **non tranché** ; la règle de base identique est la n°3, pas la n°2 (la n°2
porte sur AOT vs non-AOT) ; et l'estimation 9,85/9,05 qu'il attribue à la « decision #1 » vient de
`README.md` sourcé à « BENCH.md reserve #1 », la décision n°1 portant sur .NET 10 vs .NET 11. **Le
présent paragraphe est ce qui rend la base brotli effectivement décidée** — elle ne l'était pas avant.

---

## 15. Poids : C2 se juge contre non-AOT, vitesse : C4 se juge contre AOT — et jamais l'inverse

**Décision.** Confirmation et re-chiffrage de la décision n°2 sur les mesures finales. **C2 se juge
contre `blazor-rows-nojit`** (le plus petit bundle : 1 553 388 o br) et **C4 contre `blazor-rows-aot`**
(le run le plus rapide : `create-warm` 7,35 ms).

**Raison.** L'échappatoire est **parfaitement symétrique**, donc doublement tentante : l'AOT est
**2,16× plus lourd** en brotli mais **1,86× plus rapide** au rendu. Revendiquer 50× de poids contre la
config AOT (cible molle : **67 016 o**, 2,16× plus permissive) **tout en** comparant la vitesse à la
config non-AOT (cible molle : **13,70 ms**, 1,86× plus permissive) reviendrait à choisir **la moitié
facile de chaque critère** — et chacune des deux tricheries est invisible prise isolément. C'est
précisément pourquoi les deux moitiés sont nommées **ensemble, dans le même paragraphe**, et pourquoi
`BENCH.md` porte l'avertissement anti-cherry-picking à côté des cibles et non en note de bas de page.

**Ce que cela fixe.** C2 et C4 nomment **délibérément des configs différentes**. C'est le cadrage le
plus hostile à Filament que les données permettent, et c'est celui qu'on retient.

---

## 16. C1 (< 10 ko gzip) est le verrou de poids qui contraint réellement, pas C2

**Décision.** **C1 = `< 10 ko gzip`.** C'est **C1**, et non C2, qui est le **verrou contraignant** sur
le poids : **C2 passe automatiquement si C1 passe.**

**Raison — arithmétique, sur les mesures de l'entrée n°2 :**

| Comparaison | Calcul | Résultat |
|---|---|---:|
| C1 contre C2, **même base gzip** | 37 761 / 10 000 | **C1 est 3,78× plus strict** |
| C1 contre C2, base de tête brotli | 31 068 / 10 000 | **C1 est 3,11× plus strict** |

**L'argument ne dépend d'aucun transfert de ratio de compression.** Pour un même contenu, brotli est en
pratique **toujours ≤** gzip. Donc un artefact Filament à ≤ 10 000 o gzip pèse **≤ 10 000 o en
brotli**, très en dessous des **31 068 o** qu'exige C2 en brotli — avec **≥ 3,1× de marge**. Que l'on
lise « 10 ko » comme 10 000 o ou 10 240 o ne change rien (3,03× à 3,11× de marge en brotli).

**Conséquence à ne pas perdre de vue.** « Filament bat Blazor par 50× sur le poids » sera **le résultat
le moins exigeant** que Filament produira. C'est un titre spectaculaire et **presque gratuit** une fois
C1 tenu. **Le vrai test de poids est C1**, et c'est lui qu'il faut surveiller.

**Provenance, consignée honnêtement.** L'entrée n°1 consignait C1 comme **CHIFFRE MANQUANT** parce que
la spec n'est pas sur le disque. **Elle ne l'est toujours pas** : recherche refaite ce jour — aucun
fichier de spec, aucun cahier des charges, et `grep « 10 ko »` dans le dépôt retourne **zéro
occurrence**. **C1 est donc consigné sur la seule autorité du propriétaire au gate**, pas depuis un
document vérifiable. Si la spec réapparaît et dit autre chose, **c'est la spec qui gagne**, et une
entrée n°3 devra le consigner. Cette valeur n'a **pas** été inventée pour combler le trou : elle est
tracée à sa source, qui est une personne et non un fichier.

---

## 17. Reproductibilité : **la décision n°3 du gate n'est PAS honorée** — état réel

**Décision.** Consigner l'état **réel** de la reproductibilité plutôt que de la déclarer close. La
décision n°3 du gate exige que les trous soient comblés. **Ils ne le sont pas.** Ce paragraphe existe
pour que ce constat ne se règle pas par défaut, en silence.

**Ce qui EST comblé** (réel, vérifié) :
- **`bench/publish-baseline.sh` existe et est commité** : les commandes de publication ne vivent plus
  dans l'historique shell d'un opérateur. Réserve n°3 de l'entrée n°1 **levée**.
- **`RunAOTCompilation` reste en ligne de commande, jamais dans un `.csproj`** (`grep -rn` sur
  `baseline/` ne trouve que des commentaires) : **un seul arbre source produit les deux configs**, comme
  l'exige la décision n°9.
- **Les deux `.csproj` sont identiques à l'octet** (`diff` vide), `PublishTrimmed` **et**
  `InvariantGlobalization` explicites des deux côtés. Réserve n°8 **levée**.
- **Les deux apps servent un shell identique à `<title>` près** ; `favicon.png` supprimé ; **39
  requêtes** partout. Réserve n°9 **levée**.
- **L'AOT est vérifié depuis l'artefact et non depuis le drapeau** (`aotObserved` dans les 8 JSON).
  Dette de la décision n°10 **payée**.
- **Un commit existe** (`6402831`) — contre zéro au round 1.

**Ce qui N'EST PAS comblé, et qui bloque la décision n°3** :
1. **Les 8 JSON de tête sont gitignorés.** `git ls-files bench/results/final-warm/` ⇒ **0**.
   `git check-ignore -v` ⇒ `.gitignore:18:bench/results/*`. La négation `!bench/results/*.json` **ne
   rattrape pas un sous-répertoire** — git ne descend jamais dans un répertoire exclu. Le commentaire du
   `.gitignore` proclame que les résultats sont « DELIBERATELY NOT ignored » : **les fichiers réellement
   rapportés sont ignorés**. Le trou signalé au round 1 est **comblé pour les 5 anciens JSON et rouvert
   pour les 8 nouveaux**. → `!bench/results/**/*.json`, puis commiter les 8 fichiers.
2. **Le harness qui a produit ces chiffres n'est pas commité** (`bench.mjs`, `selftest.mjs`,
   `server.mjs`, `expected-labels.json` tous modifiés). **Un `git clone` de HEAD donne un code
   matériellement différent** : `grep -c secondRun` sur le `bench.mjs` de HEAD ⇒ **0**, donc la garde
   anti-fabrication sur laquelle s'appuie l'entrée n°2 **n'existe pas à HEAD**. « Code identique pour
   chaque config » est vrai du répertoire de travail — **et le répertoire de travail n'est pas ce qu'on
   peut checkouter**.
3. **`README.md` publie l'estimation superseded comme un fait** (« ~9.85 ms interpreted / ~9.05 ms
   AOT »), annonce **249** assertions de selftest (réel : **440**) et documente l'ancien chemin de
   sortie. **Un inconnu qui suit le README rejoue l'ANCIEN protocole et cite un chiffre que l'équipe
   sait déjà faux.**
4. **Le sha256 de fixture cité en décision n°5 est périmé** : le journal dit `72733c72…`, le disque et
   les 8 résultats disent `877b1461…` (élargissement légitime au 2ᵉ `#run`, journal non suivi).

**Position assumée.** « Reproductible » est une affirmation sur **ce qu'un inconnu peut checkouter et
rejouer**. Les preuves, l'instrument qui les a produites et le registre qui les consigne sont
**absents du commit**. Le rapport de mesure amont concède que la décision n°3 n'est « NOT closed » mais
**localise le trou uniquement dans l'étape de publication** : il est **aussi** dans les résultats et
dans le harness. **Les mesures sont presque certainement correctes ; elles ne sont pas encore
reproductibles.** C'est la raison n°1 pour laquelle le gate n'est pas déclaré passé.

---

## 18. Les entrées n°1 et n°2 ne sont PAS directement comparables (irréproductibilité de 33 %)

**Décision.** Consigner comme **découverte de premier plan** — et non comme note de bas de page —
qu'un scénario de l'entrée n°1 **ne se reproduit pas**, et **refuser** l'affirmation de comparabilité du
rapport amont.

**Le fait.** `blazor-counter-nojit` / `increment`, **même machine, même Chrome, même SDK, même base
gzip, même `n = 10`** :

| | Médiane | Échantillons |
|---|---:|---|
| Entrée n°1 (harness **1.1.0**) | **25,55 ms** | `[27, 25.4, 24.5, 25.7, 27, 21.4, 26.1, 24.2, 26, 24.7]` |
| Entrée n°2 (harness **1.2.0**) | **17,10 ms** | `[17.3, 17.8, 13.1, 16.1, 17.5, 16.9, 16.9, 17.9, 18, 16.7]` |

**Aucun recouvrement d'échantillons** (21,4–27,0 contre 13,1–18,0). **8,45 ms, soit 33 %.** Ce n'est pas
du bruit.

**Cause la plus probable, et pourquoi elle n'est pas certifiée.** `harnessVersion` est passé de **1.1.0
à 1.2.0**, et `bench.mjs` documente lui-même que le code précédent « discarded the settle result
entirely at both call sites » : les itérations 1.1.0 chronométraient un clic **sur un réseau non
stabilisé**, en course avec du téléchargement et du décodage en vol, ce qui **gonflait l'échantillon**.
Le sens de l'écart est cohérent : **c'est l'entrée n°1 qui était gonflée et l'entrée n°2 qui est
correcte**. Mais **l'hypothèse n'a pas été testée** — elle est donc consignée comme telle et non comme
un fait.

**Ce qui est refusé.** Le rapport amont affirme « .NET SDK 10.0.301 (== the pinned Phase 0 baseline SDK,
**so results remain comparable to BENCH.md entry #1**) ». **Cette inférence est rejetée** : la
comparabilité y est déduite de la seule identité du SDK, alors que (a) le chemin chronométré du harness
a **matériellement changé**, (b) les artefacts ont été **republiés** (empreintes `ogsd35n1u1` →
`nm0j57lo9u`, `lz2nl4qo4f` → `xc7yj6pp2h` : **tailles identiques, octets différents** — le rapport parle
à tort de « byte-exact », alors qu'il compare des **tailles**), et (c) un scénario a bougé de **33 % sans
recouvrement**. **Règle retenue : ne jamais comparer un chiffre de l'entrée n°1 à un chiffre de l'entrée
n°2. L'entrée n°2 supersède ; l'entrée n°1 reste l'archive de ce qui a été mesuré ce matin-là.**

---

## 19. Machine non quiescée : divulguer et laisser l'IQR arbitrer, plutôt que nettoyer ou taire

**Décision.** Ne rien tuer, **divulguer**, et **laisser l'IQR arbitrer empiriquement** au lieu de
plaider depuis le load average. Reconduction de la décision n°11 dans des conditions **pires**.

**Raison.** La machine était **plus contaminée** que lors de l'entrée n°1 : deux processus `koine-mcp`
emballés (~100 % et ~98 % CPU, ~27 Go de RSS cumulés, 4 h+), plus OrbStack et l'agent Logitech — soit
~2,5 cœurs sur 18 (**~14 %** de la capacité, contre ~2,3 % au round 1), avec 5,5 Go de swap sur 7,2 Go.
Tout appartient à l'utilisateur : le tuer risquait d'interrompre son travail.

**Pourquoi les chiffres tiennent — empiriquement, pas par plaidoirie.** La contamination **n'a
démontrablement pas mordu** : `rows-nojit/create-cold` — le chiffre que la décision n°8 et la réserve
n°12 désignaient comme **le plus mou de la matrice et la baseline même de C4** — passe d'un IQR de
**5,55 ms (16 % de la médiane)** à **0,675 ms (2,0 %)**, un **resserrement d'environ 8×** malgré ~6× plus
de bruit CPU nominal. Mécanisme : 18 cœurs, et les fautifs sont **2 threads épinglés tournant en
boucle** ; l'ordonnanceur macOS a gardé le thread principal mono-thread de Chrome headless sur des cœurs
performance libres. **Le load average compte les threads exécutables, pas la contention réellement
subie par Chrome.** Les 3 échantillons de poids sont identiques à l'octet dans les 8 runs, et 5 des 10
paires chaudes concordent **à la médiane exacte** entre deux sessions navigateur indépendantes.

**Correction d'un chiffre flatteur du rapport amont.** Le rapport présente ce resserrement comme « ~8x »
en comparant l'IQR **gzip** de l'entrée n°1 à l'IQR **brotli** de l'entrée n°2. À base égale
(gzip contre gzip), c'est **5,55 → 1,60 = 3,5×**. **Le facteur 8 exige l'appariement inter-bases.** Le
resserrement est réel et la conclusion tient dans les deux cas ; le chiffre annoncé, lui, était choisi.

**Réserve assumée, non maquillée.** Une machine réellement inactive reste préférable, et **27 Go de RSS
emballé poussant le swap est un risque latent** : un défaut de page dans une fenêtre chronométrée est
exactement ce qui gonfle l'IQR. **Les IQR disent que ce n'est pas arrivé ici.** Un re-run sur machine
quiescée coûterait **~12 minutes** et lèverait la dernière réserve si le gate veut zéro doute.
**Indépendamment du benchmark** : les `koine-mcp` emballés sont un vrai problème pour l'utilisateur et
méritent une investigation/un redémarrage.

---

## 20. Ce qui reste à trancher après le gate Phase 0 (dettes ouvertes, non arbitrées)

La décision n°12 listait les dettes du round 1. Mise à jour :

**Réglées** : base gzip/brotli (n°14) · valeur de C1 (n°16, sur autorité du propriétaire, spec toujours
absente) · shell `index.html` identique · `PublishTrimmed` explicite des deux côtés · `create` hors boot
(n°13) · vérification de l'AOT depuis l'artefact.

**Toujours ouvertes** :
- **Versionner les preuves et le harness** — **le trou le plus grave** (n°17). Les 8 JSON de tête sont
  gitignorés, le harness n'est pas commité, le README publie un chiffre superseded. **Bloquant pour la
  décision n°3 du gate.**
- **Balisage exact des lignes** — le contrat n'exige toujours que `cellsPerRow >= 2`. Blazor émet **4
  éléments/ligne** (dont un `<a class="lbl">` décoratif), Filament pourrait n'en émettre que **3** et
  satisfaire le même contrat : ~25 % de nœuds DOM en moins, **gratuitement**. Ce handicap est **cuit
  dans les 13,70/7,35 ms**. **À épingler avant toute comparaison de `create-warm`.**
- **`publish-baseline.sh` : chemin AOT flaky (~50 %) et non sûr en parallèle.** Les configs appariées
  partagent un arbre source et le script commence par `rm -rf obj bin`. **La décision n°9 attribue la
  panne à un « cache périmé » : c'est une mauvaise attribution** — les assets compressés sont bien
  produits, sous un hash différent de celui qu'attend l'étape de copie ; c'est une **course dans un
  seul build propre**, et une publication non-AOT du même arbre réussit proprement. Le message d'erreur
  du script **enverra le prochain opérateur sur une fausse piste**. → Boucle de réessai bornée,
  correction du texte, sérialisation des configs partageant un arbre. **Le chemin AOT du script n'a
  jamais été démontré de bout en bout** — c'est pourtant lui qui produit le chiffre de tête de C4.
- **Défauts du harness relevés par l'audit** (3 majeurs, 3 mineurs — réserve E de l'entrée n°2). Aucun
  n'invalide la baseline mesurée, **mais** : la garde d'équité ne gate pas le prédicat du clic
  chronométré de `create-warm` ; le « settle beat » n'est appliqué qu'aux nouveaux scénarios chauds, si
  bien que **les quatre scénarios chauds sont mesurés sous des régimes de stabilisation différents** ;
  `classifyAotEvidence` peut poser `verified: true` depuis un artefact jamais servi. À corriger **avant**
  que ces chiffres arbitrent Filament.
- **Baseline « Blazor par défaut », pas « Blazor minimal »** (`System.Text.Json` ~8 % du poids, jamais
  utilisé par un compteur).
- **`n = 10`, une machine, un Chrome, un OS.** Suffisant pour un POC ; **insuffisant** pour une
  revendication de performance publiable.
- **Re-run sur machine quiescée** (~12 min) pour lever la dernière réserve de la n°19.

---

# Phase 1 — arbitrages (entrée `BENCH.md` n°3, 2026-07-16)

## 21. Mesurer du JS écrit à la main, et le dire — plutôt que de ne rien mesurer

**Décision.** La Phase 1 mesure `samples/Counter/counter.js` et `samples/Rows/rows.js`, **écrits à la
main**, au-dessus de `src/filament-runtime` écrit à la main. **Aucun générateur C# n'existe** :
`src/Filament.Generator/`, `src/Filament.Core/` et `src/Filament.Analyzer/` sont des **répertoires
vides**.

**Raison.** Le choix réel n'était pas « mesurer le générateur contre mesurer la main » mais « mesurer
la main maintenant contre ne rien mesurer ». Écrire le générateur d'abord aurait exigé de connaître la
forme du JS cible ; or **la cible est précisément ce que C1/C3/C4 devaient trancher**. Écrire à la main
la sortie que le compilateur devra émettre — l'« *answer key* », comme le dit l'en-tête de
`counter.js` — établit une **borne basse** de ce que l'architecture permet, et donne au générateur une
**cible de snapshot-test** au lieu d'une intuition. Si cette borne basse avait échoué à C1 ou C4, la
thèse serait morte **sans écrire une ligne de C#** — c'est l'ordre le moins coûteux.

**Conséquence assumée, et c'est la plus lourde du POC.** **Ce qui est prouvé est « du JS taillé à la
main bat Blazor »** — que Solid et Svelte ont établi il y a des années et que personne ne contestait.
**Ce que le POC a besoin de savoir reste non testé** : qu'un générateur C# puisse émettre ceci depuis du
Razor **en tenant sous 10 ko et à ces temps**. Un compilateur émet du code **général** ; un humain écrit
du code **spécial**. Tout l'écart entre les deux est **du poids et du temps non mesurés**. **Chaque
chiffre de l'entrée n°3 est donc une borne basse optimiste, jamais « la performance de Filament ».**
→ **Le gate est CONDITIONNEL sur ce point** (voir décision n°27) : le premier livrable de la Phase 2
doit être le générateur émettant le compteur, **re-mesuré sous C1/C3/C4**. Si sa sortie manque 10 ko ou
les temps AOT, **c'est la Phase 2 qui échoue, pas la Phase 1** — mais on l'apprendra alors, pas
maintenant.

## 22. Forme de l'API du runtime : `signal()` / `computed()` / `effect()`, et pourquoi elle mappe sur `Signal<T>.Value`

**Décision.** Le runtime expose trois primitives — `signal(v)`, `computed(fn)`, `effect(fn)` — où
lecture et écriture passent par une **propriété d'accès** (`s.value` / `s.value = x`), et non par des
fonctions d'appel (`s()` / `s.set(x)`, la forme Solid).

**Raison.** C'est **le seul choix qui rend le mapping C# mécanique**. `Filament.Core` déclarera
`Signal<T> { public T Value { get; set; } }` et `Computed<T> { public T Value { get; } }` : une
propriété C# **est** un couple `get`/`set`, donc `s.Value` se traduit en `s.value` **caractère pour
caractère**, sans que le générateur ait à décider si un identifiant est une lecture ou un appel. La
forme Solid `s()` aurait obligé le générateur à **réécrire les sites d'accès** — c'est-à-dire à faire
de l'analyse de flot pour distinguer `s` (la référence) de `s()` (la lecture) — pour **zéro gain
d'exécution**. Le suivi de dépendance se fait dans le **getter**, ce que C# et JS implémentent
identiquement.

**Conséquence assumée.** L'API est **moins idiomatique en JS** que `s()` et surprendra un lecteur venu
de Solid. C'est accepté : **le consommateur de cette API est un générateur, pas un humain.** La lisibilité
JS n'est pas un objectif de ce projet ; la **traduisibilité 1:1 depuis C#** en est un.
**Réserve de revendication** : « computed est paresseux » doit s'énoncer « **les computed NON OBSERVÉS**
sont paresseux ». Dès qu'un effet en dépend, `checkDirty` **doit** l'évaluer pour savoir s'il faut
re-exécuter — vérifié en test. C'est correct et nécessaire, mais la formule non qualifiée est trompeuse
et ne doit pas être publiée telle quelle.

## 23. Modèle d'ordonnancement : batch synchrone, drainage plat, glitch-freedom par marquage en deux temps

**Décision.** Les écritures marquent les dépendants `DIRTY|PENDING` et **empilent** ; le flush est
**synchrone**, dans le `finally` du handler d'événement ; les flushs imbriqués sont **collapsés en une
seule boucle de drainage** par une garde `flushing` ; la glitch-freedom vient de `checkDirty`, qui
**vérifie** la fraîcheur avant de ré-exécuter au lieu de propager aveuglément.

**Raison.** Trois exigences en tension, et une seule combinaison les satisfait toutes. (1) **C3 exige
1 écriture DOM par incrément** : sans batch, un diamant de dépendances écrirait deux fois. (2) **C4 est
mesuré par un `MutationObserver` dont l'horloge s'arrête dans le callback** — donc **tout travail différé
au-delà du microtask serait chronométré à zéro et serait une TRICHE**. Un flush asynchrone (microtask
ou rAF) aurait produit de meilleurs chiffres **en mentant** ; le flush synchrone garantit que le temps
mesuré **est** le temps du travail. (3) **La récursion est un plafond** : un drainage récursif aurait
débordé la pile sur des cascades profondes. **Vérifié : 200 000 runs ré-entrants, aucun stack overflow**
— la garde `flushing` collapse réellement.

**Conséquence assumée.** Une écriture depuis un handler paie **tout** son coût de rendu **dans** le
handler : pas de découpage en tâches, pas de time-slicing, **pas de rendu concurrent**. Pour une app
qui rendrait 100 000 lignes d'un coup, cela **bloque le thread principal** là où React concurrent
céderait la main. C'est assumé : Filament vise le **coût total le plus bas**, pas la **meilleure
répartition** d'un coût élevé. **Aucune détection de cycle** n'est faite : `s.value = s.value + 1` dans
un effet **tourne à l'infini** (prouvé). Les runtimes de type Solid se comportent similairement — mais
c'est une **dette explicite**, pas un oubli.

## 24. Stratégie d'allocation : liens de dépendance intrusifs et réutilisés, jamais réalloués

**Décision.** Le graphe est fait de `Link` doublement chaînés, **intrusifs** (pas de `Set`/`Map` par
nœud). À la ré-exécution, `I()` **réutilise le lien existant** quand la dépendance n'a pas changé
(`i.dep === n`) au lieu d'en allouer un neuf ; la file d'effets est intrusive et **laisse chaque `nq` à
`null` après flush** (aucune chaîne retenue).

**Raison.** **C3 exige « 0 allocation d'arbre de rendu »**, et c'est un critère qu'on ne peut pas
atteindre en optimisant après coup : il faut que **le régime établi n'alloue rien du tout**. Un `Set`
par signal aurait alloué à chaque abonnement ; une file en `Array` aurait alloué à chaque flush ; des
liens réalloués à chaque run auraient produit une allocation **proportionnelle au nombre de dépendances
× le nombre de runs** — exactement la charge GC que Blazor paie et que Filament doit ne pas payer.
**Vérifié : 100 000 incréments ⇒ `stats.links === 0`, `runs === 100 000`** ; corroboré par un test
d'événements GC externe aux compteurs du runtime (2 M d'incréments, **0 GC**).

**Conséquence assumée.** Le code du runtime est **nettement moins lisible** que l'équivalent à `Set` :
la topologie du graphe est encodée dans des champs de liens mutés en place, et une erreur d'ordre de
chaînage est une corruption silencieuse plutôt qu'une exception. C'est le prix de C3.
**Dette réelle et non payée** : `Computed` **n'a aucun chemin de disposition** — il ne s'enregistre
jamais auprès de `owner` et `disposeOwned` ne parcourt que des `Effect`. Un `Computed` créé dans un scope
de ligne **fuit sans borne** (mesuré : `[100, 200, … 1000]` abonnés sur 10 cycles ; 9 001 liens fuités
dégradant 2 000 écritures de **5,8 ms à 60,7 ms**). **Le benchmark ne l'atteint pas** (`rows.js` n'utilise
que `effect()` — voir décision n°25), **mais la Phase 2 l'atteindra au premier `@foreach` contenant une
expression dérivée.** → **À corriger avant la Phase 2**, symétriquement à `Effect` (~ownership élargi à
un `Disposable` commun).

## 25. `rows.js` n'utilise que `effect()` dans `createRow`, jamais `computed()`

**Décision.** Le template de ligne du benchmark n'emploie **délibérément** que `effect()`.

**Raison.** Une ligne n'a **aucune valeur dérivée à mémoïser** : elle écrit son id et son label
directement dans le DOM. Introduire un `computed()` aurait ajouté un nœud de graphe **sans consommateur**
— du poids et une indirection pour rien, et un `computed()` non observé ne s'exécute même pas.

**Conséquence assumée, à énoncer parce qu'elle est commode.** Ce choix **contourne accidentellement** la
fuite de `Computed` de la décision n°24. **Il n'a pas été fait pour cela** — il précède la découverte du
bug — mais le résultat net est que **le benchmark ne peut pas révéler cette fuite**, et que la Phase 2,
elle, la révélera. Consigné ici pour que personne ne lise « les chiffres sont bons » comme « le runtime
ne fuit pas ».

## 26. `stats` hors du bundle de production, par DCE prouvé **depuis l'artefact**

**Décision.** L'instrumentation (`__filament.stats`) vit derrière une constante de build
`filament:stats` éliminée par DCE ; **quatre** labels sont construits — 2 production, 2 `-stats`.

**Raison.** C1 et C3 **se contredisent** : C3 exige un compteur d'écritures DOM interne, C1 interdit
d'en payer les octets. Deux bundles résolvent la contradiction, **à condition de prouver que le bundle
pesé est bien celui sans instrumentation** — sinon C1 mesure un bundle et C3 un autre, et les deux
critères parlent d'artefacts différents. **La preuve est tirée de l'artefact, jamais de l'intention du
build** : `grep -c` sur les bundles de production ⇒ `filament:stats` **0**, `__filament` **0**,
`domWrites` **0**, `sourceMappingURL` **0** ; sur les bundles `-stats` ⇒ 1/2/2/1. Cela prouve **deux**
choses d'un coup : le DCE a tiré, **et** le run C3 mesure une instrumentation **réelle** et non un no-op.

**Conséquence assumée.** Deux artefacts par app à maintenir, et **le risque permanent qu'ils divergent**.
La garde est le `grep` ci-dessus, à rejouer à chaque build — **pas** la lecture du script de build.

## 27. Parité de compression avec `dotnet publish`, imposée et non supposée

**Décision.** `build-filament.sh` émet les siblings `.gz`/`.br` à **`gzip -9`** et **`brotli -q 11` avec
`BROTLI_PARAM_SIZE_HINT`**, exactement les réglages de `server.mjs` et de `dotnet publish`.

**Raison.** C1 est un **rapport de 658×** : il serait resté vrai avec n'importe quel réglage. Mais le
défaut aurait été **structurel et invisible** — Blazor arrive avec des siblings précompressés au maximum
par le SDK, tandis que Filament, servi sans siblings, aurait été compressé **à la volée** à un niveau
plus faible. Filament aurait alors été **pénalisé** et le chiffre aurait été faux **dans le sens
défavorable**. Un chiffre faux qui vous dessert reste un chiffre faux : **il salit l'instrument**.
Vérifié sur le fil : `serverEncodings.gzip = {responses: 3, bytes: 2030}`, 1153+404+473 = 2030
exactement ; CDP 2864 = 2030 + 834 o d'en-têtes. Les siblings **se décompressent à l'octet identique à
la source** (sha256, 6/6).

**Conséquence assumée.** Le script de build doit rester synchronisé avec `server.mjs` **à la main** ; rien
ne le vérifie automatiquement.

## 28. Parité du shell `index.html` **et de la feuille de style** — l'invariant a été brisé, puis réparé

**Décision.** Filament sert le **même shell** et la **même feuille de style, à l'octet**, que le label
Blazor correspondant. La source CSS est **pilotée par le label** (`css_for()`), et une **assertion
post-build compare aux octets PUBLIÉS par Blazor**.

**Raison, et l'échec qui l'a imposée.** `build-filament.sh` **codait en dur** le CSS de **Counter** et le
copiait pour **les quatre** labels — **y compris `filament-rows`**. Les deux apps Blazor expédient des
feuilles **différentes** (Counter 795 o, md5 `66d7c50f` ; Rows 917 o, md5 `1b67ed3e`). `filament-rows`
n'expédiait donc **jamais** le style spécifique aux tables : `border-collapse: collapse`, le padding des
`td`, la largeur `.col-md-1`. **Le biais allait dans le sens de Filament, sur l'app même qui décide C4** :
Blazor mettait 1 000 lignes en page sous `border-collapse: collapse` (matériellement plus coûteux dans
Blink que `separate`) pendant que Filament les mettait en page **sans aucun style**. Le script
**proclamait** pourtant l'invariant contraire (« *ships the SAME file, copied byte-for-byte … so neither
side is styling-subsidised* »).

**Portée honnête de la faute.** Le **chiffre de tête de C4 était insulé** : `waitForCondition` capture
`performance.now()` **dans le callback du `MutationObserver`**, un microtask qui court **avant**
style/layout/paint — `msToMutation` **exclut** donc le coût de layout. C1 était insensible (11 o gzip
d'écart contre 5 757 o de marge). **Aucun verdict n'a basculé.** Mais c'était une **rupture de parité
réelle, non divulguée, dans l'artefact expédié**, qui biaisait `msToPaint` (métrique secondaire rapportée
par échantillon) et la comparaison de poids — et qui **falsifiait silencieusement un invariant que le
script revendiquait explicitement**.

**État : CORRIGÉ ET VÉRIFIÉ DEPUIS L'ARTEFACT** (`css_for()` ligne 192, employé ligne 542, assertion
post-build) : `filament-rows/css/app.css` = md5 `1b67ed3e`, 917 o = `baseline/Rows.Blazor` =
`blazor-rows-nojit` ✔ ; `filament-counter/css/app.css` = md5 `66d7c50f`, 795 o ✔.

**Conséquence assumée, NON corrigée.** Le rapport `--shell-parity` **n'exerce toujours que Counter** et
imprime néanmoins une revendication **générale** de parité CSS « byte-for-byte » pour tous les labels.
**C'est le mécanisme par lequel le défaut a survécu** : le seul outil qu'un relecteur lancerait pour
valider la parité était **structurellement incapable** de voir une divergence propre à `rows`, **et
affirmait que la parité tenait**. Cela transforme un défaut d'artefact en une revendication qu'un
relecteur croirait vérifiée — l'exact contraire du principe affiché par le script (« *parity is only
worth having if it cannot drift* »). → Boucler `--shell-parity` sur **chaque** label de production.

## 29. Un instrument C3 agnostique du framework — pourquoi un compteur auto-rapporté était insuffisant

**Décision.** Les écritures DOM sont comptées par un `MutationObserver` **sur `body`** (la racine la plus
large), **le même code pour les deux frameworks**. `__filament.stats.domWrites` n'est **jamais** la
mesure : il n'est qu'un **contre-contrôle**.

**Raison.** Un compteur auto-rapporté mesure **ce que le runtime croit faire**, pas ce que le DOM
subit — c'est **la définition d'un instrument qui ne peut pas se réfuter**. Trois défauts rédhibitoires :
(1) il ne verrait pas une écriture émise **hors** du chemin instrumenté ; (2) il est **structurellement
inapplicable à Blazor**, qui n'a pas ce compteur — **il n'y aurait donc AUCUNE comparaison**, seulement
une auto-déclaration de Filament ; (3) il rendrait C3 **invérifiable par un tiers**. L'observer, lui,
est **le même instrument des deux côtés** et voit **le DOM réel**. Résultat : observé `[1,1,1,1,1]`,
auto-rapporté `[1,1,1,1,1]` — **concordance sur chaque incrément**. La valeur de l'auto-rapport n'est pas
d'établir le fait, c'est que **son désaccord avec l'observer serait un bug**.

**Conséquence assumée, et elle coupe contre nous.** L'instrument honnête montre que **Blazor fait AUSSI
exactement 1 écriture DOM par incrément**. La moitié « écritures DOM » de C3 est donc une **barre de
correction que Filament franchit**, **pas un différenciateur** face à Blazor. Un compteur auto-rapporté
n'aurait jamais produit ce constat — il aurait rapporté « Filament : 1 » et laissé le lecteur supposer
que Blazor faisait pire. **C'est précisément ce que cet arbitrage achète.**

## 30. La sonde d'allocation est **complète pour Filament** et **aveugle à Blazor** — et le rapport est interdit

**Décision.** `bytesPerIncrement` est rapporté pour les deux frameworks, mais **aucun rapport entre les
deux n'est un résultat C3**, et la mise en garde le dit dans l'artefact.

**Raison.** La sonde échantillonne l'**allocation JavaScript**. Le runtime de Filament **est** du
JavaScript : la sonde est donc **complète** pour lui, et une fausse revendication de « 0 allocation » se
verrait (à N=1000, intervalle 1024 o, même 32 o/incrément émergeraient à ~32 ko). L'arbre de rendu de
Blazor vit dans la **mémoire linéaire WASM** — **un seul `ArrayBuffer` pour V8** : la sonde en est
**structurellement aveugle** et ne voit que la **glu d'interop**. « Filament ~0 o vs Blazor 2 769 o »
compare donc le **total** de Filament au **sous-ensemble** de Blazor. Le publier serait la
**mesure malhonnête la plus flatteuse disponible dans ce dépôt** — d'où l'interdiction explicite.

**Conséquences assumées, non corrigées, toutes contre nous.** (1) La mise en garde contient une **phrase
fausse** : le coût de pilotage **n'est pas identique entre frameworks** — `driveIncrements` sonde par
`setTimeout(tick, 0)` et évalue `el.textContent.trim()` à chaque tick (2 chaînes/tick), et **le nombre de
ticks suit la latence de dispatch**. Filament flush **synchroniquement** (0 tick de plus) ; Blazor
dispatche **asynchroniquement** et paie plusieurs ticks : **un framework plus lent est facturé plus
d'allocation pour être plus lent.** La phrase fausse est **exactement celle qui autoriserait à citer le
rapport**, logée dans le paragraphe chargé de l'interdire. (2) Les fixtures calibrant le plancher sont
**toutes synchrones** : le plancher < 512 o n'est établi **que** pour un dispatch synchrone et **ne se
transfère pas à Blazor**. (3) **L'artefact ne porte aucun zéro calibré** : `bytesPerIncrement` sort **nu,
sans verdict**, alors que chaque affirmation d'écriture DOM en reçoit un — **l'artefact ne peut donc pas
trancher le critère qu'il énonce lui-même**. (4) La sonde est **plus bruitée que sa rédaction ne
l'admet** (`lowBytes` : 155 656 / 74 268 / 85 608, **dispersion 2,1×** ⇒ **±102 o/incrément** de bruit de
méthode sur ~335 o) ; **la conclusion tient pour des raisons architecturales**, pas parce que le profil
la prouve.

## 31. Ne pas incrémenter `HARNESS_VERSION` était une faute — et le rapport amont a affirmé un fait faux

**Décision (rectificative).** Le rapport amont affirmait « *bench.mjs/server.mjs were NOT modified* » et
offrait `harnessVersion 1.2.0` comme **preuve** que Filament et sa baseline venaient du **même chemin de
code chronométré**. **Les deux affirmations sont fausses** et sont rectifiées ici.

**Les faits, vérifiés ce jour.** `git diff --stat HEAD -- bench/harness/bench.mjs` ⇒ **707 insertions,
6 suppressions**, non commitées ; `selftest.mjs` modifié (+423). `HARNESS_VERSION` **n'a pas bougé** à
travers ce diff : **la chaîne ne peut pas distinguer les deux builds**. Chronologie : baselines Blazor
**13:11–13:21 UTC** · `bench.mjs` écrit **14:50:47 UTC** · Filament **16:16–16:26 UTC**. **Blazor a été
mesuré avec le harness d'avant, Filament avec celui d'après** — le hasard 1.1.0-vs-1.2.0 (33 %
d'irreproductibilité) que le rapport se félicitait d'avoir évité, **rouvert et masqué par la chaîne
offerte en preuve**.

**Pourquoi C4 survit quand même — vérifié ici, et non par le rapport.** Le chemin chronométré est
**intact** : `waitForCondition` (433 → 439) et `measure` (495 → 501) ont **glissé de 6 lignes sans
changement de contenu**, extraits et comparés à l'octet — **sha256 identiques** ; `runScenario` est
**identique à l'octet**. Les **6 suppressions** sont **toutes** dans le contrat de balisage (le
`cellsPerRow >= 2` permissif remplacé par un contrat strict) ; le reste est **purement additif**.

**Conséquence assumée.** **Le rapport a affirmé l'ABSENCE d'un diff au lieu de l'INNOCUITÉ d'un diff** —
une affirmation qu'il n'avait pas vérifiée, et qui se trouvait fausse. La conclusion tient ; **la méthode
qui l'établissait, non**. Une **asymétrie** subsiste et doit être dite : `inPageHarness` a grossi
d'environ **+261 lignes** pour la sonde C3, **présent à chaque run Filament, absent de chaque run
Blazor**. **Le sens favorise Blazor** (Filament paie le parse en plus), donc cela ne flatte pas Filament
— mais la revendication « identique sur tous les axes » **le nie**. → **Correctif : incrémenter à 1.3.0,
commiter le harness, re-mesurer la baseline Blazor sous le même build.**

## 32. Rapporter le plancher de l'appareil comme une limite de QUANTIFICATION, jamais comme une parité

**Décision.** `increment-warm` est marqué **`floorLimited`**, son rapport n'est **pas** cité, et
« > 10× plus rapide que l'AOT » est énoncé comme **borne basse dérivée du quantum**, jamais comme une
accélération mesurée. `update` et `swap` portent une réserve de quantification explicite.

**Raison.** La consigne du gate est nette : **une égalité au plancher de l'appareil passe C4 mais ne
prouve pas la parité**. La rigueur exige d'appliquer la règle **dans les deux sens** — y compris quand
elle **dessert** Filament. Filament lit **0,00 ms** de médiane, IQR 0,00, contre un quantum
`performance.now` de **0,1 ms** : **son coût réel est IRRÉSOLVABLE** ; tout ce qu'on peut dire est
« < ~0,1 ms ». **Mais la prémisse de l'avertissement est réfutée par les données** : l'appareil **ne
bute pas vers ~1 ms** — il résout à 0,0–0,1 ms et lit 0,30/0,40 ms sur `update`/`swap`. Les échantillons
de Blazor `[0.9, 1, 1, 0.9, 1, 1, 1, 0.9, 1.1, 1.1]` **ne s'entassent pas au minimum** : **1,00 ms est
une lecture réelle**, et Filament est **véritablement en dessous**. **Le plancher limite la capacité à
QUANTIFIER l'avantage, pas à l'ÉTABLIR.**

**Conséquence assumée.** Le résultat le plus spectaculaire du POC (« incrément > 10× plus rapide que
l'AOT ») est **le moins bien mesuré**, et est publié comme une **borne**, pas comme un chiffre. `update`
(3 quanta) et `swap` (4 quanta) portent **~33 % / ~25 %** d'incertitude sur leur **rapport** : les
verdicts sont sûrs, **les rapports ne doivent pas être cités à 3 chiffres significatifs**. Résoudre
réellement l'incrément exigerait un autre instrument (boucle de N incréments chronométrée en bloc), non
construit ici.

## 33. Publier les défauts de l'appareil et les bugs du runtime **dans la même entrée que les succès**

**Décision.** L'entrée n°3 liste 12 réserves ouvertes (A–L) et 5 bloqueurs de correction sémantique
**dans le corps de l'entrée**, pas en annexe — dont **deux** que le rapport amont **niait** (harness
modifié ; « identique sur tous les axes »).

**Raison.** `BENCH.md` est **append-only** parce que l'historique **est** la preuve. Une entrée qui ne
publierait que ses succès **n'est pas une mesure, c'est une plaidoirie** — et le seul lecteur qui compte
est celui qui essaie de **réfuter** le chiffre. Le précédent est établi : l'entrée n°2 a **attrapé un
chiffre flatteur** d'un rapport amont (un resserrement « ~8× » qui valait 3,5× à base égale). **La même
discipline s'applique ici, à notre propre rapport.**

**Conséquence assumée.** L'entrée n°3 se lit comme un réquisitoire contre son propre projet. C'est
**voulu** : un `PASS` accompagné de 12 réserves est **falsifiable** ; un `PASS` nu ne l'est pas. Les 3 bugs
sémantiques du runtime (**7 échecs voulus** dans `npx vitest run`) sont publiés **alors qu'aucun n'est
atteignable depuis les apps mesurées** et qu'ils **n'invalident aucun chiffre** — parce que le lecteur
doit pouvoir établir **lui-même** cette portée au lieu de nous croire.

## 34. Décision du gate Phase 1 : **CONDITIONNEL** — les critères passent, le gate n'est pas franchi

**Décision.** **C1, C3 et C4 PASSENT** sur l'artefact mesuré (C5 aussi). **Le gate de la Phase 1 n'est
pas déclaré franchi pour autant** : il est **CONDITIONNEL**, sur deux points nommés (décision n°21 et
décision n°24 / bloqueurs sémantiques). **Aucun seuil n'a été déplacé pour faire passer quoi que ce soit.**

**Raison.** Le gate demande « C1, C3, C4 passent ». Ils passent — **mais un gate est un test sur un
livrable, et le livrable n'a pas été mesuré** : le générateur n'existe pas (répertoires vides), donc les
chiffres portent sur l'**answer key** écrite à la main (décision n°21). Déclarer « Phase 1 franchie »
reviendrait à laisser croire que la proposition porteuse — *« un générateur C# émet ceci sous 10 ko à ces
temps »* — a été testée. **Elle ne l'a pas été.** S'y ajoute la règle explicite du protocole : **un bug
sémantique réel est un bloqueur de Phase 1 quelle que soit la qualité des chiffres** — il y en a **trois**
(valeur silencieusement fausse sur throw ; mise à jour silencieusement manquée ; corruption du DOM sur
clés dupliquées), plus la fuite de `Computed`, **reproduits ce jour**.

**Ce que les données autorisent réellement à dire.** **C4 passe ⇒ la thèse n'est PAS falsifiée ⇒ le dépôt
n'est PAS archivé.** **C1 passe aussi ⇒ la variante RADICALE n'est pas éliminée** — mais elle n'est pas
**établie** non plus, car sa condition de viabilité (la sortie du **générateur** sous 10 ko) est
précisément ce qui n'est pas mesuré. Le prix de la variante radicale reste **la rupture totale avec
l'écosystème de composants Blazor**, et **on ne paie pas ce prix sur la foi d'une borne basse**.

**Conséquence assumée — recommandation.** **Phase 2, mais avec un premier livrable imposé et un
ré-arbitrage explicite** :
1. **Corriger les 3 bloqueurs sémantiques + la disposition de `Computed`** (décision n°24). Ils sont hors
   du chemin mesuré aujourd'hui et **sur le chemin principal de la Phase 2**.
2. **Écrire le générateur pour le SEUL compteur**, et **re-mesurer C1/C3/C4 sur sa sortie**. C'est le
   test décisif, et il est **peu coûteux** : une app, un snapshot contre `counter.js`.
3. **Ne trancher RADICAL vs PRUDENT qu'ensuite**, sur ce chiffre-là. Si la sortie du générateur tient
   sous 10 ko et à ces temps, **RADICAL est viable et le prix de la rupture est justifié**. Si elle
   dépasse 10 ko **en tenant les temps C4**, c'est le cas nommé par la spec §8 : **variante PRUDENTE** —
   signaux comme mode de rendu Blazor, réutilisant `Filament.Core` et émettant du C# court-circuitant
   `RenderTreeBuilder`. **Cette décision est reportée parce que la donnée qui la tranche n'existe pas
   encore, pas parce qu'elle est inconfortable.**
4. **Assainir l'appareil avant qu'il n'arbitre à nouveau** : `HARNESS_VERSION` → 1.3.0, harness commité,
   baseline Blazor re-mesurée sous le même build (décision n°31), `--shell-parity` bouclé sur tous les
   labels (n°28), plancher d'allocation calibré et émis avec un verdict (n°30).

---

# Phase 1 — arbitrages de la mesure propre (entrée `BENCH.md` n°4, 2026-07-16)

## 35. BUG 1 — restaurer le marqueur de re-run sur throw, avec un bit **`STALE` SÉPARÉ** de `DIRTY`

**Décision.** `prune(c)` sort du `finally` et passe **DANS le `try`, APRÈS `fn()`** — chemin de succès
**uniquement**. Un `catch` positionne un **nouveau drapeau `STALE`**, **distinct de `DIRTY`**.

**Raison.** Le bug avait **deux trous indépendants**, et n'en corriger qu'un ne suffit pas.
(a) `refresh()` effaçait `DIRTY` **avant** `recompute()` et **rien ne le restaurait** ; (b) le
`finally { prune(c) }` tournait contre un **curseur PARTIEL** : les arêtes que `fn()` n'avait jamais
atteintes étaient **indiscernables** de celles qu'elle avait délibérément cessé de lire, et étaient donc
**déliées**. **Pourquoi `STALE` et pas `DIRTY` — c'est le point non évident** : `propagate()` **élague**
sa marche sur tout nœud déjà porteur de `DIRTY|PENDING`, sur l'invariant *« un nœud marqué a déjà marqué
son sous-arbre »*. Or **un computed marqué par un recompute ÉCHOUÉ n'a marqué RIEN DU TOUT**. Réutiliser
`DIRTY` **empoisonne cet invariant** et `propagate()` **cesse de descendre au-delà du computed en échec,
pour toujours** — le test *« un effet en aval d'un computed qui lève devient définitivement sourd »*
**échoue** sur un build corrigé-de-(a)-seulement. **`STALE` RESTAURE l'invariant au lieu d'affaiblir la
marche.**

**Conséquence assumée.** Le commentaire de `core.ts` affirmant que le `continue` de `propagate()` est
« *une OPTIMISATION d'élagage de marche, pas la garde de correction* » **devient FAUX** dès qu'un
computed peut rester sale par un throw. **Rectifié dans le code**, parce qu'un commentaire faux sur un
invariant est la manière dont le prochain lecteur réintroduit le bug.

## 36. BUG 1 (racine) — la garde va sur l'**INVARIANT**, pas sur la ligne que le test pointait

**Décision.** Le `try/catch` de `refresh()` **enveloppe AUSSI la branche `PENDING`** — donc l'appel à
`checkDirty()`, qui **peut lever** en rafraîchissant des computeds amont — et pas seulement la branche
`DIRTY → recompute()`.

**Raison.** **Le premier correctif ne corrigeait le bug qu'à LA PROFONDEUR QUE LE TEST ÉPINGLAIT.**
`refresh()` contenait **le même motif effacer-avant-appel-risqué** que celui corrigé dans `recompute()`
**deux lignes plus haut** : il faisait `c.flags = f & ~PENDING` **puis** appelait `checkDirty(c)`,
**sans `try/catch` ni restauration**. Un computed dont la vérification `PENDING` levait restait
**CLEAN**, **jamais recalculé**, **assis sur une valeur que sa `fn` n'a jamais retournée**. La suite
passait parce que le test épinglé est à **profondeur 1** (`effect → computed → signal`), où `refresh()`
prend la branche `DIRTY` vers le `recompute()` désormais gardé. **Ajouter UN computed à la chaîne** — *la
forme réelle massivement majoritaire* — et le bug de valeur silencieusement fausse était **intégralement
intact** : `a=signal(1); b=computed(a*2); c=computed(b+1)` ⇒ après un throw de `b`, mesuré
**simultanément**, **`b.value === 10` et `c.value === 3`** : l'invariant `c === b+1` **violé
définitivement, sans erreur, et sans qu'aucune écriture ultérieure ne le répare**. **La règle générale
est : TOUTE voie qui efface un marqueur de re-run avant d'exécuter du code pouvant lever DOIT le
restaurer.** La garde est posée sur **cette règle**, pas là où pointait la stack trace.

**Conséquence assumée, et c'est le vrai enseignement de cette passe.** C'est **la signature du
chemin-le-plus-court-vers-le-vert** : la transition 7→0 mesurait « *les entrées épinglées passent* », pas
« *la sémantique est correcte* ». **Un consommateur qui aurait fait confiance à la suite verte aurait
expédié un runtime servant des valeurs fausses depuis toute chaîne de computeds à 2 niveaux ou plus.**
Le test de profondeur est désormais **paramétré sur la PROFONDEUR (`[1,2,3,5]`)** dans
`adversarial.test.ts` : **c'est l'invariant qui est épinglé, plus la profondeur.**

## 37. BUG 1 (seconde voie) — **créer l'arête même si le refresh lève** (`link` dans un `finally`)

**Décision.** Le getter `Computed.value` fait `try { refresh(this) } finally { if (activeSub !== null)
link(this, activeSub) }`.

**Raison.** L'effet définitivement sourd **survivait par une SECONDE voie, non corrigée** : **l'arête
d'abonnement n'était JAMAIS CRÉÉE quand le PREMIER accès à un computed levait.** Le getter faisait
`refresh()` **puis** `link()` ; le throw **saute par-dessus le `link()`**, donc **l'abonné ne s'abonne
jamais**. **`STALE` n'y peut RIEN** : il marque correctement le computed, `propagate()` le traverse
correctement — **puis parcourt `c.subs`, qui est VIDE**. Mesuré : `subCount(c) === 0` après le premier
run ; le computed **est** atteint et **est** marqué à l'écriture suivante, mais **n'a aucun abonné**, donc
l'effet ne re-tourne jamais. **Ce n'est pas un chemin exotique : c'est ce que fait une FRONTIÈRE D'ERREUR
ORDINAIRE** — `effect(() => { try { use(c.value) } catch { showError() } })`, la manière naturelle
d'écrire du code récupérable. L'ancien test ne l'attrapait pas parce qu'il **laissait le computed RÉUSSIR
d'abord**, ce qui **construisait l'arête avant le throw** : il ne testait que la **RÉTENTION** d'arête,
**jamais la CRÉATION**. **Lier avec la version PRÉ-refresh n'est pas un compromis, c'est le principe** :
le computed est `STALE`, donc le `checkDirty()` suivant de l'abonné le rafraîchit, **voit la version
bouger**, et re-exécute. Une version qui paraît périmée sur une arête vers un computed **qui EST périmé**
est simplement **la vérité**.

**Conséquence assumée, nommée plutôt que masquée.** Un computed qui lève **avant d'avoir lu le moindre
signal** n'a de dépendance **nulle part** : **aucune écriture ne peut jamais le re-déclencher**. **Aucun
runtime push ne peut récupérer cela** — il n'y a **rien d'où pousser**. **Hors périmètre, et dit.**

## 38. BUG 2 — isolation par effet, **première erreur gagne, RE-LEVÉE APRÈS le drain**

**Décision.** `try/catch` **par effet** dans `flush()` : **le drain va TOUJOURS jusqu'au bout**. La
**première** erreur est **différée puis re-levée une fois la queue vide**. Les erreurs suivantes sont
**abandonnées**.

**Raison.** Un throw **avortait toute la boucle de drainage** : les effets déjà dépilés-et-effacés
**manquaient définitivement** le changement — laissés propres, non exécutés, **et rien ne les
re-marquait**. La re-levée **différée** préserve la propriété qui compte : **l'erreur surgit toujours
SYNCHRONEMENT sur le site d'écriture**. Vérifié : **même instance d'`Error`**, `.stack` nommant toujours
le corps de l'effet utilisateur, **et l'effet frère tourne désormais** (`[0,1]` contre `[0]` en
baseline). **NE PAS avaler : un catch silencieux est un bug à lui seul**, et de la même famille que celui
qu'on corrige. Première-erreur-gagne plutôt qu'un `AggregateError` : **rapporter la première cause vaut
mieux qu'un agrégat que personne ne lit**, et coûte **0 octet**.

**Conséquence assumée.** Les erreurs des effets **suivants** sont **perdues** — un effet qui lève pendant
qu'un autre a déjà levé ne sera **jamais** rapporté. Assumé : la **première cause** est presque toujours
la vraie ; les suivantes sont typiquement des **dommages collatéraux** de la première.

## 39. BUG 3 — clés dupliquées : **TRAITER au runtime, NE PAS lever**

**Décision.** Une ligne : `keyToNew.delete(r.k)` (`list.ts`). **Aucun throw, aucun avertissement dev.**
Sémantique : **la première ancienne ligne gagne l'identité, les doublons excédentaires sont démontés.**

**Raison, et c'est la suite elle-même qui tranche.** `x.set([1,1,2])` **asserte `dom() === [1,1,2]`** :
**tout throw échouerait dès le PREMIER `set`**. Indépendamment : **un throw dev-only, éliminé par
tree-shaking, laisse la PROD faire la corruption** — le seul résultat **PIRE** que l'une ou l'autre
option. Et **la vérification à la Blazor qu'on écrirait (« la NOUVELLE liste a des clés dupliquées ») NE
SE DÉCLENCHERAIT MÊME PAS ICI** : `[1,1,2] → [2,1]` a une **nouvelle** liste **sans doublon** — **la
corruption vient de l'ANCIENNE**. Mécanique : deux anciennes lignes partageant une clé résolvaient vers
**le même `ni`** (la seconde écrasait `rows[ni]`, la première orpheline) **et** incrémentaient **toutes
deux** `patched` — qui comptait donc des **revendications**, pas des **cases remplies**, **dépassait
`toPatch`**, et la garde **démontait une ligne SURVIVANTE**. **Consommer la clé rend chaque revendication
exclusive** : `patched` recompte des **cases** et **ne PEUT plus** excéder `toPatch` — ce qui rend la
garde existante **correcte RÉTROACTIVEMENT, au lieu de simplement chanceuse**.

**Conséquence assumée.** Blazor/Svelte **lèvent** ; Vue/React **avertissent en dev**. Filament ne fait
**ni l'un ni l'autre**, et c'est délibéré : **rejeter un `@key` dupliqué appartient au COMPILATEUR de la
Phase 2**, où c'est une **propriété STATIQUE du template**, **rapportable contre la source** — au lieu
d'un throw runtime pointant du JS généré. **Le contrat du runtime est plus étroit et absolu : ne JAMAIS
corrompre le document.** Vérifié au-delà des repros connus : 11 transitions inédites plus un **fuzz de
400 itérations × 5 étapes** sur un espace de 3 clés — **zéro corruption**.

## 40. BUG 4 — plafond de cycle **dans `flush()`**, **valeur = 1e6**, imposée par notre propre suite

**Décision.** `CYCLE_CAP = 1_000_000` dans `flush()`. Lève `Filament: cycle detected`.

**Raison.** **Une cascade qui termine et une qui ne termine pas ont LA MÊME FORME** ; seul « *a-t-elle
convergé ?* » les distingue — d'où **un plafond, dans le drain**, et non une détection structurelle.
**La VALEUR est FORCÉE par notre propre suite** : le test *« la cascade d'auto-écriture est PLATE »*
**épingle une cascade de 200 001 exécutions comme LÉGITIME**, donc **un plafond à la Solid (100)
REJETTERAIT du code que ce dépôt DÉCLARE CORRECT**. 1e6 ≈ **5× ce plafond**. **C'est une garde de
VIVACITÉ, pas de correction — d'où l'erreur volontairement HAUTE.** Mesuré : baseline **pend pour
toujours** (tuée à 15 s, **zéro sortie**) ; corrigé **lève en 22 ms**, self-write **et** mutuel ; la
cascade légitime de 200 k **n'est pas rejetée**, ni la chaîne de 100 computeds, ni la cascade de 300 k.

**Conséquence assumée, et c'est un COÛT RÉEL, pas une formalité.** **Ce n'est pas une détection de cycle,
c'est une HEURISTIQUE DE LONGUEUR DE DRAIN**, et **quand elle se déclenche elle SAUTE des effets**.
Prouvé : à `CYCLE_CAP = 100`, un graphe **parfaitement ACYCLIQUE** de 200 effets indépendants sur un
signal **lève « cycle detected »** et **100 des 200 effets ne tournent JAMAIS** — marqueurs effacés, **UI
périmée jusqu'à la prochaine écriture de leurs deps**. À 1e6 c'est **inatteignable pour toute app
réaliste** (le bench rows : ~1 000 effets) — **mais dans le cas faux-positif, le message d'erreur est
FAUX et des effets sont silencieusement sautés.** Assumé, **et publié** (entrée n°4, réserve n°O).

**Bug introduit puis attrapé, publié parce que c'est le plus instructif de la passe.** Le drain de cycle
laissait d'abord les effets **spectateurs** `DIRTY`-mais-**non-enfilés** — **le seul état dont
`propagate()` ne peut PAS se remettre**. Un effet **innocent**, simplement **enfilé derrière** un cycle,
devenait **définitivement sourd**. **Le correctif d'un bug de surdité en recréait un.** Démontré, puis
corrigé (**+5 o**).

## 41. Le **5ᵉ bug non listé** — `runEffect()` avait le trou **identique** à `recompute()`

**Décision.** Corrigé, **sans coût en octets** (une ligne déplacée **dans** le `try`).

**Raison.** `runEffect()` présentait **exactement** le même trou d'élagage-sur-throw : **un effet qui lève
en cours de run larguait toutes les dépendances qu'il n'avait pas encore atteintes** et devenait
**définitivement sourd** à leur égard. **Trouvé en cherchant la CLASSE du bug plutôt que ses instances
listées** — c'est le même raisonnement que la décision n°36, appliqué avant qu'un audit ne l'impose. **Non
demandé par le brief ; corrigé quand même**, parce que le laisser aurait signifié **corriger le symptôme
et publier la cause**.

## 42. Le coût en octets de la correction — **payé**, et le budget qui compte n'est PAS celui de l'app

**Décision.** **+110 à +122 o gzip sur le fil** par app (counter 2 864 → **2 976** ; rows 4 243 →
**4 365**). **Payé. Aucune correction rognée pour tenir.** Coût gzip par correctif : BUG 1 **+21 o**,
BUG 2 **+27 o**, BUG 3 **+8 o**, BUG 4 **+59 o**, `runEffect` **~0**.

**Raison — et elle RECTIFIE la prémisse du brief.** Le brief citait la marge C1 de l'app **rows**
(4 243 o contre un gate de 10 000, **2,36×**) pour conclure que « *payer quelques centaines d'octets est
CORRECT et attendu* ». **C'est le mauvais budget.** Le gate **propre au runtime** est `scripts/size.mjs`
`BUDGET = 2048`, et le runtime était à **1 812 o** : **236 o de marge — pas « quelques centaines »**. **Il
n'y a JAMAIS eu la place de payer « quelques centaines d'octets » ici.** Le premier jet sortait à
**1 975 o** (73 o restants) ; la garde de cycle a été **restructurée pour réutiliser la boucle de drain
principale**, récupérant **~44 o**. **Arbre livré, vérifié à la rédaction : 4 535 o brut / 1 943 o gzip —
105 o de marge, PASS.** Contre le gate d'**app**, le coût vaut **~1,1 % d'un budget de 10 000 o** avec
**56–70 % de marge restante** : **C1 n'est pas près d'échouer.**

**Conséquence assumée.** **Le budget de 2 048 o du RUNTIME est désormais la contrainte LIANTE de la
Phase 2, pas le bundle d'app.** C'est le chiffre à surveiller. **Le chemin chaud reste sans allocation** :
**`gc_events = 0` à 50 000 000 d'incréments**, avant **et** après (à 16 o/op cela ferait 800 Mo et ne
pourrait pas rester à zéro GC) ; delta de tas **plat sur un balayage 50×** (o/op → 0,0002 **et
décroissant** ⇒ une **constante**, pas un coût par opération) ; débit **~93 M op/s avant vs ~89–100 M op/s
après** — **dans le bruit inter-runs. Le `try/catch` ne coûte rien de mesurable.**

## 43. L'identité du harness devient un **HASH DE CONTENU** — parce que la chaîne écrite à la main a échoué, silencieusement

**Décision.** `computeHarnessIdentity()` calcule **au runtime** un sha256 par fichier source du harness,
agrégé sur les lignes `"nom:sha256"` **triées** (indépendant du chemin, de l'ordre, de la machine), écrit
dans `environment.harness` de **CHAQUE** JSON de résultat. **Périmètre** : `bench.mjs`, `server.mjs`,
`expected-labels.json`. `HARNESS_VERSION` est **conservé et porté à 1.3.0**, **annoté DANS LE CODE comme
ÉTIQUETTE, PAS PREUVE**.

**Raison — l'échec est CONSTATÉ, pas hypothétique.** `HARNESS_VERSION` était **tenu à la main** et est
**resté `"1.2.0"` à travers un diff de 701 lignes**. Résultat : le Blazor de l'entrée n°2 et le Filament
de 18:20 **revendiquaient tous deux « 1.2.0 »** tout en ayant été produits par des harness
**matériellement différents**. **La chaîne AFFIRMAIT une comparabilité qui n'existait pas, et RIEN ne
pouvait le détecter** — c'est la définition d'une revendication **infalsifiable**, et c'est exactement le
défaut que la décision n°31 avait relevé sans pouvoir le clore. **Un hash de contenu ne peut pas mentir
sur ce qu'il n'a pas lu** : il **EST** une mesure de l'appareil, pas une déclaration à son sujet.
**`computeHarnessIdentity()` LÈVE plutôt que de dégrader** — un run incapable d'établir son identité
**doit s'arrêter**, car **un hash nul est EXACTEMENT la revendication infalsifiable qu'on remplace**.

**Pourquoi ce périmètre, et pourquoi ces exclusions.** `bench.mjs` porte le driver, la primitive de
chronométrage **et `inPageHarness()` — une FONCTION DANS CE FICHIER**, injectée par `addInitScript` : il
n'existe donc **aucun asset in-page séparé** susceptible d'échapper au hash (**vérifié** : les seuls
`readFile` visent les fichiers hashés). `server.mjs` porte la négociation et **les niveaux de
compression** — **changer la qualité brotli déplace TOUS les poids sans toucher `bench.mjs`**.
`expected-labels.json` est la **fixture d'or** : elle **DÉFINIT** ce que « la même charge de travail »
signifie. **Exclus** : `selftest.mjs` (**jamais** sur le chemin de mesure) et `package*.json`
(Playwright/Chrome sont **déjà observés** dans `environment`).

**Conséquence assumée.** Le hash **`47e7e46f…` est partagé par les 12 configs**, et **l'analyse REFUSE
d'émettre une comparaison inter-configs si l'ensemble des hashs n'est pas de cardinal 1** : **la
comparabilité est désormais une précondition VÉRIFIÉE PAR LA MACHINE, pas une affirmation dans un
rapport**. **Re-calculé indépendamment à la rédaction de l'entrée n°4 : reproduit exactement**, les trois
hashs par fichier inclus. La décision n°31 est ainsi **close sur sa cause racine**, pas sur son symptôme.

## 44. Mesurer C1 sur les **octets DU FIL**, la base la plus SÉVÈRE, alors qu'une base plus flatteuse existait

**Décision.** C1 est prononcé sur `encodedDataLength` (**en-têtes de réponse INCLUS**) : **2 976** /
**4 365** o. L'aperçu au build (somme des frères gzip, **hors en-têtes**) donne **2 142** / **3 531** o.

**Raison.** L'aperçu est **~830 o plus flatteur** et aurait passé le gate plus confortablement. Il est
**rejeté** pour deux motifs. (a) **C'est le champ d'où venaient les 2 864 / 4 243 de l'entrée n°3** : le
**delta des correctifs n'est comparable terme à terme que sur cette base** — changer de base **en même
temps** qu'on annonce un surcoût aurait rendu le surcoût **inauditable**. (b) **L'utilisateur télécharge
les en-têtes.** Le build **rapporte contre les DEUX lectures de « 10 ko »** (10 000 décimal et 10 240
binaire) parce que **la spec est ambiguë** — **et ni elle ni nous ne tranchons cette ambiguïté en
choisissant la lecture flatteuse**. **Le gate passe des deux côtés.**

**Conséquence assumée.** On publie un chiffre **~28 % plus élevé** que celui qu'on pourrait défendre, et
on le fait **dans l'entrée qui annonce une régression de poids**. C'est le coût d'une base stable.

## 45. **Conserver** `verify-independent.test.ts`, et **NE PAS** retourner les tests de cycle dans cette passe

**Décision.** Les 34 tests de `test/verify-independent.test.ts` (écrits par le vérificateur sceptique)
**restent dans l'arbre** : suite livrée **212 tests, tous verts**. Les deux tests de cycle
**adversariaux** sont **laissés en l'état**, et le défaut est **publié** (entrée n°4, réserve n°I).

**Raison.** Le correctif de BUG 4 a **ZÉRO couverture** dans la suite adversariale — **prouvé par
mutation** : `CYCLE_CAP = Number.MAX_SAFE_INTEGER` (détection **entièrement désactivée**) ⇒ **la suite
adversariale passe intégralement**. Pire : les deux tests de cycle **documentent encore le bug comme
PRÉSENT**. Ils installent leur **propre disjoncteur à 50 000** — qui se déclenche **très en deçà** du
plafond 1e6 — et **assertent POSITIVEMENT** `.toThrow('RUNAWAY')` et
`expect(runs).toBeGreaterThan(1000)` (« *Documenting the observed behaviour: it really did spin* »). **La
suite épingle donc le comportement BUGUÉ comme attendu** : une régression supprimant la détection
**rapporterait vert**, et un vrai correctif est **indiscernable d'aucun correctif**.
`verify-independent.test.ts` est **l'UNIQUE couverture de BUG 4** (self-write, mutuel, anneau à 3,
**sans disjoncteur de test**) et **l'unique couverture fuzz** des clés dupliquées : **le supprimer
rouvre la lacune**. **Une garde sans test est exactement ce qui pourrit.**

**Conséquence assumée.** Retourner les tests adversariaux **change la sémantique documentée** d'une suite
déclarée **RED-state** : c'est un **arbitrage de propriétaire**, pas une correction de passage. **Il est
donc PUBLIÉ comme réserve ouverte plutôt qu'exécuté silencieusement** — même règle que la décision n°33.
**À faire dans la passe suivante**, avec l'assertion explicite que `CYCLE_CAP` est **au-dessus** de la
cascade de 200 k, pour que les deux contraintes soient **visiblement liées**. *(Note : le correcteur
justifiait son abstention par « *vous avez spécifié 171/171, je ne voulais pas déplacer les poteaux* » —
mais il **avait déjà ajouté 7 tests adversariaux sans le dire**. Voir n°46.)*

## 46. Publier que **le rapport de correction a menti sur la suite de tests** — et pourquoi les correctifs tiennent quand même

**Décision.** L'inexactitude est **publiée** (entrée n°4, réserves n°J et n°L), et **le verdict de
confiance repose sur le TEST DE RÉVERSION, PAS sur l'auto-déclaration du correcteur**.

**Raison.** Le rapport affirme « *The adversarial suite is byte-for-byte intact — 52 tests* », « *every
test file predates my session (latest 18:38)* » et « *I did not add a test … I didn't want to silently
move the goalposts* ». **Les trois sont faux** : `adversarial.test.ts` porte **mtime 19:34:17** —
**à l'intérieur** de la session de correction — et contient **59 tests, pas 52**. La suite livrée compte
**212 tests**, ni 171 ni 178. **Et aucune baseline git n'existait** (`git ls-files src/filament-runtime/`
⇒ **0** ; tout en `??`) : **l'instruction du brief « diffez `src/` ET `test/` contre HEAD » était
INEXÉCUTABLE par quiconque**, et **la provenance des tests inauditable par construction**. Le
vérificateur a donc substitué le **test de réversion** — ré-introduire **chaque bug individuellement** et
confirmer que la suite **ACTUELLE** l'attrape (BUG 1 ⇒ **9** tests adversariaux rouges ; BUG 2 ⇒ **1** ;
BUG 3 ⇒ **2** ; BUG 4 ⇒ **la suite reste VERTE**, ce qui **confirme** la lacune de n°45) — **strictement
plus fort qu'un diff** : **un test affaibli serait resté vert**. **Le sens de l'inexactitude est le
DURCISSEMENT** (les tests ajoutés incluent la boucle paramétrée en profondeur de n°36, dont le
commentaire dit « *a guard on only one branch passes everything above while c !== b + 1 permanently* » —
le correcteur a **tenté un correctif partiel, l'a trouvé insuffisant, et a durci le test**), **donc aucun
faux correctif n'est dissimulé et les correctifs TIENNENT.**

**Conséquence assumée.** « *Je n'ai pas touché aux tests* » est **exactement** la revendication sur
laquelle un vérificateur s'appuie ; **elle était fausse**. **La cause racine est STRUCTURELLE, et cette
entrée la clôt : le runtime, le harness et les résultats sont désormais COMMITTÉS.** **Sans baseline git,
aucun cycle correction/vérification futur n'a de sens** — c'était la condition de possibilité de l'audit,
et elle manquait. La **dérive numérique** est publiée au même titre : annoncé **4 510 / 1 936 / 112 o**,
**livré 4 535 / 1 943 / 105 o**. **Le rapport ne décrivait pas l'arbre livré.**

## 47. Publier le **confond d'ordre** que le rapport de mesure ne divulguait pas

**Décision.** Publié comme réserve **n°F** de l'entrée n°4, **avec le sens du biais énoncé**.

**Raison.** L'exécution était **séquentielle** (**aucun recouvrement** — vérifié : chaque config démarre
~0,5 s après la fin de la précédente) **mais PAS ENTRELACÉE** : **tout Blazor a tourné en premier
(19:50–20:01), tout Filament en dernier (20:02–20:07)**. **La dérive thermique/machine est donc CONFONDUE
avec le framework**, et la section « quiescence » du rapport amont **ne le mentionne jamais** tout en
revendiquant un protocole « *held, unweakened* ». **Le sens est CONSERVATEUR** : Filament est mesuré **en
dernier, sur la machine la plus chaude** — **le confond joue CONTRE Filament**, donc le verdict n'est pas
menacé. **Publié quand même**, parce que **c'est au lecteur d'établir le sens du biais, pas à nous de le
lui dire après coup** — et parce qu'un confond non divulgué **dont le sens nous arrangerait** serait
découvert par le prochain lecteur, qui aurait alors raison de douter du reste.

**Conséquence assumée — action.** **ENTRELACER les configs** dans tout run futur. C'est **gratuit** et
cela **supprime** le confond au lieu de le borner.

## 48. Rectifier le `deltaVsPrevious` du rapport amont — les erreurs allaient **toutes dans le sens flatteur**

**Décision.** Les **14** scénarios Blazor sont publiés **INTÉGRALEMENT** dans l'entrée n°4, **aucun
omis**, avec les **trois réfutations** énoncées en toutes lettres.

**Raison.** Le rapport amont est **faux sur trois points**, **confirmés indépendamment ici depuis les
JSON bruts et la table de l'entrée n°2**. (1) « *all Blazor got SLOWER* » — **FAUX** : `rows-nojit/swap`
va **12,65 → 12,60 ms** (**plus RAPIDE**). **13/14, pas 14/14.** Un demi-quantum, **négligeable en
magnitude — mais un universel énoncé est falsifié par les données du run lui-même.** (2) « *warm
perturbation is single-digit percent* » — **FAUX** : `counter-nojit/increment-warm` fait **+26,9 %**,
**la plus grande perturbation CHAUDE du run**, et était **ENTIÈREMENT ABSENTE** de la section dont c'est
**l'unique objet**, pendant que son voisin **PLUS PETIT** `counter-aot/increment-warm` (+10,0 %) y
**figurait, une ligne plus loin**. Également omis : `rows-nojit/clear` (+14,3 %) et `rows-nojit/update`
(+7,5 %). **TOUTES les omissions sont des lignes `nojit`.** (3) La fourchette « *+0,7 % à +6,6 % sur
create-warm* » — **INFONDÉE** : les deux valeurs réelles sont **+6,1 %** et **+6,6 %**.

**Pourquoi cela compte alors que les magnitudes sont petites.** **La dérive de Blazor VERS LE HAUT est
EXACTEMENT ce qui GONFLE les rapports de Filament.** Une section qui **sous-estime** cette dérive **fait
paraître la stabilité de ces rapports mieux établie qu'elle ne l'est** — **l'erreur va dans le sens qui
nous arrange**. *En toute équité : l'argument du rapport lui-même (« un quantum sur une valeur de 1 ms,
c'est de la résolution, pas du signal ») **couvrirait** le +26,9 % — il vaut +0,35 ms, ~3 quanta. **Mais
il ne l'applique jamais, parce qu'il ne liste jamais la ligne.*** **C'est un défaut de RAPPORT, pas de
MESURE** : la mesure a **survécu à la réfutation** (hash **reproduit indépendamment**, poids Blazor
**byte-identique sur 8/8 configs**, planchers signalés avec **fidélité EXACTE** aux échantillons bruts —
3/10 et 5/10 zéros, vérifiés). **Le précédent de la décision n°33 s'applique à notre propre rapport, sans
exception.**

## 49. Le verdict C4 de l'entrée n°3 est **RENFORCÉ**, mais ses **RAPPORTS sont SUPERSEDED**

**Décision.** **Verdict : TIENT** (*a fortiori*). **Rapports : à RESTATER depuis l'entrée n°4**, **pas** à
annoter en note de bas de page. **Comparaison de POIDS de l'entrée n°2 : NON invalidée du tout.**

**Raison.** **Blazor est plus lent sur le harness propre dans 13 scénarios sur 14.** L'ancien registre
comparait donc **un Blazor mesuré sur le harness RAPIDE** à **un Filament mesuré sur le LENT** : **le
biais courait CONTRE Filament**, exactement comme la tâche l'anticipait. **Un verdict qui a survécu à un
test biaisé CONTRE lui survit *a fortiori* au test non biaisé.** Et la robustesse a été **testée, pas
supposée** : recalculé contre les **ANCIENS** chiffres Blazor (les plus rapides), **chaque scénario passe
encore** — `create-warm` **2,01×**, `update` **12,00×**, `swap` **8,63×**, `clear` **1,71×** (la plus
étroite), `increment-warm` **10,00×** (borné). **Aucune perturbation de l'ampleur observée ne menace le
verdict.** **Mais chaque temps Blazor a bougé, donc CHAQUE rapport de la table C4 de l'entrée n°3 est
FAUX** — et ils **sous-estimaient** Filament. **Un chiffre faux dans le sens flatteur reste un chiffre
faux** : on ne garde pas un rapport parce qu'il nous dessert. Le **poids**, lui, reproduit **à l'octet
près sur 8/8 configs** : **rien à rectifier de ce côté**, et c'est **le contrôle qui isole le delta comme
purement temporel**.

## 50. Ce que le `PASS` de l'entrée n°4 **ne dit pas** — la décision n°34 reste ouverte

**Décision.** `gateVerdict = PASS` **pour C1 et C4**. **La Phase 1 n'est TOUJOURS PAS déclarée
franchie.** La décision n°34 (**gate CONDITIONNEL**) **reste en vigueur**.

**Raison.** **Une seule** des deux conditions de la n°34 a bougé : les **3 bloqueurs sémantiques + la
garde de cycle** sont **corrigés et vérifiés par réversion** (n°35–41), et **deux voies supplémentaires**
que le premier correctif laissait ouvertes ont été fermées (n°36, n°37). **L'autre condition est
INTACTE : le générateur n'existe toujours pas** — `src/Filament.Generator/`, `src/Filament.Core/` et
`src/Filament.Analyzer/` restent **vides**. Les chiffres portent donc **toujours** sur l'**answer key
écrite à la main** (décision n°21), et **la proposition porteuse — *« un générateur C# émet ceci sous
10 ko à ces temps »* — n'est TOUJOURS PAS testée.** S'y ajoute que **C3 n'a PAS été re-mesuré** dans ce
run (`--c3` désactivé) : **cette entrée ne dit RIEN sur C3**. Et la **fuite de disposition de `Computed`**
(n°24) — **désormais corrigée dans le code** (le constructeur s'enregistre auprès de `owner`) — reste
accompagnée d'une **verrue préexistante NON corrigée** : **disposer un `computed` laisse silencieusement
les effets en aval sur une valeur périmée**, aujourd'hui **bénie par un test** (`adversarial.test.ts:485`)
plutôt que corrigée. **Même classe que BUG 1/BUG 2 : valeur fausse silencieuse.**

**Conséquence assumée — le livrable imposé est INCHANGÉ.** **Écrire le générateur pour le SEUL compteur,
et re-mesurer C1/C3/C4 sur SA sortie.** **RADICAL vs PRUDENT ne se tranche qu'ensuite** — **la donnée qui
tranche n'existe toujours pas.** **Ce qui a changé, c'est qu'on ne construira plus le générateur au-dessus
d'un runtime qui sert des valeurs silencieusement fausses depuis toute chaîne de computeds à deux
niveaux.** C'était le point 1 de la recommandation de la n°34 ; **il est fait.** Restent les points 2, 3
et une partie du 4 (**`--shell-parity` sur tous les labels** n°28 ; **plancher d'allocation calibré**
n°30 — **toujours ouverts**).
