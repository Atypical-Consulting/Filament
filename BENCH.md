# BENCH.md — Registre de mesures (append-only)

> **Ce fichier est en AJOUT SEUL (append-only).**
> On n'édite jamais, on ne corrige jamais et on ne supprime jamais une entrée existante.
> Un chiffre erroné est corrigé par une **nouvelle entrée** qui référence et rectifie
> explicitement l'ancienne. L'historique des mesures fait partie de la preuve : une entrée
> réécrite après coup n'est plus une mesure, c'est une affirmation.
>
> Toute entrée doit porter : sa date, son environnement complet, son protocole exact,
> la commande permettant de la rejouer, et ses réserves connues.
> Un scénario en échec se consigne **comme un échec**, jamais comme une lacune silencieuse.

---

## Entrée n°1 — 2026-07-16 — Phase 0 : baseline Blazor WebAssembly

Première entrée du registre. Établit la ligne de base contre laquelle les critères **C2**
(poids) et **C4** (vitesse) seront jugés.

### Environnement

| Élément | Valeur |
|---|---|
| Machine | Apple M5 Max (Mac17,6), 18 cœurs, 64 Gio de RAM (68 719 476 736 o) |
| Alimentation | Secteur (AC), aucun throttling thermique/puissance relevé (`pmset -g therm` propre) |
| OS | macOS 26.5.1 (build 25F80), Darwin 25.5.0, arm64 |
| Navigateur | Google Chrome 150.0.7871.124 (canal `chrome`) |
| Pilotage | Playwright 1.61.1, Node v26.5.0 |
| .NET SDK | 10.0.301 (`wasm-tools` 10.0.109) |
| Headless | **oui** (`--headless`) |
| Harness | `bench/harness/bench.mjs` v1.1.0 (`harnessVersion` maintenu à la main) |
| Date des runs | 2026-07-16, 10:51–10:58 UTC |

**Charge machine au moment des runs (divulgation honnête)** : 90,56 % d'inactivité, load average
2,09 sur 18 cœurs. Deux démons de l'utilisateur consommaient ~0,2 cœur chacun
(`logioptionsplus_updater` ~20–26 %, `OrbStack Helper` ~20 %), soit ~2,3 % de la capacité totale.
Ils **n'ont pas été tués** : OrbStack est le runtime Docker de l'utilisateur (risque d'interrompre
un travail en cours) et l'agent Logitech se relance seul. Les runs étaient **strictement
séquentiels**, jamais parallèles. La machine n'était donc **pas totalement quiescée** — voir les
réserves ouvertes.

### Protocole de mesure (section 7 de la spec)

Protocole appliqué, tel qu'exécuté, et suffisant pour rejouer :

1. **Serveur** — `bench/harness/server.mjs` sert la racine statique (`<publish>/wwwroot`) en local.
   Négociation de contenu par requête, avec le sibling précompressé (`.br`/`.gz`) émis par
   `dotnet publish` quand il existe. `Cache-Control: no-store` sur toute réponse.
2. **Encodage — plafonné à gzip** (`--max-encoding gzip`). Vérifié sur le fil :
   `Content-Encoding: gzip` sur **39/39** réponses framework de chaque config ;
   `index.html` = 922 o, `dotnet.native.wasm` = 582 769 o, identiques octet pour octet aux siblings
   `.gz`. Aucun octet brut servi à la place d'un octet compressé.
3. **Cache froid** — un `BrowserContext` neuf par itération, donc un cache vide par itération.
   Prouvé et non affirmé : `transferredBytesToInteractive` est **constant sur les 10 runs** de
   chaque config (un cache chaud l'aurait effondré).
4. **Service workers bloqués** (`serviceWorkers: "block"`), pour que la garantie « cache froid » et
   le comptage d'octets tiennent par construction.
5. **Poids** — somme des `encodedDataLength` (CDP `Network.loadingFinished`) sur **toutes** les
   requêtes, depuis la navigation jusqu'à ce que le hook de contrat soit cliquable **et** que le
   réseau soit resté inactif 2 000 ms. Ce sont des **octets sur le fil**, ni la taille disque ni le
   décompressé. 3 runs de poids (`--weight-runs 3`).
6. **Temps** — horodatage **in-page**. Un clic, puis attente d'un prédicat DOM piloté par
   `MutationObserver` ; **l'horloge s'arrête dans le callback de l'observer**, à l'instant où le
   prédicat est vrai (`msToMutation`, la métrique de tête). Aucune frontière de frame n'est utilisée
   comme proxy de complétion.
7. **Statistiques** — **médiane et IQR (p75−p25)**, `n = 10` (`--runs 10`), **jamais la moyenne**.
8. **Un timeout est un échec, jamais un nombre.** Garde anti-vacuité : un prédicat déjà vrai avant
   le clic lève une exception au lieu de rapporter 0.
9. **Égalité de charge de travail** — fixture `expected-labels.json`
   (sha256 `72733c72c80a1a6d5c984d0468ce421bd294babcbfe218a100f3290d38006168`) vérifiée en page
   contre les lignes 0–4 et 999 des deux configs Rows : les deux exécutent le même flux LCG
   Park-Miller.

#### Commande de rejeu

```bash
# 1. Publication (par config). Purger obj/ et bin/ ENTRE chaque config : le cache
#    static-web-assets est empoisonné par la bascule AOT/non-AOT (voir DECISIONS.md).
rm -rf baseline/Rows.Blazor/obj baseline/Rows.Blazor/bin
dotnet publish baseline/Rows.Blazor    -c Release -o bench/publish/blazor-rows-nojit    -p:RunAOTCompilation=false
dotnet publish baseline/Rows.Blazor    -c Release -o bench/publish/blazor-rows-aot      -p:RunAOTCompilation=true
dotnet publish baseline/Counter.Blazor -c Release -o bench/publish/blazor-counter-nojit -p:RunAOTCompilation=false
dotnet publish baseline/Counter.Blazor -c Release -o bench/publish/blazor-counter-aot   -p:RunAOTCompilation=true

# 2. Mesure. La racine statique est <out>/wwwroot, JAMAIS <out>.
node bench/harness/bench.mjs \
  --dir bench/publish/blazor-rows-nojit/wwwroot \
  --app rows --label blazor-rows-nojit \
  --runs 10 --weight-runs 3 --max-encoding gzip --headless --no-aot \
  --out bench/results/blazor-rows-nojit.json

node bench/harness/bench.mjs \
  --dir bench/publish/blazor-rows-aot/wwwroot \
  --app rows --label blazor-rows-aot \
  --runs 10 --weight-runs 3 --max-encoding gzip --headless --aot \
  --out bench/results/blazor-rows-aot.json

# idem pour les deux configs counter avec --app counter
```

> **Réserve de reproductibilité** : ces commandes sont consignées **ici et nulle part ailleurs**.
> `RunAOTCompilation` n'apparaît dans aucun `.csproj`, aucun script, aucune CI. Voir les réserves.

JSON bruts : `bench/results/{blazor-counter-nojit,blazor-counter-aot,blazor-rows-nojit,blazor-rows-aot}.json`
Référence brotli secondaire : `bench/results/brotli-weight-reference.json`

---

## Résultats — Phase 0

### Poids transféré (octets gzip sur le fil, cache froid, médiane de 3 runs de poids)

| Config | App | AOT | Octets gzip | Kio | Requêtes |
|---|---|---|---:|---:|---:|
| `blazor-counter-nojit` | Counter | non | **1 885 505** | 1 841,3 | 39 |
| `blazor-counter-aot`   | Counter | oui | **4 849 865** | 4 736,2 | 39 |
| `blazor-rows-nojit`    | Rows    | non | **1 889 184** | 1 844,9 | 40 |
| `blazor-rows-aot`      | Rows    | oui | **4 858 810** | 4 744,9 | 40 |

Poids parfaitement déterministe : écart (max−min) = **0 octet** sur les 3 runs de poids, dans les
4 configs. Recoupement CDP↔serveur : delta de 270–271 o/requête partout = exactement les en-têtes de
réponse, et CDP ≥ corps serveur partout (aucun sous-comptage). Octets chargés paresseusement à
l'interaction : **0** dans les 4 configs.

Les configs Rows font 40 requêtes : 39 en gzip + `favicon.png` (1 148 o) en `identity`. C'est
**correct** et non un trou de compression — un PNG est déjà entropy-codé, `publish` n'émet pas de
`.gz` pour lui, et la denylist du serveur refuse à raison de le recompresser.

**L'AOT alourdit** : il compile le code natif dans `dotnet.native.wasm` (3,6 Mio gzip contre
0,56 Mio interprété), soit **2,57× le poids total** de la page. C2 et C4 tirent donc en **sens
opposés** pour Blazor.

