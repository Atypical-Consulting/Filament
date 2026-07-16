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