### Base brotli (mesurée, secondaire — voir la décision dans DECISIONS.md)

Le serveur négocie brotli par défaut auprès de Chrome. La base gzip retenue ci-dessus est donc la
base **conservatrice (borne haute)**, généreuse envers Filament. Base brotli **mesurée** (même
harness, même cache froid, `--max-encoding br`) :

| Config | gzip (o) | brotli (o) | Écart |
|---|---:|---:|---:|
| `blazor-counter-nojit` | 1 885 505 | 1 551 590 | −17,71 % |
| `blazor-counter-aot`   | 4 849 865 | 3 351 531 | −30,89 % |
| `blazor-rows-nojit`    | 1 889 184 | 1 554 591 | −17,71 % |
| `blazor-rows-aot`      | 4 858 810 | 3 351 937 | −31,00 % |

⚠️ Les **temps** des runs brotli ont été pris à `--runs 1` : ils **ne sont pas valides au
protocole** et n'ont servi qu'à extraire le poids (légitime, puisque l'écart de poids est de 0 o).

### Temps — Rows (msToMutation, médiane et IQR, n = 10)

| Scénario | non-AOT médiane | non-AOT IQR | AOT médiane | AOT IQR | Gain AOT (brut) |
|---|---:|---:|---:|---:|---:|
| `create` (1000 lignes) | **35,40 ms** | 5,55 | **23,45 ms** | 1,275 | 1,51× |
| `update` (1 ligne / 10) | **12,95 ms** | 0,55 | **3,20 ms** | 0,175 | 4,05× |
| `swap`   | **12,85 ms** | 1,20 | **3,55 ms** | 0,10 | 3,62× |
| `clear`  | **4,10 ms** | 0,20 | **2,70 ms** | 0,175 | 1,52× |

### Temps — Counter (msToMutation, médiane et IQR, n = 10)

| Scénario | non-AOT médiane | non-AOT IQR | AOT médiane | AOT IQR | Gain AOT |
|---|---:|---:|---:|---:|---:|
| `increment` | **25,55 ms** | 1,525 | **14,40 ms** | 0,775 | 1,77× |

### Échecs

**Aucun échec de scénario, aucun timeout, aucun nombre fabriqué.**
Les 4 configs sont passées. **100 itérations chronométrées** (10 runs × 10 scénarios), 0 échec,
0 timeout. Zéro avertissement CDP, zéro octet non tracé, zéro erreur console/page, zéro problème
de contrat (`contractCheck.problems` vide dans les 4 JSON).

> **Rectification d'un chiffre du rapport amont** : le rapport de mesure annonçait
> « 40/40 itérations chronométrées sur 10 scénarios ». C'est **faux** : le compte réel est de
> **100** échantillons chronométrés (vérifié en re-sommant `n` dans les 4 JSON : 10 scénarios × 10).
> L'erreur **sous-estime** le travail accompli (elle n'avantage personne), mais elle est consignée
> ici parce qu'elle figurait dans un rapport titré « ZERO fabricated numbers ».

**Deux échecs de publication ont eu lieu et ont été résolus** (ce ne sont pas des échecs de
mesure, mais ils sont consignés parce qu'ils conditionnent le rejeu) :
- `blazor-rows-nojit` : premier `publish` mort sur `MSB3073` — `wasm-opt` « expected more elements
  in list / Fatal: error parsing wasm ». Cause : intermédiaires `obj/` périmés/corrompus, pas une
  chaîne d'outils cassée. `wasm-opt` réécrit `dotnet.native.wasm` **sur place**, donc une écriture
  partielle empoisonne les publications suivantes.
- `blazor-rows-aot` : premier `publish` mort sur ~40× `MSB3030` — fichiers `compressed/publish/<hash>-{0}-<hash>.gz`
  introuvables (noter le `{0}` littéral non substitué). Cache static-web-assets périmé.
- **Correctif dans les deux cas** : `rm -rf obj bin` puis republier. Aucun contournement, aucun
  chiffre issu d'une publication douteuse.

---

## Ce que C1, C2 et C4 signifient désormais, en chiffres

### C1 — plafond absolu de bundle

⚠️ **CHIFFRE MANQUANT.** Le critère C1 fixe un plafond absolu de poids, mais **la spec n'est pas
présente sur le disque** (aucun fichier de spec, README ou cahier des charges dans le dépôt ;
recherche effectuée). La valeur cible de C1 **ne peut donc pas être reportée ici sans l'inventer**.
Elle doit être renseignée par une entrée ultérieure, une fois la spec disponible.
Ce qui est mesurable aujourd'hui — le poids de la baseline et la cible dérivée de C2 — est ci-dessous.

### C2 — battre la baseline Blazor d'un facteur 50 sur le poids

La cible **dure** est le **plus petit** bundle Blazor, donc la config **non-AOT** (la config AOT est
2,57× plus lourde : la viser serait choisir la moitié facile).

**Base gzip (retenue, conservatrice) :**

| Cible C2 | Baseline Blazor non-AOT | Filament doit rester sous |
|---|---:|---:|
| **Rows** | 1 889 184 o (1 844,9 Kio) | **37 784 o = 36,90 Kio** gzip |
| **Counter** | 1 885 505 o (1 841,3 Kio) | **37 710 o = 36,83 Kio** gzip |

**Si la base brotli est retenue à la place**, la cible se resserre de ~18 % :

| Cible C2 | Baseline Blazor non-AOT | Filament doit rester sous |
|---|---:|---:|
| **Rows** | 1 554 591 o | **31 092 o = 30,36 Kio** brotli |
| **Counter** | 1 551 590 o | **31 032 o = 30,30 Kio** brotli |

> **Filament doit impérativement être mesuré sous le MÊME `--max-encoding`.** Comparer un Filament
> brotli à un Blazor gzip ne serait pas un ratio de 50×, ce serait un artefact d'encodage.

*(Pour référence seulement, jamais comme titre : la cible 50× contre l'AOT serait 97 176 o /
94,90 Kio gzip pour Rows — soit 2,57× plus permissive. Ne pas l'utiliser.)*

### C4 — ne pas dépasser ces médianes par scénario

La cible **dure** est le **run le plus rapide** de Blazor, donc la config **AOT**.

| Scénario | Filament ne doit pas dépasser (médiane, n = 10) |
|---|---:|
| `create` (1000 lignes) | **23,45 ms** |
| `update` (1 ligne / 10) | **3,20 ms** |
| `swap` | **3,55 ms** |
| `clear` | **2,70 ms** |
| `increment` (Counter) | **14,40 ms** |

**C2 et C4 nomment donc des configs différentes, et c'est délibéré** : non-AOT pour le poids
(le bundle le plus petit), AOT pour la vitesse (le run le plus rapide). Filament affronte la
meilleure moitié de chaque, jamais un homme de paille.

---

## Réserves ouvertes (soulevées par les vérificateurs sceptiques)

Ces réserves ne sont **pas** résolues. Elles sont consignées telles quelles.

### Bloquantes pour l'interprétation de C4

1. **`create` est majoritairement du coût de première interaction, pas de la construction de
   lignes.** `create` est le **premier clic** sur une page fraîchement chargée (le harness recharge
   la page à chaque itération par conception). Incrémenter un `int` ne peut pas coûter 25,55 ms :
   `increment` est donc essentiellement un **plancher de première interaction**. En le soustrayant :
   `35,40 − 25,55 = 9,85 ms` (non-AOT) et `23,45 − 14,40 = 9,05 ms` (AOT) — **61 à 72 % de la
   baseline C4 de 35,40 ms n'est pas de la construction de lignes**. Conséquence directe : *tout
   framework ayant un chemin de première interaction moins cher bat 35,40 ms sans afficher une
   seule ligne plus vite*. (L'inférence utilise Counter comme proxy du coût de boot de Rows ; les
   payloads ne diffèrent que de 0,2 %, donc elle est solide, mais elle devrait être **mesurée**
   directement par un second `create` chronométré après warmup.)
2. **Le gain AOT de 1,51× sur `create` n'est pas une accélération de rendu.** Corrigé du boot :
   `(35,40 − 25,55) / (23,45 − 14,40) = **1,09×**`. L'AOT n'assemble pas les lignes plus vite ;
   il atteint son premier appel plus vite. La note « 1,51× faster than interpreted » du rapport
   amont est une **mauvaise attribution**. En revanche `update` (4,05×), `swap` (3,62×) et
   `clear` (1,52×) sont **propres** : `setupRows()` s'exécute non chronométré avant eux, ce sont de
   vrais gains en régime établi.

### Bloquantes pour la reproductibilité

3. **La commande de publication n'existe nulle part sur le disque.** `RunAOTCompilation` n'apparaît
   dans aucun `.csproj`, aucun script, aucun doc ; il n'y a ni README, ni script de build, ni
   Makefile, ni CI dans le dépôt. La **seule** chose distinguant `blazor-counter-nojit` de
   `blazor-counter-aot` est l'historique shell d'un opérateur. *(Le bloc « Commande de rejeu »
   ci-dessus est le premier remède ; un script versionné reste à écrire.)*
4. **Zéro commit git** : `git log` répond « branch 'main' does not have any commits yet ». Rien
   n'est épinglé. `bench/results/` est dans `.gitignore` (les preuves mesurées ne sont pas
   versionnées) ; `obj/` et `bin/` aussi, donc les artefacts qui **prouvent** l'engagement de l'AOT
   disparaissent à un checkout propre.
5. **Le harness a été modifié dans la session qui a produit les chiffres** (ajout de
   `--max-encoding` à `server.mjs` et `bench.mjs`). Additif, comportement par défaut préservé
   (br), selftest re-passé indépendamment à **249/249**. Non contesté sur le fond, mais résultats et
   outil co-évoluent sans hash ni tag les liant ; `harnessVersion: "1.1.0"` est maintenu à la main.

### Équité de la comparaison à venir

6. **Trou d'équité sur le balisage des lignes.** Le contrat n'exige que `cellsPerRow >= 2` et lit
   `td:nth-child(n).textContent` ; le balisage interne des lignes est **libre**. Blazor émet
   `<tr @key><td class="col-md-1">id</td><td class="col-md-4"><a class="lbl">label</a></td></tr>`
   = **4 éléments/ligne** (4 000 pour 1 000 lignes), dont un `<a class="lbl">` décoratif. Filament
   peut satisfaire le **même** contrat avec 3 éléments/ligne (3 000) — 1 000 nœuds de moins, 2 000
   attributs `class` de moins, pas de résolution de style `a.lbl`/`.col-md-1`. Ce handicap d'environ
   25 % des nœuds DOM est **cuit dans la baseline 35,40/23,45 ms**. → Épingler le balisage exact
   dans le contrat, ou asserter le nombre de nœuds, **avant** de comparer les temps de `create`.
7. **`--aot` est auto-déclaré** (`bench.mjs:1421`), enregistré verbatim et **jamais vérifié** par le
   harness. Le JSON enregistrerait volontiers `aot: true` pour un build non-AOT. C'était correct ici
   (l'AOT a été prouvé indépendamment depuis le wasm natif : `dotnet.native.wasm` 1 494 734 →
   11 362 554 o ; chaîne `"Counter.Blazor"` présente dans le natif AOT et absente sinon ; marqueurs
   `mono_aot` 2 → 34). → Verrouiller sur une empreinte du binaire, pas sur un drapeau CLI.
8. **Les deux apps baseline ne sont pas configurées identiquement.**
   `Counter.Blazor.csproj` déclare `<PublishTrimmed>true</PublishTrimmed>` ; `Rows.Blazor.csproj`
   **ne le déclare pas** et s'appuie sur le défaut SDK Release. Vérifié : les deux ont réellement
   été trimmés aujourd'hui (32 assemblies managées chacune, CoreLib lié à 1 483 776 o), donc
   **aucune différence de résultat aujourd'hui** — mais la baseline C2 dépend silencieusement d'un
   défaut SDK pour une app et d'un drapeau explicite pour l'autre. → Rendre les deux explicites.
9. **Les deux apps ne servent pas le même shell.** Counter utilise
   `<link rel="icon" href="data:,">` (avec un commentaire expliquant que les favicons décoratifs
   « ajoutent du bruit à la trace réseau ») ; Rows expédie `favicon.png` (1 148 o, la 40ᵉ requête) et
   **ignore la justification de son propre projet**. 0,06 % du total, sans effet sur les conclusions,
   mais c'est du bruit gratuit dans une métrique de tête. → Les apps Filament devront servir un shell
   **octet pour octet identique**.

### Autres

10. **Base gzip vs brotli non tranchée** (correctement remontée, ce n'est pas un défaut). Déplace la
    cible C2 de jusqu'à 31 %. Voir la décision dédiée dans `DECISIONS.md`.
11. **La baseline est « Blazor par défaut », pas « Blazor minimal ».** `System.Text.Json` coûte
    149 932 o gzip (~8 % des 1 885 505 o de Counter) et est tiré par
    `Microsoft.Extensions.Configuration.Json` dans le host WASM standard ; une app compteur ne s'en
    sert jamais. Défendable comme baseline « stock », mais ~8 % du poids que Filament doit battre
    par 50× est du défaut de framework mort.
12. **`rows-nojit/create` est simultanément le scénario le plus bruité** (IQR 5,55 ms = 16 % de la
    médiane, contre ≤ 1,2 ms partout ailleurs) **et le plus contaminé par le boot** — et c'est
    exactement la baseline de C4. C'est le seul chiffre méritant un re-run sur machine quiescée.
13. **`msToPaint` ne doit jamais servir à comparer des frameworks** : il porte un décalage additif de
    0 à 16,7 ms dû au vsync et au harness, pas au framework (visible dans les données : chaque
    `msToPaint` est ~10–16 ms au-dessus de son `msToMutation`). Toutes les valeurs de tête ci-dessus
    sont des `msToMutation`.
14. **Sortie ILLink non déterministe** : les quatre CoreLib trimmées font exactement 1 483 776 o,
    mais Counter et Rows diffèrent sur 440 824 octets (29,7 %) alors que leurs tailles gzip ne
    diffèrent que de 15 octets. C'est une permutation/réordonnancement, pas une différence de
    trimming, et cela **n'affecte pas le poids** — mais qui tentera une reproduction octet-exacte
    sera dérouté par des tailles identiques et des hashs différents.
15. **`n = 10`, une seule machine, un seul Chrome, un seul OS.** Pas d'intervalles de confiance, pas
    de réplication inter-machines. Suffisant pour une baseline de POC ; **insuffisant** pour une
    revendication de performance publiable.
16. **`--warmup 1` est structurellement inopérant** : chaque itération, warmup compris, tourne dans
    son propre `BrowserContext`/page/`goto`. Rien ne se reporte (ni module WASM, ni tiering JIT, ni
    code cache V8). Tous les `update`/`swap`/`clear` mesurés sont donc des premiers appels à froid —
    choix défendable, mais ce n'est pas ce que « warmup » annonce.

---

*Fin de l'entrée n°1. Ne pas modifier — ajouter une entrée n°2 pour toute rectification.*

---

## Entrée n°2 — 2026-07-16 — Phase 0 : mesure finale (`create` à chaud, base brotli)

**Cette entrée rectifie et supersède les chiffres de tête de l'entrée n°1. Elle ne l'efface pas.**
L'entrée n°1 reste le registre de ce qui a été mesuré le matin du 2026-07-16 ; ce qu'elle contient
demeure vrai *en tant que mesure*. Ce qui change ici, ce sont **les chiffres qui font foi pour C2 et
C4**, et une réserve de l'entrée n°1 (« mesurer `create` hors coût de boot ») est désormais **levée
par la mesure directe** au lieu d'être estimée par soustraction.

Runs : 2026-07-16, 13:11–13:23 UTC. JSON bruts : `bench/results/final-warm/<label>.{br,gzip}.json`
(8 fichiers, 10 runs chacun). Les 5 JSON de l'entrée n°1 dans `bench/results/` sont **intacts**.

### Pourquoi une seconde entrée : les deux décisions du propriétaire au gate Phase 0

Ces deux décisions sont **liantes** et sont reproduites verbatim, parce qu'elles expliquent pourquoi
deux rounds existent et lequel fait foi.

> **1. `create` est mesuré à FROID ET à CHAUD ; le CHAUD est le chiffre de tête de C4.**
> Rationale: the cold create number is ~72% Blazor runtime boot, not row-building. Filament boots in
> ~0ms and would "win" create on boot alone, making C4 pass for the wrong reason. Boot-adjusted, the
> real row-building cost is ~9.85ms interpreted / ~9.05ms AOT.

> **2. Le poids est reporté sur les DEUX bases gzip et brotli ; BROTLI est le chiffre de tête du
> ratio 50× de C2.**
> Rationale: real static hosts serve brotli and `dotnet publish` emits .br siblings. Brotli is the
> honest, HARDER target (Blazor is 17.7-31.0% smaller under br). gzip stays in the table because the
> spec states C1's threshold in gzip. Filament must later be held to the SAME basis — comparing
> Filament-gzip against Blazor-brotli (or vice versa) is not a real comparison.

> **3. Les trous de reproductibilité doivent être comblés** (les vérificateurs sceptiques ont jugé la
> baseline non reproductible alors même que les mesures ont survécu à la réfutation).

**Ce qui a changé matériellement entre les deux rounds** (à lire avant toute comparaison entrée n°1
↔ entrée n°2) :

| Élément | Entrée n°1 | Entrée n°2 |
|---|---|---|
| Harness | `harnessVersion` **1.1.0** | **1.2.0** — le chemin chronométré a changé (voir réserve A) |
| Scénarios `create`/`increment` | un seul, à froid | **dédoublés** : `-cold` et `-warm` |
| Base de tête | gzip | **brotli** (gzip conservé et mesuré) |
| Artefacts | publication du matin | **republiés** (csproj harmonisé, `favicon.png` supprimé) |
| Fixture `expected-labels.json` | sha256 `72733c72…` | sha256 `877b1461…` (élargie au 2ᵉ `#run`) |

### Environnement

| Élément | Valeur |
|---|---|
| Machine | Apple M5 Max (Mac17,6), 18 cœurs, 64 Gio de RAM, arm64 |
| OS | macOS 26.5.1 (Darwin 25.5.0) |
| Navigateur | Google Chrome 150.0.7871.124, **headless**, via Playwright 1.61.1 |
| Node | v26.5.0 |
| .NET SDK | 10.0.301 (identique à l'entrée n°1) — `wasm-tools` 10.0.109 |
| Harness | `bench/harness/bench.mjs` **v1.2.0** (≠ entrée n°1) |
| Date des runs | 2026-07-16, 13:11–13:23 UTC |

**Charge machine (divulgation honnête — pire que l'entrée n°1).** La machine n'était **pas quiescée**,
et davantage contaminée sur le papier que le run que l'entrée n°1 signalait déjà comme non quiescé :

- Deux processus `koine-mcp` emballés (PID 33344 ~100 % CPU, PID 33355 ~98 % CPU), chacun épinglant un
  cœur entier depuis 4 h+, parentés par `/Applications/Claude.app`. PID 33355 avait atteint ~24 Go de
  RSS (~27 Go cumulés) en fin de run. Ces processus **fuient/tournent à vide**, ils ne servaient pas
  la mesure.
- `OrbStack Helper` 21–48 % CPU ; `logioptionsplus_updater` 9–47 % CPU.
- E/S disque de fond soutenues ~16–19 k tps / ~65–75 Mo/s ; swap 5,5 Go sur 7,2 Go utilisés.
- CPU inactif 82–85 % ; load average 3,8–7,2. Soit ~2,5 cœurs occupés sur 18 (~14 % de la capacité,
  contre ~2,3 % à l'entrée n°1).

Rien n'a été tué : tout appartient à l'utilisateur. **Pourquoi les chiffres tiennent quand même —
argument empirique, pas excuse** : l'IQR arbitre, et la contamination ne mord pas. `rows-nojit/create-cold`
— le chiffre que l'entrée n°1 désignait comme le plus mou de la matrice (réserve n°12) — passe d'un
IQR de **5,55 ms (16 % de la médiane)** à **0,675 ms (2,0 %)**, soit un **resserrement d'environ 8×**
malgré ~6× plus de bruit CPU nominal. Mécanisme : 18 cœurs, et les fautifs sont 2 threads épinglés qui
tournent en boucle ; l'ordonnanceur macOS a gardé le thread principal mono-thread de Chrome headless
sur des cœurs performance libres. Le load average compte les threads exécutables, pas la contention
réellement subie par Chrome.

**Réserve non maquillée** : une machine réellement inactive reste préférable, et les ~27 Go de RSS
emballé poussant le swap sont un risque latent (un défaut de page dans une fenêtre chronométrée est
exactement ce qui gonfle l'IQR). Les IQR disent que ce n'est pas arrivé ici. Un re-run sur machine
quiescée coûterait ~12 min et lèverait la dernière réserve. **Recommandation indépendante du
benchmark** : l'utilisateur devrait investiguer/redémarrer les `koine-mcp` emballés — c'est un vrai
problème en soi.

### Protocole

Identique à l'entrée n°1 sur les points structurants (Release, cache froid, médiane+IQR, `n = 10`,
`msToMutation`, timeout = échec), avec ces différences :

1. **Intégrité du harness vérifiée AVANT de mesurer** : `node selftest.mjs` ⇒ **440 passed, 0 failed**
   (l'entrée n°1 en annonçait 249 ; le harness a grossi). Point décisif : le selftest pilote une
   fixture **synthétique à boot de 400 ms délibérément injecté** et confirme `cold = 461,4 ms` contre
   `warm = 61,3 ms` (delta **400,1 ms**). **L'horloge à chaud est donc prouvée contre une vérité
   terrain connue**, et non supposée. Reproduit indépendamment par un vérificateur : 440/0, delta
   400,1 ms.
2. **Cache froid imposé de TROIS façons indépendantes par itération** : `BrowserContext` neuf, CDP
   `Network.setCacheDisabled`, et `Cache-Control: no-store` serveur. Zéro avertissement de violation.
3. **Poids** = somme des `encodedDataLength` (CDP `Network.loadingFinished`) — octets sur le fil, ni
   taille disque ni décompressé. 3 runs de poids.
4. **8 configs × 10 runs = 280/280 itérations chronométrées**, 0 échec, 0 timeout, 0 avertissement.
5. **Configs jouées STRICTEMENT en séquence** (horodatages vérifiés : 13:11:08→13:12:01,
   13:12:21→13:13:14, … 13:21:14→13:23:18 — aucun chevauchement).
6. **Service workers bloqués** ; `untrackedRequests` vide dans les 8 runs.
7. **AOT vérifié depuis l'artefact, pas depuis le drapeau** (dette n°10 de `DECISIONS.md` : **payée**).
   Le harness inspecte les artefacts servis et enregistre `aotObserved` ; déclaré == observé dans les
   8 runs. Base : `dotnet-wasm-native-runtime-size`. Re-vérifié à la main sur disque ce jour :

   | Config | `dotnet.native.*.wasm` | Octets |
   |---|---|---:|
   | `blazor-rows-nojit` | `kllr7zg72l` | 1 494 734 |
   | `blazor-counter-nojit` | `kllr7zg72l` | 1 494 734 |
   | `blazor-rows-aot` | `nm0j57lo9u` | **11 380 806** |
   | `blazor-counter-aot` | `xc7yj6pp2h` | **11 362 554** |

   Rapport AOT/non-AOT = **7,60×**. Les empreintes AOT ont **changé** depuis l'entrée n°1
   (`ogsd35n1u1`/`lz2nl4qo4f` → `nm0j57lo9u`/`xc7yj6pp2h`) : les tailles sont identiques, **les octets
   ne le sont pas**. C'est la trace de la republication (voir réserve B).
8. **Encodage réellement honoré, vérifié et non supposé** : `weight.serverEncodings` montre `br` sur
   **39/39** réponses dans les runs brotli et `gzip` sur **39/39** dans les runs gzip. Les chiffres
   gzip sont de **vrais octets gzip**, pas des octets brotli ré-étiquetés.
9. **Garde d'égalité de charge élargie** : la fixture `expected-labels.json` couvre désormais le
   **second `#run`** — celui que `create-warm` chronomètre. `contractCheck` observe
   `secondRunFirstId = "1001"`, `secondRunLastId = "2000"` et un flux de labels **distinct** du
   premier (`« mushy blue mouse »…` contre `« adorable pink desk »…`). Une app qui mettrait ses labels
   en cache après un premier `#run` correct **ne peut plus passer**. `contractCheck.problems == []`
   partout.

#### Commande de rejeu

```bash
./bench/publish-baseline.sh          # les 4 configs (voir réserve D : le chemin AOT est FLAKY)

# Base brotli (TÊTE) et base gzip, par config. Exemple pour rows-nojit :
node bench/harness/bench.mjs --dir bench/publish/blazor-rows-nojit/wwwroot \
  --app rows --label blazor-rows-nojit --runs 10 --weight-runs 3 \
  --max-encoding br   --headless --no-aot --out bench/results/final-warm/blazor-rows-nojit.br.json
node bench/harness/bench.mjs --dir bench/publish/blazor-rows-nojit/wwwroot \
  --app rows --label blazor-rows-nojit --runs 10 --weight-runs 3 \
  --max-encoding gzip --headless --no-aot --out bench/results/final-warm/blazor-rows-nojit.gzip.json
# idem pour blazor-rows-aot (--aot), blazor-counter-nojit / blazor-counter-aot (--app counter)
```

> ⚠️ **Ce rejeu ne fonctionne pas depuis un `git clone` de HEAD** : le harness qui a produit ces
> chiffres n'est **pas commité**. Voir réserve C — c'est bloquant pour la décision n°3.

---

## Résultats — Phase 0, mesure finale

### Poids transféré (octets sur le fil, cache froid, médiane de 3 runs de poids)

**Base de tête = brotli** (décision n°2 du gate). gzip conservé : c'est la base dans laquelle C1 est
exprimé.

| Config | App | AOT | **brotli (o)** | **Kio br** | gzip (o) | Kio gzip | Gain br | Requêtes |
|---|---|---|---:|---:|---:|---:|---:|---:|
| `blazor-counter-nojit` | Counter | non | **1 551 670** | 1 515,3 | 1 885 613 | 1 841,4 | −17,71 % | 39 |
| `blazor-counter-aot`   | Counter | oui | **3 353 458** | 3 274,9 | 4 849 976 | 4 736,3 | −30,86 % | 39 |
| `blazor-rows-nojit`    | Rows    | non | **1 553 388** | 1 517,0 | 1 888 029 | 1 843,8 | −17,72 % | 39 |
| `blazor-rows-aot`      | Rows    | oui | **3 350 819** | 3 272,3 | 4 857 650 | 4 743,8 | −31,02 % | 39 |

- **Poids parfaitement déterministe** : les 3 échantillons de poids sont **identiques à l'octet** dans
  chacun des 8 runs (IQR = 0 partout).
- **Rows fait maintenant 39 requêtes et non 40** : `favicon.png` (1 148 o) a été supprimé et le shell
  utilise `<link rel="icon" href="data:,">`. La réserve n°9 de l'entrée n°1 est **levée** : les deux
  apps servent désormais un shell identique à `<title>` près.
- **La fourchette de gain brotli de l'entrée n°1 (−17,71 % à −31,00 %) est reproduite exactement**
  (−17,71 % à −31,02 %) depuis une mesure fraîche et cette fois **valide au protocole**. L'entrée n°1
  notait que ses temps brotli étaient à `--runs 1` ; **son poids brotli, lui, était déjà à
  `--weight-runs 3`** — donc ce n'est pas une mesure invalide qui est corrigée, c'est une mesure
  **re-prise sur les artefacts republiés** (écart ~1,2 ko, cohérent avec la suppression du favicon).
- **L'AOT alourdit toujours** : ×2,16 en brotli (3 350 819 / 1 553 388). C2 et C4 tirent en sens
  opposés — inchangé depuis l'entrée n°1.

### Temps — Rows (`msToMutation`, médiane et IQR, n = 10, base brotli)

| Scénario | Froid/chaud | Tête | non-AOT méd. | IQR | AOT méd. | IQR | Gain AOT |
|---|---|---|---:|---:|---:|---:|---:|
| `create-cold` (1000 lignes) | froid | non | 34,15 ms | 0,675 | 23,90 ms | 2,35 | 1,43× |
| **`create-warm`** (1000 lignes) | chaud | **OUI** | **13,70 ms** | 0,25 | **7,35 ms** | 0,275 | **1,86×** |
| `update` (1 ligne / 10) | chaud | oui | 12,60 ms | 0,175 | 3,60 ms | 0,175 | 3,50× |
| `swap` | chaud | oui | 12,65 ms | 0,175 | 3,45 ms | 0,275 | 3,67× |
| `clear` | chaud | oui | 4,20 ms | 0,4 | 2,90 ms | 0,3 | 1,45× |

### Temps — Counter (`msToMutation`, médiane et IQR, n = 10, base brotli)

| Scénario | Froid/chaud | Tête | non-AOT méd. | IQR | AOT méd. | IQR | Gain AOT |
|---|---|---|---:|---:|---:|---:|---:|
| `increment-cold` | froid | non | 17,15 ms | 1,2 | 15,85 ms | 0,575 | 1,08× |
| **`increment-warm`** | chaud | **OUI** | **1,30 ms** | 0,175 | **1,00 ms** | 0,075 | 1,30× |

### Contrôle d'encodage : les temps sont-ils indépendants de l'encodage ?

Les temps sont pris après chargement et stabilisation : ils **ne doivent pas** dépendre de l'encodage
de transfert. Deux runs expédiant 1,55 Mo contre 1,89 Mo sur le fil doivent donner la même médiane.

**Constat honnête, corrigé par rapport au rapport de mesure amont** : il y a **10** paires de
scénarios chauds (2 `increment-warm` + 8 Rows chauds), pas 7. **9 sur 10 concordent à ≤ 0,10 ms**,
dont 5 **à la médiane exacte** (`counter-nojit/increment-warm` 1,3/1,3 ; `counter-aot/increment-warm`
1,0/1,0 ; `rows-nojit/create-warm` 13,7/13,7 ; `rows-nojit/swap` 12,65/12,65 ; `rows-aot/clear`
2,9/2,9). **La 10ᵉ ne concorde pas à ≤ 0,10 ms** : `rows-nojit/update` = 12,60 br contre 12,90 gzip,
soit **0,30 ms (2,4 %)**. Écart maximal toutes catégories : **1,05 ms (3,1 %)** sur
`rows-nojit/create-cold` — un scénario **froid**, seul endroit où un terme de boot ajoute
légitimement de la variance.

> **Rectification d'un chiffre du rapport amont.** Le rapport de mesure affirmait « Every WARM scenario
> (all 7) agreed within <= 0.10 ms » sous un titre « PASS, strongly ». C'est **faux sur deux points** :
> il y a 10 paires chaudes et non 7, et `rows-nojit/update` s'écarte de 0,30 ms. Le rapport énonce
> lui-même ce 0,30 ms dans la note du scénario concerné : **son résumé contredit ses propres données**,
> et l'erreur va dans le sens flatteur. La conclusion de fond tient (le harness mesure du rendu, pas du
> téléchargement), mais elle tient à **9/10 à ≤ 0,10 ms**, pas à 10/10.

### Recoupements de validité

| Contrôle | Résultat |
|---|---|
| CDP contre grand livre serveur | CDP > corps serveur dans les 8 runs, de 10 501–10 584 o sur 39 requêtes = **269,3–271,4 o/requête** = en-têtes de réponse. Aucun delta négatif (aucun sous-comptage), aucun 404. |
| Déterminisme du poids | 3/3 échantillons identiques à l'octet, 8/8 runs. |
| `create-warm` ≪ `create-cold` | Vrai dans les 4 configs (voir analyse boot). L'horloge à chaud n'est pas cassée. |
| Contrat DOM | `problems == []` dans les 8 runs ; 1000 lignes, 2 cellules/ligne, swap 2↔999, `updateMissedCount = 0`, `clear → 0`. |
| Anti-fabrication | Le `#run` chronométré de `create-warm` produit un flux de labels **distinct** du premier (ids 1001–2000). Un cache de labels est refusé. |

---

## Analyse du boot : combien de `create` est-ce du rendu ?

C'est le cœur de la décision n°1 du gate, et **le point où cette entrée corrige à la fois l'entrée n°1
et le rapport de mesure amont.**

### Ce que la mesure directe dit (base brotli)

| Config | `create-cold` | `create-warm` | cold − warm | Proxy de boot (`increment-cold` − `increment-warm`) | Résidu |
|---|---:|---:|---:|---:|---:|
| `rows-nojit` | 34,15 | **13,70** | 20,45 (59,9 % du froid) | 15,85 (**46,4 %** du froid) | **4,60** |
| `rows-aot` | 23,90 | **7,35** | 16,55 (69,2 % du froid) | 14,85 (**62,1 %** du froid) | **1,70** |

**Lecture.** `cold − warm` **n'est pas** « le boot ». Le proxy de boot indépendant (mesuré sur Counter,
dont le payload ne diffère que de 0,2 %) vaut 15,85 ms (non-AOT) / 14,85 ms (AOT). Le **résidu** —
4,60 ms non-AOT, 1,70 ms AOT — est du **réchauffement de chemin de code au premier appel**
(étagement de l'interpréteur), pas du boot. Le selftest le dit explicitement : une fixture **sans
aucun boot injecté** montre quand même `create-cold` nettement au-dessus de `create-warm`.

> **Rectification d'un chiffre du rapport amont.** Le rapport annonce « `~20.45 ms (60%) is runtime
> boot » et « cold create is ~60-72% boot ». C'est une **mauvaise étiquette** : 20,45 ms est
> `cold − warm`, pas le boot. Le boot mesuré indépendamment vaut **46,4 %** de `rows-nojit/create-cold`
> (et non 60 %). Environ **4,6 ms de ce qui est appelé « boot » est en réalité du réchauffement de
> premier appel.** La conclusion qualitative du gate — le `create` froid est majoritairement autre
> chose que de la construction de lignes — **tient** ; le pourcentage exact non.

### La décision n°1 du gate est vindiquée sur le fond, son estimation arithmétique est superseded

La décision n°1 chiffrait la construction de lignes hors boot à **~9,85 ms interprété / ~9,05 ms AOT**
et concluait implicitement que l'AOT et l'interprété sont quasi à égalité sur le rendu (9,85/9,05 =
**1,09×**). **La mesure directe dit autre chose :**

| | Estimation du gate | **Mesure directe (`create-warm`)** | Écart |
|---|---:|---:|---|
| Interprété | ~9,85 ms | **13,70 ms** | l'estimation était **28 % trop basse** |
| AOT | ~9,05 ms | **7,35 ms** | l'estimation était **23 % trop haute** |
| Ratio AOT | 1,09× | **1,86×** | l'AOT est **bien plus** en avance qu'estimé |

**La conséquence est concrète : C4 doit être jugé contre 13,70 ms (interprété) / 7,35 ms (AOT), et la
cible AOT est nettement plus dure que le gate ne le supposait.** La rationale de la décision n°1
(« mesurer le chaud directement ») est **validée** ; seule son arithmétique est superseded.

### Mais le diagnostic du rapport amont sur la CAUSE est faux, et c'est important

Le rapport affirme que la soustraction « n'a pas seulement ajouté du bruit, elle a **inversé** la
conclusion », parce qu'elle « soustrait un terme de boot lui-même partiellement recouvert avec le
travail sur les lignes ». **Cette explication ne résiste pas aux données de ce run.** Refaite avec les
entrées **du même round** (base gzip, pour rester homogène avec l'entrée n°1) :

| Config | `create-cold` − `increment-cold` | `create-warm` mesuré | Écart de la méthode |
|---|---:|---:|---:|
| `rows-aot` | 23,65 − 16,10 = **7,55** | **7,45** | **0,10 ms** |
| `rows-nojit` | 33,10 − 17,10 = **16,00** | **13,70** | 2,30 ms (sens attendu : étagement de l'interpréteur) |

**La méthode de soustraction reproduit le chiffre chaud AOT à 0,10 ms près.** Elle n'est donc **pas
structurellement cassée**. Ce qui a bougé, ce n'est pas la méthode, **c'est son entrée** — voir la
réserve A, qui est la vraie découverte de ce round et que le rapport amont n'a pas faite.

---

## Ce que C1, C2 et C4 exigent désormais de Filament — chiffres fermes

### C1 — plafond absolu de bundle : **< 10 ko gzip**

L'entrée n°1 consignait C1 comme **CHIFFRE MANQUANT** (spec absente du disque). **La valeur est
désormais fixée par le propriétaire au gate Phase 0 : `< 10 ko gzip`.**

> **Provenance à ne pas oublier** : la spec n'est **toujours pas sur le disque** (recherche refaite ce
> jour : aucun fichier de spec, aucun cahier des charges ; `grep` de « 10 ko » dans le dépôt : zéro
> occurrence). C1 est donc consigné **sur l'autorité du propriétaire**, pas depuis un document
> vérifiable. Si la spec réapparaît et dit autre chose, **c'est la spec qui gagne** et une entrée n°3
> devra le consigner.

### C2 — battre la baseline Blazor d'un facteur 50 sur le poids

La cible **dure** est le **plus petit** bundle Blazor : la config **non-AOT**.

**Base de tête = brotli (décision n°2 du gate). Filament doit rester à ou sous :**

| Cible C2 | Baseline Blazor | **Cible brotli (TÊTE)** | Cible gzip (secondaire) |
|---|---:|---:|---:|
| **contre `blazor-rows-nojit`** | 1 553 388 o br | **31 068 o = 30,34 Kio** | 37 761 o = 36,88 Kio |
| **contre `blazor-rows-aot`** | 3 350 819 o br | 67 016 o = 65,45 Kio | 97 153 o = 94,88 Kio |

*(Les cibles contre l'AOT sont données pour référence et **ne doivent jamais servir de titre** :
elles sont 2,16× plus permissives.)*

### **C1 est ~3× plus strict que C2 — c'est donc C1 qui contraint le poids**

C'est le point le plus opérationnel de cette entrée :

| Comparaison | Calcul | Résultat |
|---|---|---:|
| C1 contre C2, **même base gzip** | 37 761 / 10 000 | **C1 est 3,78× plus strict** |
| C1 contre C2, base de tête brotli | 31 068 / 10 000 | **C1 est 3,11× plus strict** |

**Conséquence, énoncée sans ambiguïté : C1 est le verrou de poids qui contraint réellement, et C2 passe
automatiquement si C1 passe.** L'argument est solide et ne dépend d'aucun transfert de ratio : pour un
même contenu, brotli est en pratique **toujours ≤** gzip. Donc un artefact Filament à ≤ 10 000 o gzip
pèse ≤ 10 000 o en brotli, ce qui est **très en dessous** des 31 068 o exigés par C2 en brotli — avec
au moins **3,1× de marge**. *(Que l'on lise « 10 ko » comme 10 000 o ou 10 240 o ne change rien :
3,03× à 3,11× de marge en brotli, 3,69× à 3,78× en gzip.)*

**Ce que cela implique pour la suite** : annoncer « Filament bat Blazor par 50× » sera **le résultat le
moins exigeant** que Filament aura à produire sur le poids. Le vrai test de poids est C1.

### C4 — médianes par scénario à ne pas dépasser

La cible **dure** est le **run le plus rapide** de Blazor : la config **AOT**. Base brotli, `n = 10`.

| Scénario | **Filament ne doit pas dépasser (AOT — cible dure)** | *(non-AOT, pour information)* |
|---|---:|---:|
| **`create-warm`** (1000 lignes) — **le chiffre de tête de C4** | **7,35 ms** | *13,70 ms* |
| `update` (1 ligne / 10) | **3,60 ms** | *12,60 ms* |
| `swap` | **3,45 ms** | *12,65 ms* |
| `clear` | **2,90 ms** | *4,20 ms* |
| `increment-warm` (Counter) | **1,00 ms** | *1,30 ms* |
| *`create-cold` (contexte, jamais un titre de C4)* | *23,90 ms* | *34,15 ms* |

### ⚠️ Avertissement anti-cherry-picking : C2 et C4 nomment des configs DIFFÉRENTES, et c'est délibéré

- **Le poids se juge contre `nojit`** (1 553 388 o br) : c'est le **plus petit** bundle Blazor.
- **La vitesse se juge contre `aot`** (`create-warm` 7,35 ms) : c'est le **run le plus rapide** de Blazor.

**Revendiquer un gain de poids de 50× contre la config AOT (cible molle : 67 016 o, 2,16× plus
permissive) tout en comparant la vitesse à la config non-AOT (cible molle : 13,70 ms, 1,86× plus
permissive) reviendrait à choisir la moitié facile de chaque critère.** Ce serait une double
tricherie, chacune invisible prise isolément. Filament affronte la **meilleure moitié de chaque**,
jamais un homme de paille. **Et Filament doit être mesuré sous le MÊME `--max-encoding` que la
baseline qu'il affronte** : un Filament brotli contre un Blazor gzip n'est pas un ratio de 50×, c'est
un artefact d'encodage.

### Échecs

**Aucun échec de scénario, aucun timeout, aucun nombre fabriqué, aucune config en échec.**
8 configs × 10 runs = **280/280 itérations chronométrées retenues**, 0 échec, 0 timeout, 0 prédicat en
échec, 0 avertissement du harness, 0 problème de contrat, 0 requête non tracée, 0 violation de cache
froid. Toutes les valeurs ci-dessus sont des médianes/IQR mesurées ; aucune n'est estimée ou
interpolée.

**Un échec de publication est consigné** (il ne s'agit pas d'un échec de mesure, mais il conditionne le
rejeu) : voir réserve D — le chemin AOT de `publish-baseline.sh` échoue ~50 % du temps au premier essai.

---

## Réserves ouvertes de l'entrée n°2

Remontées par deux vérificateurs sceptiques indépendants. **Les deux ont conclu `trustworthy: false`
— non pas sur les mesures, qui ont survécu à toutes leurs tentatives de réfutation, mais sur la
couche d'interprétation et sur la reproductibilité.** Elles sont listées, pas enterrées.

### A. **Irréproductibilité de 33 % entre l'entrée n°1 et l'entrée n°2 — la vraie découverte de ce round**

C'est la réserve la plus importante, et le rapport de mesure amont **ne l'a pas vue**.

`blazor-counter-nojit`, scénario `increment`, **même machine, même Chrome, même SDK, même base gzip,
même `n = 10`** :

| | Médiane | Échantillons |
|---|---:|---|
| **Entrée n°1** (harness 1.1.0) | **25,55 ms** | `[27, 25.4, 24.5, 25.7, 27, 21.4, 26.1, 24.2, 26, 24.7]` |
| **Entrée n°2** (harness 1.2.0) | **17,10 ms** | `[17.3, 17.8, 13.1, 16.1, 17.5, 16.9, 16.9, 17.9, 18, 16.7]` |

**Les deux ensembles d'échantillons ne se recouvrent pas du tout** (n°1 : 21,4–27,0 ; n°2 : 13,1–18,0).
Écart de **8,45 ms, soit 33 %**. Ce n'est pas du bruit.

**Cause la plus probable : le harness lui-même.** `harnessVersion` est passé de **1.1.0 à 1.2.0**, et
`bench.mjs` documente que le code précédent « discarded the settle result entirely at both call
sites », de sorte que les itérations 1.1.0 chronométraient un clic sur un réseau non stabilisé, « racing
in-flight download and decode work, inflating that sample ». **Autrement dit : c'est l'entrée n°1 qui
était gonflée, et l'entrée n°2 qui est correcte** — ce qui est cohérent avec le sens de l'écart. Mais
**cela reste une hypothèse non testée**, et c'est exactement pourquoi c'est consigné comme réserve.

**Conséquence directe sur la « réfutation » de la décision n°1** : l'estimation ~9,85/~9,05 du gate
n'était pas le produit d'une méthode cassée, c'était `35,40 − 25,55` et `23,45 − 14,40` **avec l'entrée
n°1 comme entrée**. Puisque `25,55` ne se reproduit pas, l'estimation ne pouvait pas se reproduire. La
méthode, elle, reproduit le chaud AOT à **0,10 ms** avec les entrées du même round.

> **Le rapport de mesure amont affirme que « results remain comparable to BENCH.md entry #1 » en
> s'appuyant sur la seule identité du SDK.** C'est **faux** : le chemin chronométré du harness a changé
> (1.1.0 → 1.2.0), les artefacts ont été republiés, et un scénario de l'entrée n°1 a bougé de 33 % sans
> le moindre recouvrement d'échantillons. **Les deux entrées ne sont pas directement comparables.**

### B. Les artefacts ont été republiés — « byte-exact » est le mauvais mot

Le rapport amont affirme que les runtimes sont « **byte-exact** against the four verifier agents'
reports (11 362 554 / 11 380 806 / 1 494 734 / 1 494 734) ». **Ce sont des TAILLES, pas des octets.**
Les empreintes de contenu ont changé (`ogsd35n1u1` → `nm0j57lo9u`, `lz2nl4qo4f` → `xc7yj6pp2h`) :
**taille identique, contenu différent**. C'est précisément la preuve de la republication que le rapport
ne mentionne pas. Sans conséquence sur les conclusions ; consigné parce que le mot figurait dans un
rapport titré « NO fabricated numbers ».

### C. **BLOQUANT pour la décision n°3 — les preuves et l'instrument ne sont PAS versionnés**

La décision n°3 du gate exige que les trous de reproductibilité soient **comblés**. **Ils ne le sont
pas.** Vérifié sur le dépôt ce jour :

1. **Les 8 JSON de tête sont GITIGNORÉS.** `git ls-files bench/results/final-warm/` ⇒ **0 fichier**.
   `git check-ignore -v` ⇒ `.gitignore:18:bench/results/*`. La négation `!bench/results/*.json` ne
   rattrape **pas** un sous-répertoire : git ne descend jamais dans un répertoire exclu. Le commentaire
   du `.gitignore` proclame que les résultats sont « DELIBERATELY NOT ignored » — **les fichiers
   réellement rapportés ici sont ignorés**. Le trou signalé au round précédent est **comblé pour les 5
   anciens JSON et rouvert pour les 8 nouveaux.** → Correctif : `!bench/results/**/*.json`, puis
   commiter les 8 fichiers.
2. **Le harness qui a produit ces chiffres n'est pas commité** : `bench.mjs`, `selftest.mjs`,
   `server.mjs`, `expected-labels.json` sont tous **modifiés et non commités**. Un `git clone` de HEAD
   donne un code **matériellement différent** : `grep -c secondRun` sur le `bench.mjs` de HEAD ⇒ **0**.
   **La garde anti-fabrication sur laquelle repose le contrôle d'égalité de charge de cette entrée
   n'existe pas à HEAD.** Le selftest à 440 assertions n'existe que dans le répertoire de travail ;
   HEAD est à 249.
3. **`README.md` publie l'estimation superseded comme un fait** : il affirme toujours « Boot-adjusted,
   real row building is ~9.85 ms interpreted / ~9.05 ms AOT » (mesuré : 13,70 / 7,35 ; AOT 1,86× et non
   1,09×), annonce « 249 assertions » (réel : 440) et documente `--out bench/results/<label>.json`, pas
   la matrice gzip/brotli de `final-warm/`. **Un inconnu qui suit le README rejoue l'ANCIEN protocole
   et cite un chiffre que l'équipe sait déjà faux.**
4. **La fixture citée dans `DECISIONS.md` n°5 est périmée** : le journal cite le sha256 `72733c72…`, la
   fixture sur le disque et dans les 8 résultats est `877b1461…` (élargissement légitime au 2ᵉ `#run`,
   mais le journal n'a pas suivi).

### D. **`bench/publish-baseline.sh` : chemin AOT flaky et non sûr en parallèle — hérité, non corrigé**

Quatre agents vérificateurs indépendants le rapportent, et c'est confirmé structurellement :

- **Non sûr en concurrence** : `blazor-rows-nojit` et `blazor-rows-aot` partagent **un seul arbre
  source** (`baseline/Rows.Blazor`), donc `obj/` et `bin/` ; idem pour les deux Counter. Le **premier
  acte** du script pour chaque config est `rm -rf "$project_dir/obj" "$project_dir/bin"`. Lancer les
  configs appariées en parallèle permet à un run de purger `obj/` sous un build en vol. Ce round n'a
  survécu que **par chance d'ordonnancement**. → Sérialiser `rows-nojit → rows-aot` et
  `counter-nojit → counter-aot` (le parallélisme entre les deux arbres est sûr).
- **Flaky au premier essai** : `MSB3030`/`MSB3073` sur **~50 %** des publications AOT propres. Preuve
  de cause racine : les assets compressés **sont** produits, mais sous un hash différent de celui que
  l'étape de copie attend (attendu `…-zoy55ga6o0-zoy55ga6o0.br`, présent `…-w0tmafab2t-w0tmafab2t.br`)
  — une course **à l'intérieur d'un seul build propre**, pas un cache périmé. Une publication non-AOT du
  même arbre réussit proprement : **l'AOT est requis pour déclencher le bug**.
- **`DECISIONS.md` n°9 attribue cette panne à un « cache périmé » : c'est une MAUVAISE ATTRIBUTION**,
  et le message d'erreur du script lui-même (« the obj/ purge above did not cover a stale cache — see
  DECISIONS.md #9 ») **enverra le prochain opérateur sur une fausse piste**. Le script venait de faire
  `rm -rf obj bin` quelques microsecondes plus tôt.
- **Le chemin AOT du script n'a jamais été démontré de bout en bout** : le message de commit délimite
  lui-même sa vérification à « publish-baseline.sh runs both **non-AOT** configs and is idempotent ».
  Le chemin qui produit le chiffre de tête AOT de C4 (7,35 ms) est celui que le script n'a pas prouvé
  survivre.
- → Correctifs : boucle de réessai bornée autour du `dotnet publish`, correction du texte d'aide, et
  sérialisation des configs partageant un arbre.

### E. Défauts du harness relevés par l'audit (3 majeurs, 3 mineurs) — non corrigés

Aucun n'invalide la baseline Blazor mesurée ici (`RowsApp.razor` appelle bien `NextLabel()` par ligne
et par `#run`), **mais trois retirent une garantie que le harness proclame, et ce sur le chiffre même
qui décidera de Filament** :

1. **MAJEUR — la garde d'équité ne couvre pas le `#run` CHRONOMÉTRÉ de `create-warm`** (`bench.mjs`).
   `verifyContract` charge une page fraîche et clique `#run` **une fois** ; `create-warm` chronomètre
   le **second** `#run`, dont les labels viennent des tirages LCG 3001..6000. Le clic chronométré n'est
   gardé que par `rowCount === 1000`. **Triche concrète qui passe toutes les gardes actuelles** : mettre
   les labels en cache après le premier `#run` — le premier run passe la fixture à l'octet, le second
   (le chiffre de tête) ne fait **aucune** des 3 000 opérations multiply/modulo ni des 1 000
   concaténations. *(Note : `contractCheck` observe bien `secondRunFirstId`/`secondRunLastId` et un flux
   distinct, ce qui ferme ce trou en pratique pour ce run ; l'audit signale que le **prédicat du clic
   chronométré** lui-même reste `rowCount === 1000`.)*
2. **MAJEUR — le « settle beat » n'est appliqué qu'aux deux nouveaux scénarios chauds**, pas à
   `update`/`swap`/`clear`, alors que la prose du changement affirme leur équivalence. `create-warm`
   bénéficie de 2 rAF + un macrotask ≥ 50 ms de protection ; `update`/`swap`/`clear` n'ont qu'~1 frame.
   **Les quatre scénarios chauds sont donc mesurés sous des régimes de stabilisation différents** —
   ce qui contredit directement l'affirmation « it is consistent with update/swap/clear ».
3. **MAJEUR — `classifyAotEvidence` peut poser `verified: true` depuis un artefact JAMAIS SERVI.**
   Le contrôle `servedInWeightRun` est une **préférence**, pas une exigence, et aucun avertissement
   n'est émis quand la preuve retenue n'est pas passée sur le fil. *(Sans effet ici : les artefacts
   servis sont bien ceux mesurés, re-vérifiés à la main sur disque ci-dessus.)*
4. **MINEUR** — `create-warm` n'asserte jamais l'état préalable dont il dépend (`#tbody` vide) : la
   garde anti-vacuité n'exige que `rowCount !== 1000`, ce que 500 ou 999 satisfont.
5. **MINEUR** — le harness facture au framework mesuré le coût d'évaluation de son propre prédicat
   (110 lookups DOM pour `update`), et **ce coût croît avec le nombre de callbacks** que le framework
   produit : un framework qui découpe son rendu en N tâches paie N × 110 lookups **dans sa propre
   fenêtre chronométrée**, uniquement pour être ordonnancé autrement. C'est un coût de harness rapporté
   comme coût de framework — la même classe de défaut que `msToPaint`, mais **non divulguée**.
6. **MINEUR** — `diagnostics.transferredBytesToInteractive` n'est pas mesuré « to interactive » : le
   snapshot est pris **après** toute l'itération. Le nom asserte une frontière que le code n'implémente
   pas.

### F. Réserves de l'entrée n°1 : ce qui est levé, ce qui reste

| Réserve n°1 | Statut |
|---|---|
| n°1 — `create` majoritairement du coût de première interaction | **LEVÉE par mesure directe** : `create-warm` mesure le rendu. |
| n°2 — le gain AOT de 1,51× n'est pas une accélération de rendu | **RECTIFIÉE** : hors boot, l'AOT est bien **1,86×** plus rapide au rendu — le gain est **réel**, l'entrée n°1 le sous-estimait (elle l'estimait à 1,09×). |
| n°3 — commande de publication nulle part sur le disque | **LEVÉE** : `bench/publish-baseline.sh` existe et est commité. |
| n°4 — zéro commit git ; `bench/results/` ignoré | **PARTIELLE** : 1 commit existe ; **les 8 JSON de tête restent ignorés** (réserve C). |
| n°7 — `--aot` auto-déclaré, jamais vérifié | **LEVÉE** : le harness vérifie depuis l'artefact servi (`aotObserved`), avec la nuance E-3. |
| n°8 — les deux apps pas configurées identiquement | **LEVÉE** : les deux `.csproj` sont **identiques à l'octet** (`diff` vide), `PublishTrimmed` et `InvariantGlobalization` explicites des deux côtés. |
| n°9 — les deux apps ne servent pas le même shell | **LEVÉE** : `favicon.png` supprimé ; shells identiques à `<title>` près ; 39 requêtes partout. |
| n°12 — `rows-nojit/create` est le chiffre le plus mou | **LEVÉE** : IQR 5,55 ms → **0,675 ms** (2,0 % de la médiane). |
| n°6 — trou d'équité sur le balisage des lignes | **NON LEVÉE.** Le contrat n'exige toujours que `cellsPerRow >= 2` ; Blazor émet 4 éléments/ligne, Filament pourrait n'en émettre que 3. **À épingler avant de comparer `create-warm`.** |
| n°11 — baseline « Blazor par défaut », pas « Blazor minimal » | **NON LEVÉE** (`System.Text.Json` ~8 % du poids, jamais utilisé par un compteur). |
| n°13 — `msToPaint` ne compare jamais des frameworks | **NON LEVÉE** (toujours vraie ; toutes les valeurs de tête ci-dessus sont des `msToMutation`). |
| n°15 — `n = 10`, une machine, un Chrome, un OS | **NON LEVÉE.** Suffisant pour un POC ; insuffisant pour une revendication publiable. |
| n°16 — `--warmup 1` structurellement inopérant | **PARTIELLEMENT ADRESSÉE** : les scénarios `-warm` réchauffent désormais **dans la page**. |

### G. Réserve inhérente au chaud, à ne pas oublier quand Filament sera mesuré

Le `#run` chronométré de `create-warm` suit un cycle `#run` + `#clear` : il bénéficie d'un tas GC déjà
grandi/recyclé et d'un allocateur réchauffé **qu'un utilisateur en premier chargement n'a jamais**.
`create-warm` est donc une **borne basse** du coût de construction de lignes, pas l'expérience réelle
d'un premier visiteur — c'est le prix assumé pour mesurer du rendu plutôt que du boot, et c'est
exactement ce que la décision n°1 demande. **Le harness impose la séquence identique à tout framework**
(`assertSetupMatchesSpec`), donc la comparaison reste équitable — **mais Filament devra être poussé par
le même chemin, sinon les cibles 13,70/7,35 ms ne veulent rien dire.**

---

*Fin de l'entrée n°2. Ne pas modifier — ajouter une entrée n°3 pour toute rectification.*
