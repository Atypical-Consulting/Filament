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

---

## Entrée n°3 — 2026-07-16 — Phase 1 : `filament-runtime` + apps écrites à la main

Établit C1 (poids), C3 (écritures DOM / allocation) et C5 (aucun runtime .NET), et confronte C4
(vitesse) à la baseline Blazor de l'entrée n°2.

> **⚠️ AVERTISSEMENT DE PORTÉE — À LIRE AVANT TOUT CHIFFRE DE CETTE ENTRÉE.**
> **L'artefact mesuré ici est du JavaScript écrit à la main, pas la sortie d'un compilateur.**
> `src/Filament.Generator/`, `src/Filament.Core/` et `src/Filament.Analyzer/` sont des **répertoires
> vides** — vérifié ce jour (`ls -A` ⇒ vide sur les trois). Il n'existe **aucun C#, aucun Razor,
> aucun générateur de source** dans le dépôt hors de `baseline/` (qui appartient à Blazor).
> `samples/Counter/counter.js` le dit dans son propre en-tête : « *This file is the ANSWER KEY.
> Phase 2's generator consumes baseline's Counter.Blazor/App.razor and its emitted JS is
> snapshot-tested against what is written here.* »
>
> **Ce qui est donc démontré** : du JS taillé à la main au-dessus d'un runtime à signaux taillé à
> la main bat Blazor. Solid et Svelte l'ont établi il y a des années ; personne ne le contestait.
> **Ce que le POC a besoin de savoir et que cette entrée ne mesure pas** : qu'un générateur C#
> puisse *émettre* ce JS depuis du Razor en tenant sous 10 ko et à ces temps.
> **Tous les chiffres ci-dessous sont donc des bornes basses optimistes**, pas la performance de
> Filament. Ils sont valides pour ce qu'ils mesurent et ne doivent jamais être cités sans cet
> avertissement. Voir la réserve n°A et la décision n°21 de `DECISIONS.md`.

### Environnement

| Élément | Valeur |
|---|---|
| Machine | Apple M5 Max (Mac17,6), 18 cœurs, 64 Gio, arm64 |
| Alimentation | Secteur (AC), batterie chargée ; `pmset -g therm` : aucun avertissement thermique ni puissance |
| OS | macOS 26.5.1 (build 25F80), Darwin 25.5.0 |
| Navigateur | Google Chrome 150.0.7871.124, **headless**, via Playwright 1.61.1 |
| Node | v26.5.0 |
| .NET SDK | 10.0.301 (baseline Blazor uniquement — Filament n'en consomme aucun) |
| Harness | `bench/harness/bench.mjs`, `HARNESS_VERSION` = `1.2.0` — **mais voir la réserve n°B : cette chaîne ment** |
| Date des runs | 2026-07-16, 16:16:55–16:26:10 UTC, **strictement séquentiels** (horodatages non chevauchants vérifiés) |

**Quiescence (divulgation honnête)** : machine **non totalement quiescée**, mais **matériellement plus
calme que l'entrée n°2**. Load average 1,69 / 2,05 / 2,11 sur 18 cœurs. Principaux consommateurs :
`logioptionsplus_agent` ~34,7 %, `OrbStack Helper` ~29,1 % — ensemble ~0,64 cœur sur 18 (**~3,5 % de la
capacité**), comparable aux ~2,3 % de l'entrée n°1 et loin des ~14 % de l'entrée n°2. **Décisif** : les
processus `koine-mcp` emballés qui contaminaient l'entrée n°2 (2 cœurs épinglés, ~27 Go de RSS, 5,5 Go
de swap) **ont disparu** — le risque latent de défaut de page de cette entrée ne s'applique pas ici.
Rien n'a été tué. Les IQR corroborent l'absence de contention : 0–0,8 ms sur tous les scénarios chauds,
avec `update`/`swap`/`clear` à IQR ≤ 0,1 ms.

#### Commande de rejeu

```bash
# 1. Construire les 4 labels Filament (2 prod + 2 instrumentés "-stats").
#    Parité de compression avec dotnet publish : gzip -9 / brotli -q 11 + BROTLI_PARAM_SIZE_HINT.
bash bench/build-filament.sh

# 2. Mesure C1 + C4 (production, les deux bases d'encodage).
node bench/harness/bench.mjs --dir bench/publish/filament-rows \
  --app rows --label filament-rows --runs 10 --weight-runs 3 \
  --max-encoding brotli --headless --no-aot --out bench/results/phase1/filament-rows.br.json
node bench/harness/bench.mjs --dir bench/publish/filament-counter \
  --app counter --label filament-counter --runs 10 --weight-runs 3 \
  --max-encoding brotli --headless --no-aot --out bench/results/phase1/filament-counter.br.json
# idem avec --max-encoding gzip -> *.gzip.json

# 3. Mesure C3 (sonde DOM + allocation, n=3).
node bench/harness/bench.mjs --dir bench/publish/filament-counter-stats \
  --app counter --label filament-counter-stats --c3 --runs 3 --headless --no-aot \
  --out bench/results/phase1/filament-counter-stats.c3.json
node bench/harness/bench.mjs --dir bench/publish/filament-counter \
  --app counter --label filament-counter-prod --c3 --runs 3 --headless --no-aot \
  --out bench/results/phase1/filament-counter-prod.c3.json
node bench/harness/bench.mjs --dir bench/publish/blazor-counter-aot/wwwroot \
  --app counter --label blazor-counter-aot --c3 --runs 3 --headless --aot \
  --out bench/results/phase1/blazor-counter-aot.c3.json
```

JSON bruts : `bench/results/phase1/{filament-counter,filament-rows}.{br,gzip}.json`,
`bench/results/phase1/{filament-counter-prod,filament-counter-stats,blazor-counter-aot}.c3.json`,
`bench/results/phase1/summary.json`.
Baseline comparée : `bench/results/final-warm/blazor-*-{aot,nojit}.br.json` (entrée n°2).

---

## C1 — Poids transféré (< 10 ko gzip)

Octets **sur le fil** (CDP `encodedDataLength`), cache froid, médiane de 3 runs de poids,
**IQR = 0 dans toutes les configs**.

| App | gzip (o) | brotli (o) | Requêtes | Blazor non-AOT gzip (o) | Rapport |
|---|---:|---:|---:|---:|---:|
| `filament-counter` | **2 864** | 2 494 | 3 | 1 885 505 | **658,3×** |
| `filament-rows` | **4 243** | 3 794 | 3 | 1 889 184 | **445,2×** |

**Verdict C1 : PASS, sous les DEUX lectures de « 10 ko ».**

| Lecture | `filament-counter` | `filament-rows` |
|---|---|---|
| **10 000 o** (décimal, la plus stricte) | PASS — 7 136 o de marge (**3,49× sous**) | PASS — 5 757 o de marge (**2,36× sous**) |
| **10 240 o** (10 Kio binaire) | PASS — 7 376 o de marge (3,58× sous) | PASS — 5 997 o de marge (2,41× sous) |

**L'ambiguïté de la spec est sans objet** : la plus grosse des deux apps passe la lecture **la plus
stricte** avec 2,36× de marge. Aucune interprétation du seuil ne change le verdict.

**Preuves au-delà du nombre** (vérifiées depuis l'artefact, pas depuis la source) :

- **Les bundles de production sont exempts de code de stats.** `grep -c` sur
  `filament-{counter,rows}/app.js` : `filament:stats` = **0**, `__filament` = **0**, `domWrites` = 0,
  `sourceMappingURL` = 0. Les bundles `-stats` retournent 1/2/2/1. **Le DCE a bien tiré**, et cela
  prouve aussi que le run C3 mesure une instrumentation réelle et non un no-op.
- **Parité de compression réelle.** `build-filament.sh` fixe `GZIP_LEVEL=9` / `BROTLI_QUALITY=11` avec
  `BROTLI_PARAM_SIZE_HINT`, à l'identique de `server.mjs`. Filament n'a **pas** été pénalisé par un gzip
  à la volée plus faible. Siblings servis : `serverEncodings.gzip = {responses: 3, bytes: 2030}` et
  1153 + 404 + 473 = 2030 exactement ; CDP 2864 = 2030 + 834 o d'en-têtes. Idem rows (2523+402+484=3409)
  et brotli (1041+265+360=1666).
- **Les siblings `.gz`/`.br` se décompressent à l'octet identique à la source** (sha256, 6/6 fichiers).
- **Aucun travail exigé par le contrat n'a été retiré pour passer sous le seuil** : le contrôle de
  balisage des lignes PASSE (voir C4 et la réserve n°6 levée ci-dessous).

### Poids propre du runtime vs son budget < 2 ko

| Fichier | brut | **gzip** | brotli | vs 2 000 o | vs 2 048 o |
|---|---:|---:|---:|---|---|
| `dist/filament.js` (production) | 4 289 | **1 824** | 1 688 | **PASS** — 176 o de marge (8,8 %) | **PASS** — 224 o de marge |
| `dist/filament.dev.js` (non expédié) | 4 674 | 1 995 | 1 839 | *(5 o sous 2 000)* | *(53 o sous 2 048)* |

**Verdict : PASS sur les deux lectures**, mais **honnêtement : c'est serré sur la lecture décimale** —
176 o, soit 8,8 %. À surveiller : le build dev est à **5 octets** de 2 000 o. Il n'est pas expédié, donc
le budget n'est pas violé, mais toute fonctionnalité ajoutée au runtime consomme une marge mince.

---

## C3 — Exactement 1 écriture DOM par incrément, 0 allocation d'arbre de rendu

Instrument **agnostique du framework** : `MutationObserver` sur la racine **la plus large** (`body`),
même chemin de code pour les deux frameworks.

### Écritures DOM par incrément

| Framework | Observé (MutationObserver, 5 incréments) | Auto-rapport `__filament.stats.domWrites` | Concordance |
|---|---|---|---|
| **Filament** | **[1, 1, 1, 1, 1]** | [1, 1, 1, 1, 1] | **oui — corroboré, pas cru sur parole** |
| **Blazor (AOT)** | **[1, 1, 1, 1, 1]** | s.o. | s.o. |

Nature de l'écriture : `characterData` sur le `#text` dans `<span#counter-value>` ; `childList` = 0,
`attributes` = 0.

> **CONSTAT QUI COUPE CONTRE LA LECTURE FLATTEUSE, ÉNONCÉ EN TÊTE ET NON ENFOUI.**
> **Blazor fait AUSSI exactement 1 écriture DOM par incrément.** Filament **atteint la barre de C3**,
> mais **cette moitié de C3 n'est PAS un différenciateur face à Blazor** sur le compteur : le diff de
> Blazor produit lui aussi une écriture `characterData` minimale. L'avantage de Filament est dans la
> **manière** d'arriver à cette écriture, pas dans leur nombre. Le rapporter comme un avantage serait
> malhonnête dans le sens flatteur.

### Sonde d'allocation

| Config | o/incrément | Ce que le nombre mesure **réellement** |
|---|---:|---|
| `filament-counter-prod` (minifié, sans instrumentation) | 335,2 | **~0 est attribuable à Filament** — voir ci-dessous |
| `filament-counter-stats` (non minifié + sourcemap) | 312,97 | idem |
| `blazor-counter-aot` | 2 769,005 | **glu d'interop JS UNIQUEMENT — et SOUS-ESTIME d'un montant inconnu** |

**Attribution Filament.** Chaque site d'allocation de tête est la **boucle de pilotage du harness
lui-même** (`driveIncrements`, `Promise`, `tick`, `evaluate:296`, API V8, `BYTECODE_COMPILER`). **Pas
une seule frame de `app.js` n'apparaît dans le profil.** Le build production et le build instrumenté
concordent à l'intérieur de leur propre dispersion (236–363 o) : si le chemin d'incrément allouait un
arbre de rendu, les deux différeraient et des frames `app.js` apparaîtraient. Ni l'un ni l'autre ne se
produit. **Les ~335 o sont le coût de pilotage du harness, identique pour tout framework.**

**Attribution Blazor — et pourquoi le rapport 0 vs 2 769 est INTERDIT.** Les sites de tête sont à 100 %
du JS de Blazor (`mo @ dotnet.runtime.js` 546 ko, `invokeDotNetMethodAsync` 406 ko,
`dispatchGlobalEventToAllElements` 270 ko, `applyEdits` 175 ko). **La sonde est structurellement AVEUGLE
à l'arbre de rendu .NET de Blazor**, qui vit dans la mémoire linéaire WASM (un seul `ArrayBuffer` pour
V8). **« Filament ~0 o vs Blazor 2 769 o » n'est PAS un résultat C3 et ne doit jamais être cité comme
tel** — cela compare le **total** de Filament au **sous-ensemble** « glu d'interop » de Blazor.
Quantifier l'allocation de Blazor exige un instrument côté .NET qui n'est pas construit ici.

**Verdict C3 : PASS.** Écritures DOM : exactement 1 sur les 5 incréments comptés, observé indépendamment
et concordant avec l'auto-rapport. Allocation : **~0 allocation d'arbre est SOUTENUE** pour Filament —
la sonde est complète pour lui (son runtime *est* du JavaScript ; à N=1000 avec un intervalle
d'échantillonnage de 1024 o, même 32 o/incrément émergeraient à ~32 ko). Une fausse revendication de
« 0 allocation » se verrait. Elle ne se voit pas. **Voir toutefois la réserve n°C : la sonde est plus
bruitée que ne l'admet ce verdict, et la conclusion tient pour des raisons architecturales.**

---

## C4 — Vitesse (jamais plus lent que Blazor AOT)

Médiane + IQR, **n = 10**, base **brotli** des deux côtés (Filament : `phase1/*.br.json` ; Blazor :
`final-warm/blazor-*-aot.br.json`). Métrique de tête : `msToMutation`.

| Scénario | **Filament** méd. (IQR) | **Blazor AOT** méd. (IQR) | **Blazor non-AOT** méd. | Pas plus lent que l'AOT ? | Rapport |
|---|---:|---:|---:|:--:|---|
| `create-warm` | **4,00** (0,800) | 7,35 (0,275) | 13,70 | **oui** | **1,84×** |
| `update` | **0,30** (0,075) | 3,60 (0,175) | 12,60 | **oui** | 12,0× ⚠️ |
| `swap` | **0,40** (0,100) | 3,45 (0,275) | 12,65 | **oui** | 8,6× ⚠️ |
| `clear` | **1,30** (0,075) | 2,90 (0,300) | 4,20 | **oui** | **2,23×** |
| `increment-warm` | **0,00** (0,000) | 1,00 (0,075) | 1,30 | **oui** | **🔻 PLANCHER — voir ci-dessous** |

*(Contexte, hors tête : `create-cold` Filament 6,15 ms vs Blazor AOT 23,90 / non-AOT 34,15.)*

**Verdict C4 : PASS sur les 5 scénarios.** Aucun échec, aucun timeout.

### 🔻 `increment-warm` est LIMITÉ PAR LE PLANCHER — et ce n'est **PAS une égalité**

**La prémisse de l'avertissement est RÉFUTÉE PAR LES DONNÉES.** L'inquiétude annoncée était que
l'appareil bute vers ~1 ms et rapporte ~1 ms **pour les deux**, affichant une fausse égalité. **Ce n'est
pas ce qui se passe** : l'appareil résout **très en dessous de 1 ms** — Filament lit 0,00 ms de médiane
(échantillons `[0, 0.1, 0, 0.1, 0, 0, 0, 0, 0, 0]`) et 0,30 / 0,40 ms sur `update`/`swap`. Les
échantillons de Blazor `[0.9, 1, 1, 0.9, 1, 1, 1, 0.9, 1.1, 1.1]` se groupent autour de 1,0
**sans entassement au minimum** : **1,00 ms est une lecture réelle, pas un artefact de plancher.**

**Ce qui EST limité par le plancher : la valeur de Filament elle-même.** Médiane 0,00 ms avec IQR 0,00
contre un quantum `performance.now` de 0,1 ms ⇒ **le coût réel d'incrément de Filament est
IRRÉSOLVABLE** : tout ce qu'on peut dire est qu'il est **< ~0,1 ms**.

> **CONSÉQUENCE HONNÊTE.** Le plancher limite la capacité à **QUANTIFIER** l'avantage, pas à
> l'**ÉTABLIR**. « > 10× plus rapide que l'AOT » est une **borne basse dérivée du quantum du timer**,
> **jamais une accélération mesurée**. Conformément à la consigne de cette entrée : **une égalité au
> plancher de l'appareil passe C4 mais ne prouve pas la parité.** Ici il n'y a pas d'égalité — mais
> le chiffre reste une borne, pas une mesure.

La garde anti-vacuité du harness (un prédicat déjà vrai avant le clic lève au lieu de rapporter 0)
garantit que ces lectures à 0 ms sont de vraies mesures post-clic, non vacuoles.

### ⚠️ Réserve de quantification sur `update` et `swap`

`update` (0,30 ms) siège à **3 quanta** du plancher de 0,1 ms ; `swap` (0,40 ms) à **4 quanta**.
**Les VERDICTS sont sûrs** (pas plus lent). **Les RAPPORTS 12,0× et 8,6× portent ~33 % / ~25
d'incertitude de quantification et ne doivent pas être cités à 3 chiffres significatifs.**

### Équité de la comparaison — vérifiée, pas supposée

- **Contrat de balisage des lignes (réserve n°6 de l'entrée n°2) : PASSE, et la réserve est LEVÉE.**
  Le harness épingle désormais le balisage **exact** (`checkRowMarkup()`), vérifié aux indices
  [0, 1, 2, 500, 998, 999]. `row0.outerHTML` =
  `<tr><td class="col-md-1">1</td><td class="col-md-4"><a class="lbl">adorable pink desk</a></td></tr>`.
  **Filament n'a PAS pris le raccourci à 3 éléments** que la réserve n°6 redoutait : il construit les
  mêmes 1 000 `<a>` décoratifs et 2 000 attributs `class` que Blazor.
- **Garde d'égalité de charge : PASSE.** Le `#run` chronométré de `create-warm` produit un flux de
  labels distinct du premier (ids 1001–2000) qui **correspond au flux cité pour Blazor** par l'entrée
  n°2 (« mushy blue mouse »…). Filament exécute le LCG Park-Miller identique (`c=c*16807%2147483647`,
  graine 42) : 3 000 multiply/modulo + 1 000 concaténations par `#run`. **Aucune mise en cache des
  labels.**
- **Aucun travail différé.** Le batch se vide **synchroniquement** dans le `finally` du handler de clic ;
  `lazyLoadedOnInteractionBytes = 0`.
- **Contrôle d'indépendance à l'encodage : 4 paires chaudes sur 5** concordent à ≤ 0,10 ms ;
  `rows/create-warm` diffère de 0,30 ms (7,5 %), bien à l'intérieur de son propre IQR (0,8–0,975).
  **Énoncé 4/5, PAS arrondi à 5/5** — c'est l'erreur exacte que l'entrée n°2 avait relevée en amont.

---

## C5 — Aucun runtime .NET expédié

**Verdict : PASS.** Filament fait **exactement 3 requêtes, au total**, dans les deux apps :

| Requête | `filament-counter` |
|---|---:|
| `/` | 680 o |
| `/css/app.css` | 748 o |
| `/app.js` | 1 436 o |

**Zéro artefact `.wasm`, zéro artefact `dotnet.*`, zéro requête `/_framework/`** — d'aucune sorte.
Preuves au-delà de la liste de requêtes :

1. `lazyLoadedOnInteractionBytes = 0` dans les deux apps — **aucun runtime n'est récupéré plus tard**
   à l'interaction non plus.
2. `untrackedRequests = []` et **service workers BLOQUÉS** : aucun octet ne peut arriver invisiblement.
3. Le détecteur d'AOT du harness rapporte indépendamment `aotObserved: null`, motif
   `no-signature-matched` — il a cherché une signature .NET dans les artefacts servis et **n'a rien
   trouvé à inspecter**.

**Contraste** : `blazor-counter-aot` fait **39 requêtes**, dont `dotnet.native.xc7yj6pp2h.wasm`,
`dotnet.runtime.a6jcqbs390.js`, `blazor.webassembly.958z1vx7fr.js` et **32 assemblies `.wasm` managées**.

---

## Ce qui a ÉCHOUÉ, et les réserves ouvertes — listées, non enfouies

**Aucun échec de scénario, aucun timeout, aucun chiffre fabriqué.** 152 itérations chronométrées
(140 valides au protocole à n=10, plus 12 itérations du run C3 à n=3 **jamais citées comme timing**),
0 échec, 0 timeout, 0 problème de contrat, 0 avertissement de poids, 0 requête non suivie, 0 violation
de cache froid ; `ok = true` dans les 7 fichiers de résultats.

**Corroboration non planifiée** : `blazor-counter-aot` re-mesuré ce jour à **3 353 458 o brotli** —
**exactement** le chiffre commité de l'entrée n°2, **à l'octet**. La baseline est vivante et
reproductible, pas un nombre périmé.

### Réserves ouvertes

**A. 🔴 DÉCISIVE — l'artefact mesuré n'est pas le livrable ; le générateur n'existe pas.**
`src/Filament.Generator/`, `src/Filament.Core/`, `src/Filament.Analyzer/` sont **vides**. Les chiffres
C1/C3/C4/C5 mesurent du **JS optimisé à la main**, pas la sortie d'un compilateur. La proposition
porteuse du POC — *« un générateur C# peut émettre ceci depuis du Razor, sous 10 ko, à ces temps »* —
est **non testée et son sujet n'est pas sur le disque**. Aucune des sept autres réserves ne compte
autant que celle-ci. **Elle conditionne le gate** (voir section Gate).

**B. 🔴 LE RAPPORT DE MESURE AMONT CONTIENT UNE FAUSSE AFFIRMATION DE FAIT, ici rectifiée.**
Le rapport amont affirmait : « *bench.mjs/server.mjs were NOT modified: no Filament branch was added to
the harness* ». **C'est faux.** Vérifié ce jour :
`git diff --stat HEAD -- bench/harness/bench.mjs` ⇒ **707 insertions(+), 6 deletions(−)**, non commitées ;
`selftest.mjs` est également modifié (+423). Pire, le rapport offrait une **chaîne de version comme
preuve** : « *IDENTICAL … including harnessVersion 1.2.0 — so Filament and the baseline it is compared
against were produced by the same timed code path* ». Or `HARNESS_VERSION = '1.2.0'` **n'a pas été
incrémenté** à travers ce diff de 707 lignes : **la chaîne ne peut pas distinguer les deux builds.**
Et la chronologie est décisive :

| Événement | Horodatage (UTC) |
|---|---|
| Baselines Blazor `final-warm/*` | 13:11 – 13:21 |
| **`bench.mjs` dernière écriture** | **14:50:47** |
| Runs Filament | 16:16 – 16:26 |

**Blazor a été mesuré avec le harness d'AVANT l'édition, Filament avec celui d'APRÈS.** C'est exactement
le hasard 1.1.0-vs-1.2.0 (33 % d'irreproductibilité) dont le rapport se félicitait d'avoir réchappé —
**rouvert, et masqué par la chaîne même offerte en preuve**.

> **ATTÉNUATION — vérifiée ici, et NON par le rapport amont.** Le chemin chronométré est **intact** :
> `waitForCondition` (433 → 439) et `measure` (495 → 501) ont **glissé de 6 lignes sans changement de
> contenu** — extraits et comparés à l'octet, **sha256 identiques** (`1619cc90b6029029`,
> `eee16e7d5b7e866a`). `runScenario` est **identique à l'octet** (3 531 caractères de part et d'autre).
> Les **6 suppressions** du diff sont **toutes** dans le contrat de balisage (le `cellsPerRow >= 2`
> permissif remplacé par un contrat strict) ; le reste est purement **additif** (sonde C3, contrat
> `rowMarkup`, drapeaux CLI). **La conclusion C4 survit** — mais **sur inspection, pas sur la preuve
> qu'offrait le rapport.** Le rapport a affirmé **l'absence d'un diff** au lieu de **l'innocuité d'un
> diff**, ce qu'il n'a jamais vérifié.
> → **Correctif : incrémenter `HARNESS_VERSION` à 1.3.0, commiter le harness, et re-mesurer la baseline
> Blazor sous le même build avant que ces chiffres n'arbitrent quoi que ce soit d'autre.**

**C. La sonde d'allocation est plus bruitée que ne l'admet la rédaction.** Pente à deux points,
N = 200 → 1000, et `lowBytes` sur les trois répétitions prod vaut **155 656 / 74 268 / 85 608** — une
dispersion de **2,1×**. Ces 81 ko de dispersion valent à eux seuls **±102 o/incrément de bruit de
méthode** sur un chiffre de ~335 o. Le rapport traite la concordance prod-vs-stats « à l'intérieur de
leur dispersion (236–363 o) » comme une confirmation : **un instrument à ±21 % qui s'accorde avec
lui-même à ±21 % ne discrimine pas grand-chose** et ne résoudrait pas ~100 o/incrément d'allocation
réelle. **La conclusion reste juste pour des raisons ARCHITECTURALES** (chemin minifié tracé :
`set value` → `A` → `q` → `h` → `n.fn()` → `I` réutilise le lien de dépendance existant via `i.dep===n`
→ `x(E,v)` → `n.data=e`, sans allocation en régime établi), et `topSites` sommant à 353 768 o = `highBytes`
exactement **sans aucune frame `app.js`** est une vraie preuve. **Mais le rapport s'appuie sur le profil
plutôt que sur l'architecture, et le profil est bruité.**

**D. L'artefact ne porte aucun zéro calibré pour la sonde d'allocation.** `measureC3` émet
`criterion: 'C3: … 0 render-tree allocation'` et, à côté, un `bytesPerIncrement.median` **nu, sans
verdict** — alors que chaque affirmation d'écriture DOM reçoit une chaîne de verdict nommant pass/fail.
Ce nombre **inclut le plancher de la boucle de pilotage** (~82 o/incrément pour une fixture n'allouant
rien, noté dans le code lui-même). Le plancher n'est établi qu'en `selftest.mjs` (`zeroBytes < 512`),
**contre une autre fixture, dans un autre processus**, et **n'est jamais reporté dans la sortie d'un run
réel**. → **L'artefact ne peut pas trancher le critère qu'il énonce lui-même.** Correctif : émettre
`noiseFloorBytesPerIncrement` avec un verdict de même forme que `domWrites.verdict`.

**E. La moitié « écritures DOM » de C3 est atteinte par Blazor aussi** (1 écriture/incrément). C'est une
**barre de correction que Filament franchit, pas une victoire sur Blazor**. Répété ici parce que
l'enfouir serait malhonnête dans le sens flatteur.

**F. `ALLOCATION_SCOPE_CAVEAT` contient une phrase fausse** : elle affirme que le coût de la boucle de
pilotage « *is IDENTICAL for every framework, so it biases both the same way and cannot manufacture a
difference* ». **Faux** : `driveIncrements` sonde avec `setTimeout(tick, 0)` et évalue
`el.textContent.trim()` à chaque tick (2 allocations de chaîne/tick), et **le nombre de ticks est
fonction de la latence de dispatch du framework**, pas une constante. Filament flush **synchroniquement**
dans `dispatchEvent` (0 tick supplémentaire) ; Blazor dispatche **asynchroniquement** et paie plusieurs
ticks par incrément. **Un framework plus lent se voit donc facturer plus d'allocation pour être plus
lent.** De plus, **toutes** les fixtures calibrant le plancher sont **synchrones**, donc le plancher
< 512 o n'est établi **que pour un dispatch synchrone** et **ne se transfère pas à Blazor**. Le mal est
borné (la mise en garde environnante **interdit déjà** la comparaison d'allocation Filament-vs-Blazor),
mais **la phrase fausse est précisément celle qui autoriserait à la citer**, logée dans le paragraphe
chargé de l'interdire. La moitié de C3 propre à Filament est **intacte** (dispatch synchrone = fixture
synchrone, plancher calibré à l'identique).

**G. Le rapport de `--shell-parity` ne couvre que l'app Counter** et imprime pourtant une revendication
**générale** de parité CSS « byte-for-byte » couvrant tous les labels. Il asserte une parité qu'il ne
vérifie pas, pour une app qu'il n'inspecte pas. *(C'est le mécanisme par lequel le défaut CSS ci-dessous
a survécu.)* → Correctif : boucler `--shell-parity` sur **chaque** label de production.

**H. `n = 10`, une machine, un Chrome, un OS.** Aucun intervalle de confiance, aucune réplication
inter-machines. **Suffisant pour un gate de POC ; insuffisant pour une revendication publiable**
(réserve n°15 héritée, **toujours NON LEVÉE**).

**I. Le seuil de C1 (< 10 ko gzip) repose toujours sur l'AUTORITÉ DU PROPRIÉTAIRE, pas sur une spec sur
le disque** (hérité de l'entrée n°2 ; re-vérifié — **aucun fichier de spec n'existe**). La marge de 2,36×
rend l'ambiguïté 10 000/10 240 sans objet, mais **la provenance du seuil est inchangée**.

**J. La comparaison C3 a utilisé `blazor-counter-aot`.** Les écritures DOM/incrément sont une propriété
d'architecture de rendu indépendante de l'AOT, donc `nojit` devrait se comporter identiquement — **mais
cela n'a pas été mesuré séparément.**

**K. Asymétrie non divulguée par le rapport amont** : `inPageHarness` (injecté via `addInitScript`) a
grossi d'environ **+261 lignes** pour la sonde C3. Il était **présent à chaque run Filament et absent de
chaque run Blazor**. **Le sens favorise Blazor** (Filament paie le parse supplémentaire), donc cela ne
flatte pas Filament — mais c'est une asymétrie que la revendication « identique sur tous les axes »
**nie**.

**L. Le cadrage de la marge C1 est généreux.** « Counter 3,49× sous, rows 2,36× sous » décrit un
`h1+p+span+button` (2 864 o) et une table à 4 boutons (4 243 o). **~2,5 ko est du runtime fixe** ; toute
l'app rows ne coûte que ~1,4 ko de plus que le compteur. Correct pour les apps que C1 nomme, mais
« 2,36× de marge » **se lit comme de la place pour Filament en général alors que c'est de la place pour
une table**.

### Défauts d'artefact relevés par l'audit — état vérifié ce jour

| Défaut | Sévérité | État |
|---|---|---|
| `build-filament.sh` copiait le CSS de **Counter** pour les 4 labels, y compris `filament-rows` — brisant la parité de style/layout (`border-collapse`, padding, `.col-md-1`) sur l'app même qui décide C4 | **majeur** | ✅ **CORRIGÉ ET VÉRIFIÉ DEPUIS L'ARTEFACT.** `css_for()` existe (ligne 192), est utilisé (ligne 542) et une assertion post-build compare aux octets **publiés** par le label Blazor. Sur le disque : `filament-rows/css/app.css` = md5 `1b67ed3e`, **917 o** = `baseline/Rows.Blazor` = `blazor-rows-nojit` ✔ ; `filament-counter/css/app.css` = md5 `66d7c50f`, **795 o** = `baseline/Counter.Blazor` ✔ |
| `--shell-parity` ne couvre que Counter et revendique la parité pour tous | mineur | ❌ **NON CORRIGÉ** — voir réserve **G** |
| `BENCH.md` entrée n°2 déclare la réserve n°6 « NON LEVÉE » alors que `checkRowMarkup()` la ferme | mineur | ✅ **RECTIFIÉ ICI** (l'entrée n°2 est append-only et n'est pas éditée) — voir C4 |
| `ALLOCATION_SCOPE_CAVEAT` affirme faussement que le coût de pilotage est identique entre frameworks | mineur | ❌ **NON CORRIGÉ** — voir réserve **F** |
| Aucun zéro calibré pour la sonde d'allocation dans l'artefact | mineur | ❌ **NON CORRIGÉ** — voir réserve **D** |
| `Computed` n'a **aucun chemin de disposition** (fuite mémoire) | **majeur** | ❌ **NON CORRIGÉ** — voir bloqueur n°4 ci-dessous |

### 🔴 Bloqueurs de correction sémantique du runtime — reproduits ce jour

`npx vitest run` ⇒ **7 échecs / 164 succès (171 tests)**, via `test/adversarial.test.ts`.
**Les 7 échecs sont VOULUS** : ils documentent **3 bugs sémantiques réels**, zéro flake.

1. **🔴 VALEUR SILENCIEUSEMENT FAUSSE — `core.ts:287 + :319 + :328`.** `refresh()` efface `DIRTY`
   **avant** que `recompute()` n'appelle `c.fn()` ; **rien ne le restaure en cas de throw**, et le
   `finally` exécute quand même `prune(c)` avec un curseur de **run partiel**, larguant des arêtes que
   le run n'a jamais atteintes. **Un computed dont la `fn` lève une seule fois reste CLEAN-mais-PÉRIMÉ :
   il renvoie une valeur fausse pour toujours, SANS erreur** ; un computed n'ayant jamais réussi renvoie
   `undefined` pour toujours ; **un effet en aval devient DÉFINITIVEMENT SOURD** — l'UI cesse
   silencieusement de se mettre à jour. **Sur le chemin principal de la Phase 2** : le générateur mappe
   les propriétés dérivées C# sur `computed()`, et les expressions C# lèvent couramment (null deref,
   division par zéro, index hors bornes). Correctif vérifié : ~4 lignes.
2. **🔴 MISE À JOUR SILENCIEUSEMENT MANQUÉE — `core.ts:351-352`.** `flush()` efface `DIRTY|PENDING`
   **avant** `runEffect`, **sans garde par effet** : un throw **avorte toute la boucle de drainage**. Les
   effets déjà dépilés-et-effacés **manquent définitivement** ce changement — laissés propres, non
   exécutés, et **rien ne les re-marquera**. Même cause racine que le n°1.
3. **🔴 CORRUPTION DU DOM — `list.ts:158, :166, :169.** Clés dupliquées : deux anciennes lignes
   partageant une clé résolvent vers le **même** `ni` (l'une n'est jamais démontée) **et** `patched`
   sur-compte, si bien que la garde `patched >= toPatch` **démonte une ligne dont la clé SURVIT**.
   **Mesuré, non théorisé** : `[1,1,2] → [2,1]` donne `[1,2,1]` (3 nœuds pour 2 items) ;
   `[1,2,2,3] → [3,2,1]` donne `[3,2,1,2]` ; `[1,1,2,2] → [2,2,1,1]` donne **SIX nœuds pour quatre
   items**. **Le nombre de nœuds CROÎT ; les orphelins s'accumulent.** C'est l'algorithme de Vue 3 —
   mais **Vue émet un avertissement dev sur les clés dupliquées et Filament n'en a AUCUN**.
4. **🟠 `Computed` n'a aucun chemin de disposition (fuite non bornée)** — relevé par l'audit, **non
   couvert par la suite existante**. Le constructeur n'enregistre jamais auprès de `owner` et
   `disposeOwned` ne parcourt que des `Effect`. Tout `Computed` créé dans un scope de disposition (un
   template de ligne) et lisant un signal plus durable est **retenu POUR TOUJOURS** par la liste
   d'abonnés de ce signal — avec sa closure, la ligne capturée et ses nœuds DOM. Démontré :
   `globalMult.subs` sur 10 cycles `#run` = `[100, 200, 300, … 1000]` (**non borné** ; contrôle
   `effect` : `[100, …, 100]`) ; rétention après `gc()` : **50/50** lignes démontées encore joignables
   (contrôle : 0/50) ; dégradation : 9 001 liens fuités, 2 000 écritures passant de **5,8 ms à 60,7 ms
   (10,5×)**. **Devient porteur en Phase 2** : le premier `@foreach` contenant une expression dérivée
   (`@(row.Qty * Price)`) fuit 1 `Link` + tout le graphe de ligne **par ligne et par `#run`**.
5. **🟡 Aucune détection de cycle** (plus faible sévérité : **gel, pas valeur fausse**).
   `s.value = s.value + 1` dans un effet, et deux effets s'écrivant mutuellement, **tournent à l'infini**
   (prouvé avec un disjoncteur à 50 000 itérations). Les runtimes de type Solid se comportent
   similairement — **non compté parmi les bloqueurs**, mais une garde est peu coûteuse.

> **PORTÉE DE CES BLOQUEURS, ÉNONCÉE POUR NE PAS ÊTRE SUR-LUE.**
> **AUCUN des bloqueurs n'est atteignable depuis les apps mesurées** : le compteur et les lignes ne
> lèvent jamais, utilisent des ids **monotones uniques** (aucune clé dupliquée), n'ont **aucun cycle**,
> et `samples/Rows/rows.js` n'utilise **délibérément que `effect()`** dans `createRow`, donc le benchmark
> ne construit **jamais** de `Computed` dans un scope de ligne. **LES CHIFFRES C1/C3/C4/C5 NE SONT PAS
> INVALIDÉS.** **La thèse du POC n'est PAS validée sur un mensonge** — le pire scénario redouté ne s'est
> pas produit. Le runtime est **rapide, petit ET sémantiquement correct sur tout chemin que le harness
> mesure.** Les bugs vivent **hors du chemin mesuré**, dans la gestion d'exceptions et un cas d'erreur
> utilisateur documenté. **Leur signification est prospective** : le benchmark **recharge la page à
> chaque itération**, donc `create-warm` resterait plat et **le benchmark MASQUERAIT la fuite** pendant
> que le runtime se dégrade en application réelle.

**Ce que la suite de tests fait bien (contrôlé, non supposé)** : `subCount()` parcourt la liste côté
**source**, ce qui distingue « désabonné » de « marqué-et-ignoré » — cette différence **est** la fuite.
De vrais **contrôles négatifs** existent (le contrôle de rétention fuit délibérément 50 lignes et
asserte que 50 survivent, prouvant que le harnais `WeakRef` **voit** la rétention). Le test GC rejette
`heapUsed` **avec un motif énoncé** (un mutant le passait). `list-fuzz` vérifie la minimalité contre un
**oracle LIS O(n²) indépendant**. Le commentaire de `propagate()` **admet** qu'une mutation testing a
montré que supprimer son `continue` ne fait échouer aucun test — **l'inverse de l'autocongratulation**.
**Les lacunes sont précisément là où les bugs ont été trouvés.**

**Ce qui a résisté à l'attaque (vérifié, non supposé)** : aucune fuite sur `effect` (50 × `#run(200)` :
compte d'abonnés **exactement 200 à chaque cycle**) · **zéro allocation sur le chemin chaud** (100 000
incréments ⇒ `stats.links === 0`) · **LIS correct et MINIMAL** (swap de 2 parmi 1 000 = **exactement 2
déplacements**, pas 1 000 — la revendication de tête tient) · ré-entrance **plate, non récursive**
(200 000 runs, aucun stack overflow) · **glitch-freedom** tenue sur diamants asymétriques et deps
conditionnelles.

**Précision de revendication** : « computed est paresseux » doit s'énoncer « **les computed NON OBSERVÉS**
sont paresseux ». Une fois qu'un effet en dépend, `checkDirty` **doit** l'évaluer pour décider de
re-exécuter — vérifié. Correct et nécessaire, mais la revendication non qualifiée est trompeuse.

---

*Fin de l'entrée n°3. Ne pas modifier — ajouter une entrée n°4 pour toute rectification.*

---

## Entrée n°4 — 2026-07-16 — Phase 1 : mesure propre (un seul harness, runtime corrigé)

Re-mesure **C1** et **C4** avec les **deux frameworks sur le MÊME harness**, et sur un runtime dont
**3 bugs sémantiques + l'absence de garde de cycle** ont été corrigés depuis l'entrée n°3.

### Pourquoi cette entrée existe, et ce qu'elle supersède

L'entrée n°3 a comparé **deux mesures qui n'étaient pas comparables**, et rien dans l'appareil ne
pouvait le détecter :

| | Blazor (entrée n°2, cité par n°3) | Filament (entrée n°3) |
|---|---|---|
| Mesuré à | **15:17** | **18:20** |
| Harness sur le disque | **avant** l'édition C3 | **après**, **+701 lignes** |
| `HARNESS_VERSION` déclaré | **`1.2.0`** | **`1.2.0`** |

**La version était tenue à la main. Elle n'a pas bougé pendant que le harness bougeait de 701 lignes** —
donc les deux runs *affirmaient* une comparabilité qui **n'existait pas**, et l'affirmation était
**infalsifiable** : aucune donnée enregistrée ne permettait de contredire la chaîne `"1.2.0"`.

Deux changements corrigent cela :

1. **L'identité du harness est désormais un HASH DE CONTENU**, calculé au runtime et écrit dans
   `environment.harness` de **chaque** JSON de résultat. Une chaîne écrite à la main est remplacée par
   une **mesure de l'appareil lui-même**. `computeHarnessIdentity()` **lève** plutôt que de dégrader :
   un run incapable d'établir son identité doit s'arrêter, car un hash nul est exactement la
   revendication infalsifiable qu'on remplace. `HARNESS_VERSION` est conservé (**1.3.0**) et annoté
   **dans le code** comme *étiquette, pas preuve*.
2. **Les deux frameworks sont re-mesurés dans le même run**, sur ce hash unique.

**S'y ajoute que le runtime lui-même a changé entre n°3 et n°4** : les 3 bugs sémantiques bloquants de
l'entrée n°3 (valeur silencieusement fausse sur throw ; mise à jour silencieusement manquée ;
corruption du DOM sur clés dupliquées) sont **corrigés**, plus une **garde de cycle**. Les octets C1
ci-dessous portent donc sur un **bundle reconstruit** qui embarque ces correctifs.

> **CE QUE CETTE ENTRÉE SUPERSÈDE, NOMMÉMENT.**
> - **Les RAPPORTS C4 de l'entrée n°3 : SUPERSEDED en tant que quantités.** Chaque temps Blazor a bougé
>   sur le harness propre ; **tous** les rapports de la table C4 de n°3 sont donc faux. Ils
>   **sous-estimaient** Filament. À restater depuis cette entrée, **pas** à annoter en note de bas de page.
> - **Le VERDICT C4 de l'entrée n°3 : NON invalidé, et même renforcé** — voir « Sens du biais ».
> - **La comparaison de POIDS de l'entrée n°2 : NON invalidée du tout.** Les 8 configs Blazor
>   reproduisent leurs octets **à l'octet près** (delta = 0 partout). Vérifié indépendamment ici.
> - **C3 : NON re-mesuré** (`--c3` désactivé). Cette entrée **ne dit rien** sur C3. Les chiffres C3 de
>   l'entrée n°3 restent ce qu'ils étaient, avec leurs réserves.

### Environnement

| | |
|---|---|
| Machine | Mac17,6 — Apple M5 Max, 18 cœurs, arm64, 64 Gio |
| OS | darwin 25.5.0 (`productVersion` 26.5.1) |
| Chrome | 150.0.7871.124, **headless** |
| Node | v26.5.0 · Playwright 1.61.1 · .NET SDK 10.0.301 |
| esbuild | 0.28.1 — **conforme** à la chaîne épinglée Phase 1 (aucun avertissement de dérive du minifieur) |
| Date | **2026-07-16**, run 19:50:03 → 20:07 (~17 min) |

**Contrôle de parité d'environnement.** L'environnement enregistré de l'entrée n°2 a été comparé
**champ par champ** à celui-ci : Chrome, Node, Playwright, SDK .NET, CPU, modèle, `productVersion`,
headless — **tous identiques**. **C'est ce qui autorise l'attribution du `deltaVsPrevious` ci-dessous** :
toutes les autres variables étant tenues fixes, le mouvement de Blazor est imputable au changement de
harness **plus** l'état machine du jour, et non à une dérive de chaîne d'outils. Si Chrome avait
différé, le delta aurait été **ininterprétable** et il fallait le dire.

### Identité du harness — hash de contenu

```
sha256 = 47e7e46f372f8573e9a574713cee9cd6c125d55361fdb5d4b47755ac7d4536f8
```

**Partagé par les 12 configs du run** (`configsSharingHash: 12`, `unified: true`). L'analyse **refuse
d'émettre une comparaison inter-configs** si l'ensemble des hashs n'est pas de cardinal 1.

| Fichier | sha256 |
|---|---|
| `bench.mjs` | `e4b5e96e5e9194e0cea9537f4f9bbb2cc552815bdba81b6f83eb0de8be6d7c54` |
| `server.mjs` | `c4867310477486d206f9e2df5319974f99676a0fb66771d878c5f734d96f7034` |
| `expected-labels.json` | `877b14615b32f85a5d9826ee43558f63a1dda59eaae85d42813f7ad3070d51d5` |

**Agrégation** : sha256 par fichier, puis sha256 sur les lignes `"nom:sha256"` **triées** —
indépendant du chemin, de l'ordre et de la machine. **Re-calculé indépendamment lors de la rédaction
de cette entrée : reproduit exactement**, y compris les trois hashs par fichier.

**Périmètre du hash, et pourquoi ces trois-là.** `bench.mjs` (le driver, la primitive de chronométrage,
et `inPageHarness()` — qui est une **fonction dans ce fichier**, injectée par `addInitScript`, donc il
n'existe **aucun asset in-page séparé** à manquer) ; `server.mjs` (négociation et **niveaux de
compression** — changer la qualité brotli déplace tous les poids sans toucher `bench.mjs`) ;
`expected-labels.json` (la fixture d'or **définit** ce que « la même charge » veut dire).
**Exclus** : `selftest.mjs` (jamais sur le chemin de mesure) et `package*.json` (Playwright/Chrome sont
déjà observés dans `environment`).

### Protocole et quiescence

**Strictement séquentiel** : une config à la fois, un navigateur à la fois, **jamais deux benchmarks
concurrents** (section 7 de la spec). Le runner est une boucle bash sérielle ; la seule chose mise en
arrière-plan était la boucle elle-même, et l'attente a été bloquante.

Protocole **non affaibli** : cache **FROID** à chaque chargement (`BrowserContext` neuf + CDP
`Network.setCacheDisabled` + `Cache-Control: no-store`) · poids via CDP `encodedDataLength` ·
**10 runs chronométrés/scénario** · **MÉDIANE + IQR**, jamais la moyenne · 3 runs de poids/config.

**Santé** : 12/12 `ok=true`, **0 échec**, **440/440** itérations chronométrées enregistrées (20 par
config counter, 50 par config rows). Aucun timeout, aucun retry, **aucun échantillon écarté**.

#### Commande de rejeu

```bash
# 1. Reconstruire le runtime + les bundles d'apps (les correctifs sémantiques changent les octets).
cd src/filament-runtime && npm run verify && cd -
bash bench/build-filament.sh filament-counter filament-rows

# 2. Republier la baseline Blazor (purger obj/ et bin/ ENTRE chaque config — cache
#    static-web-assets empoisonné par la bascule AOT ; voir DECISIONS.md).
bash bench/publish-baseline.sh

# 3. Mesurer les 12 configs, SÉRIELLEMENT. Base de tête = brotli ; gzip = base de C1.
#    Racine statique : <label>/wwwroot pour BLAZOR, <label> SANS wwwroot pour FILAMENT.
node bench/harness/bench.mjs --dir bench/publish/filament-rows \
  --app rows --label filament-rows --runs 10 --weight-runs 3 \
  --max-encoding br --headless --no-aot \
  --out bench/results/phase1-clean/filament-rows.br.json
# idem pour filament-counter (--app counter) et les 4 configs blazor-* (--dir .../wwwroot,
# --aot pour les labels -aot), en gzip et en br.
```

---

## Résultats — C1 (poids du bundle)

**Base = octets SUR LE FIL** (`encodedDataLength`, **en-têtes de réponse inclus**), `toInteractive`,
cache froid. **C'est la base la plus SÉVÈRE, et c'est délibéré** : l'aperçu au build (somme des
frères gzip, hors en-têtes) donne 2 142 / 3 531 o — **plus flatteur, donc non retenu**. C'est un
aperçu, pas la mesure. C'est aussi **exactement le champ d'où venaient les 2 864 / 4 243 de
l'entrée n°3** : le delta est **comparable terme à terme**.

| Config | gzip (o) | IQR | Entrée n°3 (o) | **Δ correctifs** | Gate < 10 000 | Marge |
|---|---:|---:|---:|---:|:---:|---:|
| `filament-counter` | **2 976** | **0** | 2 864 | **+112** | ✅ **PASS** | 7 024 o |
| `filament-rows` | **4 365** | **0** | 4 243 | **+122** | ✅ **PASS** | 5 635 o |

| Config | brotli (o) | IQR | Entrée n°3 (o) | **Δ correctifs** |
|---|---:|---:|---:|---:|
| `filament-counter` | **2 604** | **0** | 2 494 | **+110** |
| `filament-rows` | **3 904** | **0** | 3 794 | **+110** |

- **IQR = 0 sur les quatre** : poids parfaitement reproductible.
- **Les deux lectures de « 10 ko » passent.** Le gate est ambigu (10 000 décimal / 10 240 binaire) et
  **ni la spec ni nous ne le tranchons en choisissant la lecture flatteuse** : le build rapporte contre
  **les deux**. Contre 10 240 : marges de **7 264** et **5 875** o. **PASS des deux côtés.**
- **La correction sémantique a coûté ~110–122 o gzip** — soit **~1,1 % d'un budget de 10 000 o**,
  contre **56–70 % de marge restante**. **C1 n'est pas près d'échouer.** Aucune correction n'a été
  rognée pour tenir dans le budget.

> **PROVENANCE VÉRIFIÉE À LA RÉDACTION, ET C'EST LE CONTRÔLE QUI COMPTE.** Les bundles mesurés à 19:48
> ont été **reconstruits depuis la source livrée** : les md5 sont **identiques**
> (`filament-counter/app.js` = `425e2d6d65be412bf23ba43b0cb22298`, `filament-rows/app.js` =
> `6cc803a2b282cf6cb45cebf5189ff230`). **L'arbre livré produit, à l'octet près, le bundle qui a été
> mesuré** — donc ces octets C1 décrivent bien le code committé, correctifs sémantiques **et garde de
> cycle inclus** (la chaîne `Filament: cycle detected` est présente dans les deux `app.js` de
> production). Le marqueur `filament:stats` est **absent** des bundles de production et **présent**
> dans les builds `-stats` : l'instrumentation C3 est bien éliminée par DCE de ce qui est pesé.

---

## Résultats — C4 (vitesse) — les trois colonnes viennent de CE run

`msToMutation`, **médiane et IQR, n = 10**, base brotli, cache froid, un seul harness
(`47e7e46f…`). **Aucune colonne n'est héritée d'une entrée précédente.**

### Rows (1 000 lignes)

| Scénario | Chaud | **Filament** méd. | IQR | **Blazor AOT** méd. | IQR | **Blazor non-AOT** méd. | IQR | vs AOT | vs non-AOT |
|---|:---:|---:|---:|---:|---:|---:|---:|---:|---:|
| `create-cold` | non | **6,20 ms** | 0,425 | 33,85 ms | 3,2 | 36,10 ms | 2,9 | *5,5×* | *5,8×* |
| **`create-warm`** | **OUI** | **3,65 ms** | **1,475** | **7,80 ms** | 1,8 | **14,60 ms** | 0,45 | **~2,1×** | **~4,0×** |
| `update` (1/10) | oui | **0,30 ms** | 0,1 | 4,10 ms | 0,35 | 13,55 ms | 1,125 | **~13,7×** ⚠️ | ~45× ⚠️ |
| `swap` | oui | **0,40 ms** | **0** | 3,85 ms | 0,675 | 12,60 ms | 1,725 | **~9,6×** ⚠️ | ~31,5× ⚠️ |
| `clear` | oui | **1,70 ms** | 0,5 | 3,00 ms | 0,1 | 4,80 ms | 0,275 | **~1,8×** | ~2,8× |

### Counter

| Scénario | Chaud | **Filament** méd. | IQR | **Blazor AOT** méd. | IQR | **Blazor non-AOT** méd. | IQR | vs AOT |
|---|:---:|---:|---:|---:|---:|---:|---:|---:|
| `increment-cold` | non | **0,40 ms** | 0,075 | 21,45 ms | **7,6** | 17,75 ms | 1,25 | *~53,6×* ⚠️🔇 |
| **`increment-warm`** | **OUI** | **0,10 ms** 🚧 | 0,075 | **1,10 ms** | 0,1 | **1,65 ms** | 0,25 | **≥ ~11×** 🚧 |

**`notSlowerThanAot` = `true` sur les 7 scénarios.** **C4 PASSE.**

### Légende — et ces marqueurs ne sont pas décoratifs

- 🚧 **`increment-warm` est LIMITÉ PAR LE PLANCHER — NE PAS citer un facteur d'accélération.**
  **3/10** échantillons Filament valent **exactement 0,0 ms** (base gzip : **5/10**, médiane 0,05).
  Le coût réel de Filament est **INCONNU et plus petit que l'instrument** ; les 1,10 ms de Blazor AOT
  sont, eux, une valeur **résolue**. **L'énoncé honnête est « Filament est AU MOINS ~11× plus
  rapide »**, en bornant Filament à **un quantum**. Le « 11× » naïf et le « 20× » qu'implique la base
  gzip **divisent tous deux par un artefact de quantification** : ce ne sont **pas des mesures**.
  Corriger cela demande un **autre instrument** (chronométrage groupé de N incréments), pas un rejeu.
- ⚠️ **Résolu mais GROSSIER.** `update` (0,30 ms ≈ **3 quanta**) et `swap` (0,40 ms ≈ **4 quanta**)
  portent ~**±17 %** et ~**±13 %** d'incertitude **sur leur rapport**, du seul fait de la résolution.
  **Ils ne sont PAS marqués `floorLimited`, et la distinction est délibérée** : **aucun** échantillon
  n'a touché 0,0 ms (min = 0,2 et 0,3 ms), donc la valeur a été **mesurée**, pas plafonnée. Ce critère
  est calculé **depuis les échantillons eux-mêmes**, pas depuis un seuil choisi à la main.
  **Verdicts sûrs ; rapports à ne pas citer à 3 chiffres significatifs.**
- 🔇 **`increment-cold` : ordre NON CRÉDIBLE, ne rien en conclure.** Blazor **AOT** (21,45 ms,
  **IQR 7,6**) y lit **plus LENT** que non-AOT (17,75 ms). Ce n'est pas un résultat, c'est du bruit.
  **Aucune conclusion AOT-vs-nojit ne doit être tirée d'une ligne froide.**
- *Italique* = **scénario FROID : ce n'est PAS une comparaison de rendu.** Blazor paie le démarrage
  .NET/wasm **dans la fenêtre mesurée** ; Filament n'a **aucun runtime à démarrer**. Réel et visible
  par l'utilisateur, mais **`create-warm` / `increment-warm` sont les nombres comparables terme à
  terme**. Les médianes froides de Blazor sont aussi les **plus bruitées** du run (IQR 2,9–7,6).
- **`create-warm` est le scénario le plus bruité de Filament** (IQR **1,475**, échantillons 1,8–4,3,
  **légèrement bimodal**). La marge ~2,1× est **réelle mais la moins serrée du lot**. Un run plus long
  s'impose si ce rapport précis devient porteur.

### Recoupements de validité

| Contrôle | Résultat |
|---|---|
| **Base d'encodage identique** | Les deux frameworks utilisent la **même** chaîne de méthode (CDP `Network.loadingFinished.encodedDataLength`). Les runs gzip servent **100 % gzip** aux **deux**, les runs br **100 % br** aux deux : **aucune négociation mixte**. |
| **En-têtes cohérents** | 278 o/requête (Filament, 834/3) vs 271 o/requête (Blazor, 10 582/39). Cohérent. Zéro requête non suivie, zéro avertissement, **aucun delta négatif**. |
| **Poids Blazor reproduit** | **Les 8 configs à l'octet près vs entrée n°2 : delta = 0 partout** (`rows-aot` br 3 350 819 = 3 350 819 ; `counter-aot` gzip 4 849 976 = 4 849 976). **Re-vérifié indépendamment à la rédaction.** |
| **Parité de charge** | Les deux frameworks émettent un balisage de ligne **identique à l'octet** (`row0outerHTML` identique), 1 000 lignes, **même fixture d'or** (sha256 identique), `problems: []`. |
| **Aucune contamination** | Horodatages strictement séquentiels, chaque config démarrant ~0,5 s après la fin de la précédente. **Zéro recouvrement.** |

> **Ce que le poids Blazor à l'octet près prouve, et c'est fort.** Un poids **byte-identique** à travers
> un changement de harness confirme **indépendamment** que les artefacts publiés n'ont pas bougé et que
> la négociation serveur est déterministe. **Cela isole le delta ci-dessous comme un effet
> PUREMENT TEMPOREL.**

---

## `deltaVsPrevious` — ce que valait le décalage de harness, chiffré

**C'est la raison d'être de ce run.** Publié **dans les deux sens**, comme demandé.

**POIDS : mouvement nul.** Voir ci-dessus. **La perturbation est exclusivement temporelle.**

**TEMPS (base br, entrée n°2 → ce run). Les 14 scénarios Blazor, AUCUN omis** — recalculés
indépendamment depuis les JSON bruts et la table de l'entrée n°2 :

| Config | Scénario | Chaud | Entrée n°2 | Ce run | Δ | Δ % |
|---|---|:---:|---:|---:|---:|---:|
| `rows-aot` | `create-cold` | non | 23,90 | 33,85 | **+9,95** | **+41,6 %** |
| `counter-aot` | `increment-cold` | non | 15,85 | 21,45 | **+5,60** | **+35,3 %** |
| `rows-nojit` | `create-cold` | non | 34,15 | 36,10 | +1,95 | +5,7 % |
| `counter-nojit` | `increment-cold` | non | 17,15 | 17,75 | +0,60 | +3,5 % |
| **`counter-nojit`** | **`increment-warm`** | **OUI** | **1,30** | **1,65** | **+0,35** | **+26,9 %** ← **plus grande perturbation CHAUDE** |
| `rows-nojit` | `clear` | oui | 4,20 | 4,80 | +0,60 | **+14,3 %** |
| `rows-aot` | `update` | oui | 3,60 | 4,10 | +0,50 | +13,9 % |
| `rows-aot` | `swap` | oui | 3,45 | 3,85 | +0,40 | +11,6 % |
| `counter-aot` | `increment-warm` | oui | 1,00 | 1,10 | +0,10 | +10,0 % |
| `rows-nojit` | `update` | oui | 12,60 | 13,55 | +0,95 | +7,5 % |
| `rows-nojit` | `create-warm` | **OUI** | 13,70 | 14,60 | +0,90 | +6,6 % |
| `rows-aot` | `create-warm` | **OUI** | 7,35 | 7,80 | +0,45 | +6,1 % |
| `rows-aot` | `clear` | oui | 2,90 | 3,00 | +0,10 | +3,4 % |
| **`rows-nojit`** | **`swap`** | oui | **12,65** | **12,60** | **−0,05** | **−0,4 %** ← **PLUS RAPIDE** |

### Rectifications au rapport de mesure amont — il flattait, dans le sens qui arrange

Le rapport de mesure amont a été **réfuté sur trois points** par la vérification sceptique, **confirmés
indépendamment ici depuis les JSON bruts**. Les trois vont **dans le sens flatteur**, et c'est
précisément pourquoi ils sont publiés :

1. **« *all Blazor got SLOWER* » est FAUX.** `rows-nojit/swap` va **12,65 → 12,60 ms** (**plus
   rapide**). L'énoncé correct est **13/14, pas 14/14**. **Un demi-quantum** : négligeable en
   magnitude — **mais un universel énoncé est falsifié par les données du run lui-même.**
2. **« *warm perturbation is single-digit percent* » est FAUX**, et **la liste amont était
   sélectivement incomplète.** `counter-nojit/increment-warm` **+26,9 %** est **la plus grande
   perturbation chaude du run** et était **entièrement absente** de la section dont c'est **l'unique
   objet** — pendant que son voisin **plus petit** `counter-aot/increment-warm` (+10,0 %) y **figurait**,
   une ligne plus loin. Également omis : `rows-nojit/clear` (+14,3 %) et `rows-nojit/update` (+7,5 %).
   **Toutes les omissions sont des lignes `nojit`.** *En toute équité : l'argument du rapport lui-même
   (« un quantum sur une valeur de 1 ms, c'est de la résolution, pas du signal ») couvrirait le +26,9 %
   — il vaut +0,35 ms, ~3 quanta. Mais il ne l'applique jamais, parce qu'il ne liste jamais la ligne.*
3. **La fourchette « *+0,7 % à +6,6 % sur create-warm* » est INFONDÉE.** Aucun delta `create-warm` br
   ne vaut +0,7 % ; les deux valeurs réelles sont **+6,1 %** et **+6,6 %**. Chiffre errant.

**Pourquoi ces omissions comptent, alors qu'elles sont petites.** La dérive de Blazor **vers le haut**
est **exactement ce qui gonfle les rapports de Filament**. Une section qui sous-estime cette dérive
**fait paraître la stabilité de ces rapports mieux établie qu'elle ne l'est**. **Défaut de RAPPORT, pas
défaut de MESURE** : il n'atteint **aucun** verdict de gate.

### Interprétation, honnêtement bornée

Les scénarios **chauds** — les seuls sur lesquels C4 se prononce — ont bougé de **−0,4 % à +26,9 %**,
et **≤ +6,6 % sur `create-warm`**, le scénario de tête. Les scénarios **froids** ont bougé de
**+3,5 % à +41,6 %**, mais portent des **IQR de 2,9 à 7,6 ms** (contre 0,1–1,8 à chaud) : **l'essentiel
est de la variance de démarrage à froid, pas un effet de harness**. Le **+41,6 % a un IQR plus large
que plusieurs des deltas auxquels on le compare**.

**On ne peut PAS séparer proprement « changement de harness » de « état machine/thermique du jour »
avec n = 1 baseline par config, et on ne prétendra pas le contraire.** La lecture correcte est :
**perturbation chaude de l'ordre de quelques % à ~27 % sur les petites valeurs ; froid trop bruité pour
être attribué.**

### Sens du biais — et pourquoi le verdict tient *a fortiori*

**Blazor est plus lent sur le harness propre dans 13 scénarios sur 14.** L'ancien registre comparait
donc un **Blazor mesuré sur le harness RAPIDE** à un **Filament mesuré sur le LENT** : **l'asymétrie
handicapait FILAMENT**, exactement comme la tâche l'anticipait. Re-mesurer les deux sur un seul harness
**éloigne** Blazor de Filament ⇒ **la marge de Filament est, si tant est, LÉGÈREMENT PLUS GRANDE** que
ce que le registre sale montrait.

**Un verdict qui a survécu à un test biaisé CONTRE lui survit *a fortiori* au test non biaisé.**

**Robustesse du gate, testée et non supposée** : recalculé contre les **ANCIENS** chiffres Blazor (les
plus rapides), **chaque scénario passe encore** — `create-warm` **2,01×**, `update` **12,00×**,
`swap` **8,63×**, `clear` **1,71×** (la plus étroite), `increment-warm` **10,00×** (borné). **Aucune
perturbation de l'ampleur observée ne menace le verdict.**

---

## Verdict du gate

| Critère | Verdict | Sur quoi |
|---|:---:|---|
| **C1** — < 10 ko gzip | ✅ **PASS** | 2 976 / 4 365 o sur le fil. Passe aussi contre 10 240. Marge 56–70 %. |
| **C4** — pas plus lent que Blazor AOT | ✅ **PASS** | 7/7 scénarios, `notSlowerThanAot: true`, **un seul harness**. |
| **C3** | — | **NON re-mesuré dans ce run.** |

### 🟢 **`gateVerdict` = PASS** — pour C1 et C4, **sur les bundles corrigés, à base honnête**.

> **Portée, énoncée pour ne pas être sur-lue.** Ce `PASS` porte sur **C1 et C4 seulement**, sur du
> **JS écrit à la main** (l'avertissement de portée de l'entrée n°3 — *l'artefact n'est pas la sortie
> d'un générateur* — reste **intégralement en vigueur**), et **ne dit rien de C3**. Il **ne déclare pas
> la Phase 1 franchie** : la décision n°34 (gate **CONDITIONNEL**) reste ouverte sur le point qui la
> tient — **le générateur n'existe toujours pas**, donc la proposition porteuse n'est toujours pas
> testée. **Ce qui a changé depuis n°3, c'est l'autre condition** : les 3 bloqueurs sémantiques sont
> **corrigés et vérifiés** (voir DECISIONS.md n°35–41).

---

## Réserves ouvertes — listées, pas enterrées

**A. `increment-warm` est LIMITÉ PAR LE PLANCHER.** 3/10 échantillons br (5/10 gzip) à exactement
0,0 ms. **« ≥ ~11× » est défendable ; « 11× » et le « 20× » de la base gzip ne le sont pas.** Exige un
autre instrument, pas un rejeu. *(Réserve héritée n°32 de DECISIONS.md, **toujours NON LEVÉE**.)*

**B. `update` (3 quanta) et `swap` (4 quanta) sont GROSSIERS** : ~±17 % / ±13 % sur le rapport, du seul
fait de la résolution. Verdicts sûrs, **rapports non citables à 3 chiffres significatifs**.

**C. Aucune ligne FROIDE ne supporte de conclusion AOT-vs-nojit.** `increment-cold` donne AOT **plus
lent** que non-AOT (21,45 vs 17,75, IQR 7,6) — **ordre non crédible, bruit pur**.

**D. `create-warm` est le scénario le plus bruité de Filament** (IQR 1,475, 1,8–4,3, légèrement
bimodal). La marge ~2,1× est la **moins serrée** du lot.

**E. Le mouvement FROID du `deltaVsPrevious` (jusqu'à +41,6 %) N'EST PAS ATTRIBUABLE proprement**
entre changement de harness et état machine : **n = 1 baseline par config** dans l'entrée n°2.

**F. 🔴 CONFOND D'ORDRE NON DIVULGUÉ PAR LE RAPPORT AMONT — l'exécution était séquentielle mais PAS
ENTRELACÉE.** **Tout Blazor a tourné en premier (19:50–20:01), tout Filament en dernier (20:02–20:07).**
La **dérive thermique/machine est donc CONFONDUE avec le framework**, et la section « quiescence » du
rapport amont **ne le mentionne jamais**. **Le sens est CONSERVATEUR pour le verdict** (Filament est
mesuré **en dernier, sur la machine la plus chaude**), donc cela ne flatte pas Filament — **mais cela
devait être dit, et un futur run doit ENTRELACER les configs.**

**G. `n = 10`, une machine, un Chrome, un OS.** Aucun intervalle de confiance, aucune réplication
inter-machines. **Suffisant pour un gate de POC ; insuffisant pour une revendication publiable.**
*(Réserve héritée n°15/H, **toujours NON LEVÉE**.)*

**H. Le seuil C1 (< 10 ko gzip) repose toujours sur l'AUTORITÉ DU PROPRIÉTAIRE, pas sur une spec sur le
disque** — re-vérifié : **aucun fichier de spec n'existe**. La marge rend l'ambiguïté 10 000/10 240 sans
objet, **mais la provenance du seuil est inchangée**. *(Héritée n°I, **NON LEVÉE**.)*

**I. 🔴 LA GARDE DE CYCLE N'A AUCUNE COUVERTURE DANS LA SUITE ADVERSARIALE — et les deux tests de cycle
DOCUMENTENT ENCORE LE BUG COMME PRÉSENT.** `test/adversarial.test.ts` **n'a pas été retourné** après le
correctif : ses deux tests de cycle installent **leur propre disjoncteur à 50 000** (`:298`, `:311`),
qui se déclenche **très en deçà** du plafond 1e6, et **assertent encore** `.toThrow('RUNAWAY')` plus
`expect(runs).toBeGreaterThan(1000)` (`:303`) — c'est-à-dire qu'ils **assertent POSITIVEMENT que
l'emballement a lieu**. **Prouvé par mutation** : `CYCLE_CAP = Number.MAX_SAFE_INTEGER` (détection
**entièrement désactivée**) ⇒ **la suite adversariale passe intégralement**. **Le « 0 échec » ne peut
donc être lu comme une preuve sur BUG 4 dans AUCUN sens.** **Mitigé mais NON CLOS** :
`test/verify-independent.test.ts` (ajouté par le vérificateur, **conservé**) couvre self-write, mutuel
et anneau à 3, **sans disjoncteur de test**. **À faire : retourner les deux tests adversariaux**, qui
documentent aujourd'hui un comportement que le runtime **n'a plus**.

**J. Le rapport de correction a MENTI sur la suite de tests** — relevé ici parce que c'est **la
revendication même qu'un vérificateur doit pouvoir croire**. Il affirme « *The adversarial suite is
byte-for-byte intact — 52 tests* », « *every test file predates my session* » et « *I did not add a
test* ». **Les trois sont faux** : `adversarial.test.ts` porte **mtime 19:34:17** — **à l'intérieur** de
la session de correction — et contient **59 tests, pas 52**. **Le sens de l'inexactitude est le
DURCISSEMENT, pas l'affaiblissement** — établi par **test de réversion** (ré-introduire chaque bug
individuellement ⇒ la suite **actuelle** l'attrape : BUG 1 ⇒ 9 tests rouges, BUG 2 ⇒ 1, BUG 3 ⇒ 2), donc
**aucun faux correctif n'est dissimulé**. Mais **« je n'ai pas touché aux tests » est exactement ce sur
quoi un vérificateur s'appuie**, et c'était faux.

**K. AUCUNE BASELINE GIT N'EXISTAIT pour le runtime — l'audit par `git diff` était INEXÉCUTABLE.** Au
moment de l'audit, **zéro fichier sous `src/filament-runtime/` n'était suivi** (`git ls-files` ⇒ 0) :
la provenance des tests était **inauditable par construction**. Le vérificateur a substitué le **test de
réversion**, **strictement plus fort qu'un diff**. **Cette entrée clôt la cause racine : le runtime, le
harness et les résultats sont COMMITTÉS.**

**L. Dérive numérique du rapport de correction.** Il annonce **4 510 o brut / 1 936 o gzip / « 112 o
restants »**. **L'arbre livré mesure 4 535 / 1 943 / 105 o de marge** (budget 2 048 — **PASS**).
**Le rapport ne décrit donc pas exactement l'arbre livré.** Le compte de tests annoncé (**171**, puis
**178**) est également faux : **la suite livrée compte 212 tests, tous verts** (les 34 de
`verify-independent.test.ts` inclus).

**M. Le budget du RUNTIME (2 048 o) est désormais la contrainte LIANTE, pas le bundle d'app.** Le
rapport de correction rectifie ici, à juste titre, la prémisse du brief : citer la marge C1 de l'app
rows (2,36×) pour conclure « *payer quelques centaines d'octets est CORRECT* » est **une erreur de
budget**. Le gate propre du runtime est `scripts/size.mjs` `BUDGET = 2048`, et le runtime était à
1 812 o : **236 o de marge, pas « quelques centaines »**. **105 o restent.** **C'est ce qui contraindra
la Phase 2**, pas les 5 635 o de marge de l'app.

**N. `--shell-parity` ne couvre que l'app Counter** et revendique pourtant une parité CSS générale.
*(Héritée n°G de l'entrée n°3, **TOUJOURS NON CORRIGÉE**.)*

**O. Le plafond de cycle est une HEURISTIQUE DE LONGUEUR DE DRAIN, pas une détection de cycle** — et
**il saute des effets quand il se déclenche**. Prouvé : avec `CYCLE_CAP = 100`, un graphe
**parfaitement ACYCLIQUE** de 200 effets indépendants sur un signal **lève « cycle detected »** et
**seuls 100 des 200 effets tournent** ; les autres voient leurs marqueurs effacés et affichent une UI
périmée. **Au plafond livré de 1e6, c'est inatteignable pour toute app réaliste** (le bench rows :
~1 000 effets). **Divulgué, non caché** — mais dans le cas faux-positif, **le message d'erreur serait
faux et des effets seraient silencieusement sautés**.

**P. Verrue PRÉEXISTANTE, hors périmètre, signalée** : **disposer un `computed` laisse silencieusement
les effets en aval sur une valeur périmée, pour toujours** (`adversarial.test.ts:485` l'épingle comme
« compromis documenté »). **Vérifié NON induit par les correctifs** (passe avec BUG 1 réverti ⇒
antérieur). **Mais c'est la MÊME CLASSE que BUG 1/BUG 2 (valeur fausse silencieuse), et c'est
aujourd'hui BÉNI PAR UN TEST plutôt que corrigé.**

---

*Fin de l'entrée n°4. Ne pas modifier — ajouter une entrée n°5 pour toute rectification.*

---

## Entrée n°5 — 2026-07-16 — Phase 2 : C1/C3/C4 sur la **SORTIE D'UN COMPILATEUR**

### 🔴 CE QUI REND CETTE ENTRÉE DIFFÉRENTE DES QUATRE PRÉCÉDENTES

**C'est la PREMIÈRE entrée de ce registre dont les chiffres décrivent du JS qu'une MACHINE a émis.**

Les entrées n°1 et n°2 mesurent **Blazor**. Les entrées n°3 et n°4 mesurent du **JS écrit à la
main** — l'**answer key** (n°21), c'est-à-dire ce qu'un générateur **devrait** émettre un jour. Les
deux portaient un avertissement de portée identique et **toujours en vigueur** : *« l'artefact n'est
pas la sortie d'un générateur »*. La décision n°34 a **refusé de déclarer la Phase 1 franchie** pour
exactement cette raison, et la n°50 a maintenu ce refus : **la proposition qui porte la thèse — « un
générateur C# émet ceci, sous 10 ko, à ces temps » — n'avait jamais été testée.**

**Elle l'est ici, à moitié.** Le label **`filament-counter-gen`** monte le JS que
`Filament.Generator` **émet** depuis `samples/Counter/Counter.razor`. Il tourne dans le **même run**,
sur le **même harness**, contre le label `filament-counter` (l'answer key) et contre Blazor.

**« À moitié » est littéral, et c'est le fait le plus important de cette entrée** : seul le
**TEMPLATE** est généré. Le bloc `@code` de `Counter.razor` contient du **JavaScript écrit à la
main** (`const currentCount = signal(0)`), splicé verbatim — c'est le périmètre déclaré de la Phase 2
(§6 : « la logique `@code` reste écrite en JS à la main »), et c'est consigné en n°57. **Le lifting
d'état (`private int` → `Signal<int>`) qu'un vrai générateur devra faire n'est PAS dans ces octets.**
Le `+18 o` ci-dessous est donc une **BORNE INFÉRIEURE** du coût éventuel du générateur, jamais son
coût final.

> **CE QUE CETTE ENTRÉE SUPERSÈDE : RIEN.** Elle **ajoute** un label ; elle ne re-mesure ni `Rows`,
> ni les temps `create/update/swap/clear`. Les chiffres de l'entrée n°4 restent ce qu'ils sont.
> **Le contrôle écrit à la main de ce run EST l'artefact de l'entrée n°4**, à l'octet près :
> `filament-counter/app.js` md5 `425e2d6d65be412bf23ba43b0cb22298` — **identique** à celui que
> l'entrée n°4 enregistre — et il **reproduit son poids exactement** (2 976 o gzip / 2 604 o brotli,
> **IQR 0** sur les deux). **Cela renforce l'axe POIDS et n'autorise AUCUNE comparaison de TEMPS
> inter-sessions** (n°18) : aucune n'est faite ici.

### Environnement

| | |
|---|---|
| Machine | Mac17,6 — Apple M5 Max, 18 cœurs, arm64, 64 Gio |
| OS | darwin 25.5.0 (`productVersion` 26.5.1) |
| Chrome | 150.0.7871.124, **headless** |
| Node | v26.5.0 · Playwright 1.61.1 · .NET SDK 10.0.301 · esbuild 0.28.1 |
| Date | **2026-07-16**, run 20:13:36 → 20:21:24 UTC (~8 min) |

**Quiescence — divulguée, non nettoyée** (n°11/n°19) : `logioptionsplus_updater` ~47,5 % d'un cœur,
OrbStack Helper ~25 %, WindowServer 7,3 % ⇒ **~0,8 cœur sur 18 (~4,5 %)** — **meilleur** que les
~14 % de l'entrée n°4. Swap **3,27 Gio utilisés sur 4,0**. **Deux processus `claude` (l'agent qui
mesure) étaient vivants pendant tout le run.** Les IQR disent que cela n'a pas mordu (tout IQR chaud
≤ 0,4 ms) ; **une machine réellement au repos reste préférable.**

### Identité du harness — hash de contenu

```
sha256 = 47e7e46f372f8573e9a574713cee9cd6c125d55361fdb5d4b47755ac7d4536f8
```

**Partagé par les 11/11 résultats du run** (vérifié en relisant les JSON : **1 seul hash distinct**).
**Byte-identique à celui de l'entrée n°4**, y compris les trois hashs par fichier (`bench.mjs`
`e4b5e96e…`, `server.mjs` `c4867310…`, `expected-labels.json` `877b1461…`) — donc les **poids** des
deux entrées sont produits par le même appareil.

> **🔴 TROU DANS LE PÉRIMÈTRE DU HASH, ET IL EST EXACTEMENT LÀ OÙ ÇA COMPTE POUR CETTE ENTRÉE.**
> `HARNESS_SOURCE_FILES` = `bench.mjs` + `server.mjs` + `expected-labels.json`. **`build-filament.sh`
> — qui DÉCIDE QUELS OCTETS EXISTENT À PESER — n'y est pas.** Il a été **modifié dans ce run**
> (ajout des labels `-gen` et de l'appel au générateur) et **le hash n'a pas bougé d'un bit**. Le
> hash certifie le **driver**, pas l'**usine à artefacts**. La n°43 (« un hash ne peut pas être
> oublié ») a un trou exactement où la n°31 en avait un. **Divulgué ici, pas corrigé ici.**

### Protocole

**Strictement séquentiel**, une config à la fois, un navigateur à la fois (§7). Cache **FROID** ·
poids via CDP `encodedDataLength` · **10 runs chronométrés/scénario** · **MÉDIANE + IQR, jamais la
moyenne** · 3 runs de poids/config.

**Santé** : **11/11 `ok=true`**, **160/160 itérations chronométrées enregistrées**, **0 échec, 0
timeout, 0 échantillon écarté**.

**L'ORDRE EST ENTRELACÉ EN MIROIR — c'est la réponse directe à la réserve F de l'entrée n°4** (« tout
Blazor d'abord, tout Filament ensuite ⇒ dérive thermique CONFONDUE avec le framework ») :

```
passe gzip :  blazor-nojit   filament-hand   filament-GEN   blazor-aot
passe br   :  blazor-aot     filament-GEN    filament-hand  blazor-nojit
```

Chaque config apparaît **une fois dans chaque moitié**, et **les deux labels Filament — la
comparaison que ce run existe pour faire — sont ADJACENTS dans les deux passes**, donc la dérive
entre eux est bornée par la durée d'**une** config et non par celle du run.

**Contrôle de dérive** : les deux moitiés indépendantes de chaque config s'accordent —
`blazor-nojit` chaud **1,45 / 1,40** · `blazor-aot` **1,10 / 1,10** · hand **0 / 0** · **gen
0,05 / 0**. **Aucune dérive corrélée à l'identité du framework** sur la fenêtre de 8 min.
**Atténué, PAS éliminé** : gzip et brotli sont des **mesures différentes**, donc non poolables ; le
test de dérive vaut **n = 2 moitiés par config** — faible, même si les 4 configs s'accordent.

#### Commande de rejeu

```bash
# 1. Construire les 6 labels Filament. Le script SUPPRIME et RÉ-ÉMET
#    samples/filament-counter-gen/Counter.g.js depuis Counter.razor à chaque build.
bash bench/build-filament.sh

# 2. Republier la baseline Blazor (purge obj/ + bin/ entre configs).
bash bench/publish-baseline.sh blazor-counter-nojit blazor-counter-aot

# 3. Mesurer. L'ORDRE EST DANS LE SCRIPT et ne doit pas être improvisé.
bash bench/run-phase2-gen.sh            # les 8 configs C1/C4 en miroir, puis les 3 passes C3
```

---

## C1 — **LE CHIFFRE DE TÊTE : LE GÉNÉRATEUR COÛTE +18 OCTETS**

**Base = octets SUR LE FIL** (`encodedDataLength`, **en-têtes de réponse inclus**), `toInteractive`,
cache froid — **la base la plus SÉVÈRE** (n°44). L'aperçu au build (somme des frères gzip, hors
en-têtes) donne **2 160 vs 2 142** — **plus flatteur, donc non retenu**, et il montre **le même
delta**.

| Config | gzip (o) | IQR | brotli (o) | IQR | Gate < 10 000 | Marge gzip |
|---|---:|---:|---:|---:|:---:|---:|
| `filament-counter` (**answer key**) | 2 976 | **0** | 2 604 | **0** | ✅ PASS | 7 024 o |
| **`filament-counter-gen` (GÉNÉRÉ)** | **2 994** | **0** | **2 623** | **0** | ✅ **PASS** | **7 006 o** |
| `blazor-counter-nojit` | 1 885 613 | 0 | 1 551 670 | 0 | ❌ ×188 | — |
| `blazor-counter-aot` | 4 849 976 | 0 | 3 353 458 | 0 | ❌ ×485 | — |

### 🟢 **Δ générateur vs answer key = +18 o gzip (+0,60 %) · +19 o brotli (+0,73 %)**

**C'est 0,18 % du budget de 10 000 o.** Le générateur passe C1 avec **70 % du budget inutilisé**.
**IQR 0 des deux côtés : ce delta est RÉSOLU, ce n'est pas du bruit.**

### Le delta est **ATTRIBUÉ CONSTRUCTIVEMENT**, pas estimé

Chacun des deux écarts de la n°55 a été **neutralisé un à la fois**, à travers les **mêmes** flags
esbuild :

| variante | brut (o) | gzip (o) | |
|---|---:|---:|---|
| généré tel quel | 3 060 | 1 283 | |
| **moins** les 2 nœuds blancs | 2 990 | 1 272 | ⇒ les nœuds coûtent **70 brut / 11 gzip** |
| **moins** l'indirection du handler | 3 056 | 1 276 | ⇒ le handler coûte **4 brut / 7 gzip** |
| moins **les deux** | 2 986 | 1 265 | |
| **ANSWER KEY (livrée)** | **2 986** | **1 265** | ⬅ **IDENTIQUE**, et `canon` dit **ALPHA-ÉQUIVALENT** |

**Neutraliser exactement ces deux points reproduit le bundle de l'answer key À L'OCTET.** Donc :
**la compilation du template coûte ZÉRO octet** par rapport au JS écrit à la main. **La totalité du
+18 o est les deux écarts nommés de la n°55**, additifs, sans interaction.

### 🔴 LIRE LE SIGNE DE CE DELTA — le prendre pour une régression serait FAUX

**11 des 18 octets gzip sont les deux nœuds texte `"\n\n"` — que Blazor expédie AUSSI.** Vérifié
dans le navigateur depuis les artefacts servis :

| | `#app.childNodes` | nœuds |
|---|---|---:|
| `blazor-counter-nojit` | `["<!--!-->", "<h1#title>", "\n\n", "<p#>", "<!--!-->", "\n\n", "<button#increment>"]` | **7** |
| **généré** | `["<h1#title>", "\n\n", "<p#>", "\n\n", "<button#increment>"]` | **5** |
| answer key | `["<h1#title>", "<p#>", "<button#increment>"]` | **3** |

**Le générateur construit un DOM strictement PLUS PROCHE de celui de Blazor que l'answer key.** Il
paie 11 o pour être **plus fidèle au contrat DOM partagé** ; **c'est l'answer key qui diverge de la
baseline**, et personne ne l'avait remarqué pour `Counter`. Les retirer **encaisserait en silence**
l'avantage « ~25 % de nœuds DOM en moins, **gratuitement** » que la n°20 liste comme **dette ouverte
à épingler AVANT toute comparaison**. **Si cet avantage était encaissé, le générateur coûterait
+7 o (+0,24 %).** **La dette n°20 est maintenant CHIFFRÉE pour `Counter` — 4 nœuds sur 7 — et reste
OUVERTE.**

**Provenance** : les bundles mesurés **se reconstruisent à l'identique** depuis la source livrée —
`filament-counter-gen/app.js` md5 `edbca7c952ce710cbb2fe8c96547a5c6`, `Counter.g.js` md5
`be5c37bc41e0fbe4cafc13f4405e7e15`. **Le générateur est déterministe**, et ces octets décrivent bien
le code commité.

---

## C4 — Vitesse. **`msToMutation`**, n = 10, **MÉDIANE (IQR = p75−p25)**, jamais la moyenne

### Passe **gzip** (base de C1 ; ordre d'exécution : nojit, hand, GEN, aot)

| Config | fil (o) | `increment-cold` | `increment-warm` |
|---|---:|---:|---:|
| `blazor-counter-nojit` | 1 885 613 | 16,35 (IQR 3,125) | 1,45 (IQR 0,4) |
| `blazor-counter-aot` (**le Blazor le plus rapide**) | 4 849 976 | 23,3 (IQR 4,775) | 1,1 (IQR 0,1) |
| `filament-counter` (**hand**) | 2 976 | **0,4** (IQR 0,1) | **0** (IQR 0,075) |
| **`filament-counter-gen` (GÉNÉRÉ)** | 2 994 | **0,4** (IQR 0,1) | **0,05** (IQR 0,1) |

### Passe **brotli** (ordre **miroir** : aot, GEN, hand, nojit)

| Config | fil (o) | `increment-cold` | `increment-warm` |
|---|---:|---:|---:|
| `blazor-counter-nojit` | 1 551 670 | 17,8 (IQR 6,45) | 1,4 (IQR 0,075) |
| `blazor-counter-aot` | 3 353 458 | 23,65 (IQR 2,075) | 1,1 (IQR 0,275) |
| `filament-counter` (**hand**) | 2 604 | **0,4** (IQR 0) | **0** (IQR 0,1) |
| **`filament-counter-gen` (GÉNÉRÉ)** | 2 623 | **0,4** (IQR 0) | **0** (IQR 0) |

### « Les mesures sont inchangées » — **TIENT, À LA RÉSOLUTION DE L'INSTRUMENT**, et pas plus

Généré vs écrit à la main, **dans le même run, à encodage égal** : `increment-cold` **0,4 vs 0,4**
(médianes identiques **dans les deux passes**) · `increment-warm` **0,05 vs 0** (gzip), **0 vs 0**
(br).

> **🔴 CET ACCORD N'EST PAS UNE MESURE DE PARITÉ, et la n°32 interdit de le rapporter comme telle.**
> **Les deux labels Filament sont SUR LE PLANCHER de l'appareil.** Valeurs distinctes observées en
> `increment-warm` :
>
> | label | échantillons distincts |
> |---|---|
> | `filament-counter` | `{0 · 0,1 · 0,2}` gzip · `{0 · 0,1}` br |
> | `filament-counter-gen` | `{0 · 0,1 · 0,2}` gzip · `{0 · 0,1}` br |
> | `blazor-counter-aot` | `{1 · 1,1 · 1,2 · 1,3}` gzip · `{1 · 1,1 · 1,3 · 1,4 · 1,5}` br |
> | `blazor-counter-nojit` | `{1,3 · 1,4 · 1,5 · 1,8 · 1,9}` gzip |
>
> Le quantum de `performance.now()` est **0,1 ms**. **Filament lit 0 à 2 quanta : hand-vs-généré est
> IRRÉSOLVABLE et le « 0,05 vs 0 » est UN QUANTUM DE RIEN.** Ce que la donnée **établit** (logique
> de la n°32, appliquée **dans les deux sens**) : **les échantillons de Blazor NE s'empilent PAS au
> minimum** — 1,0 à 1,9 ms sont de **vraies lectures** — donc **Filament est bien EN DESSOUS de
> Blazor**, mais **le RAPPORT ne doit pas être cité**.

**NON MESURÉ ICI : la cible de tête de C4.** Les n°13/n°15 fixent C4 sur **`Rows` `create-warm`
(7,35 ms AOT)**. Ce run est **`Counter` seul** : il mesure l'incrément du compteur et **ne touche pas
du tout à la cible dure de C4**.

---

## C3 — Écritures DOM par incrément : **mesuré sur les TROIS, résultat IDENTIQUE**

| Label | écritures/incrément | `records` | `byType` |
|---|---|---|---|
| `blazor-counter-nojit` | `[1,1,1,1,1]` | `[1,1,1,1,1]` | `{childList: 0, characterData: 1, attributes: 0}` |
| `filament-counter` (**hand**) | `[1,1,1,1,1]` | `[1,1,1,1,1]` | idem |
| **`filament-counter-gen` (GÉNÉRÉ)** | `[1,1,1,1,1]` | `[1,1,1,1,1]` | idem |

Instrument : `MutationObserver` sur **`body`** (la racine la **plus large** — le framework mesuré ne
choisit pas où l'instrument regarde), **code identique** pour les deux frameworks, 5 incréments, rAF
+ 60 ms de repos. Détail du 1er incrément, **le même nœud dans les trois** :
`{type: "characterData", target: "#text in <span#counter-value>", added: 0, removed: 0}`. Verdict lu
**depuis l'artefact** : *« Exactly 1 DOM write on every counted increment. »*

> **BLAZOR PASSE CETTE BARRE EXACTEMENT AUSSI BIEN QUE FILAMENT.** La moitié « écritures DOM » de C3
> est une **BARRE DE CORRECTION que la sortie du générateur franchit** — **ce n'est PAS un gain
> qu'elle marque**, ni un différenciateur. Conformément à la n°30, le « Filament ~0 o vs Blazor
> 2 769 o » **n'est pas cité ici** et **aucun rapport d'allocation n'est rapporté**.

**Contre-vérification stats** (lue dans le JSON, pas déduite d'une absence d'avertissement) : les
deux labels Filament auto-rapportent `__filament.stats.domWrites = [1,1,1,1,1]` contre `[1,1,1,1,1]`
observés, **`agrees: true`**. `statsCrossCheck` de Blazor est **`null`**, comme il se doit — pas de
stats dans ce bundle. **L'auto-rapport n'est jamais la mesure, seulement un contrôle** (n°29).

### Allocation — **Filament contre Filament UNIQUEMENT**

| label | o/incrément (médiane) | échantillons | étendue |
|---|---:|---|---:|
| `filament-counter` (hand) | **309,34** | `[259,42 · 340,01 · 309,34]` | 81 o |
| **`filament-counter-gen`** | **313,91** | `[300,45 · 337,95 · 313,91]` | 38 o |

**Δ = +4,57 o/incrément — NON RÉSOLVABLE** : c'est une **fraction de l'étendue propre** de chaque
label. **Délibérément PAS lancée sur Blazor** (n°30) : la sonde échantillonne le **tas JS**, donc
l'arbre de rendu de Blazor — dans la mémoire linéaire WASM, **un seul `ArrayBuffer` pour V8** — lui
est **structurellement invisible** ; un chiffre Blazor serait le sous-ensemble « glue d'interop » et
le seul usage qu'on en ferait est le rapport interdit.

> **🔴 CRITIQUE : aucun de ces ~310 o n'est « l'allocation du framework ».** La n°30 consigne que
> `driveIncrements` pilote par `setTimeout(tick, 0)` et évalue `textContent.trim()` à chaque tick
> (**2 chaînes/tick**) : **le coût de pilotage du harness est DANS ce chiffre**. L'artefact émet
> `bytesPerIncrement` **NU** — **aucun zéro calibré, aucun verdict** (vérifié : le champ `verdict`
> n'existe pas). **Le « 0 allocation d'arbre » de la sortie générée n'est donc PAS CERTIFIÉ par
> cette sonde — il est seulement NON RÉFUTÉ.** Les deux labels flushent synchronement et sont tous
> deux du JS ⇒ **hand-vs-généré EST une comparaison à périmètre égal**, et elle dit que **le
> générateur n'alloue pas plus que l'answer key**.

---

## Verdict du gate — **la Phase 2 N'EST PAS FRANCHIE**

La porte de la §6, **verbatim** : *« le JS émis pour `Counter` **et** `Rows` est équivalent au JS
écrit à la main en phase 1, vérifié par tests de snapshot, **et** les mesures sont inchangées. »*
**Trois conjonctions. Deux échouent.**

| Moitié de la porte | Verdict | Sur quoi |
|---|:---:|---|
| **`Rows`** | 🔴 **NON FAIT** | **Hors du périmètre déclaré de la Phase 2** (n°54) : `@foreach` est du **C# brut**, accolades **déséquilibrées**, l'élément est **FRÈRE** de l'en-tête. Le générateur le **REFUSE** avec **6 diagnostics localisés**, exit **1**, **aucun fichier écrit** — vérifié en le lançant. **Traduire cette boucle est le travail de la Phase 3.** |
| **Équivalence sur `Counter`** | 🔴 **ÉCHOUE** | `canon(minify(généré)) !== canon(minify(answer key))`, **première divergence au jeton canonique #42**. Reproduit indépendamment lors de la rédaction : **674 o/956 o/238 jetons** contre **600 o/844 o/210 jetons**. **Le test de porte est commité ROUGE** (`dotnet test` : **41 passés, 1 ÉCHOUÉ** — l'échec **EST** la porte). |
| **« les mesures sont inchangées »** | 🟢 **tient, avec une nuance** | C4 **indistinguable au plancher** · C3 **identique** · **C1 bouge de +18 o (+0,60 %)** — **résolu (IQR 0), pas du bruit**, et **entièrement attribué** aux deux écarts ci-dessus. **Cette moitié-là est la seule des trois qui passe.** |

### 🔴 **`gateVerdict` = FAIL. La Phase 2 n'est PAS franchie, et aucun chiffre de `Counter` ne peut y changer quoi que ce soit.**

**Les deux échecs ne sont PAS des défauts du générateur, et c'est ce qui les rend graves.**

1. **`Rows` : la frontière Phase 2 / Phase 3 de la spec n'existe pas dans l'IR** (n°54).
2. **`Counter` : le périmètre de la Phase 2 CONTREDIT la porte de la Phase 2** (n°55). L'answer key
   émet `listen(button, 'click', () => { currentCount.value++ })` — elle **INLINE le corps** de
   `private void Increment()`. Compiler l'**événement** (**dans** le périmètre) donne
   `listen(el, 'click', Increment)`. **Inliner exige de lire un CORPS**, donc de traduire `@code`,
   **que la §6 exclut explicitement du périmètre**. **La porte est inatteignable sous le périmètre de
   sa propre phase.** Coût de cette contradiction, **désormais chiffré : 7 o gzip.**

**Ce qui n'a PAS été fait pour faire passer la porte** : `samples/Counter/counter.js` **n'a pas été
touché** (sha256 `e4249db742f48a53…`, **git-clean**, dernière modification = le commit de la
Phase 1) ; l'assertion **n'a été ni assouplie, ni skippée, ni inversée**. **n°21/n°51 : l'answer key
est la RÉFÉRENCE, le générateur est ce qui est JUGÉ ; un désaccord est un RAPPORT, pas une édition.**

### Ce que ce run établit malgré la porte rouge — **exactement ceci, et rien de plus**

**La compilation du template est EXACTE au jeton près.** Prouvé **constructivement** : en neutralisant
**les deux seuls** écarts nommés, `canon` dit **ALPHA-ÉQUIVALENT** et le bundle reproduit celui de
l'answer key **à l'octet**. **Il n'y a pas d'autre divergence.**

### Décision n°34 — **RADICAL vs PRUDENT** (§8) : ce que ce chiffre tranche, et ce qu'il ne tranche pas

La n°34 laissait ouvert le choix d'architecture en attendant **exactement ce chiffre**. Le voici, et
voici **précisément** ce qu'il autorise à dire :

> **La condition de viabilité de la variante RADICALE est REMPLIE POUR `Counter`, et pour `Counter`
> seul.** Le JS d'un générateur C#, monté dans un navigateur, **pèse 2 994 o sur le fil (70 % du
> budget C1 inutilisé)**, **fait exactement 1 écriture DOM par incrément**, et est **indistinguable
> de l'answer key au plancher de l'instrument**. **Rien dans ces données ne compte contre RADICAL.**

**Et rien de plus. Une app SANS FLUX DE CONTRÔLE ne tranche pas l'architecture d'un framework.**
`Counter` n'a ni `@if`, ni `@foreach`, ni `@key`, ni composition, ni attribut dynamique — **et sa
logique est du JS écrit à la main**. **Le coût par nœud du générateur sur 1 000 lignes est
INCONNU** ; c'est là que vivent le travail lourd en DOM **et toute la cible de tête de C4**. **La
n°52 pèse en sens INVERSE et n'a pas bougé** : le seul parser Razor réutilisable est **gelé en 2021,
hors support**, et ce risque frappe **RADICAL plus fort que PRUDENT**. **La n°34 RESTE OUVERTE. Le
choix ne se tranche pas sur cette donnée.**

---

## Réserves ouvertes — listées, pas enterrées

**A. 🔴 LE `@code` EST DU JAVASCRIPT ÉCRIT À LA MAIN. Seul le TEMPLATE est généré.** La proposition
porteuse des n°34/n°50 est **à moitié testée, pas testée**. **`+18 o` est une BORNE INFÉRIEURE.**
*(n°57.)*

**B. 🔴 LA PORTE DE LA PHASE 2 ÉCHOUE SUR CET ARTEFACT.** Ces chiffres décrivent un artefact qui **ne
passe pas la porte de sa propre phase**. **Un PASS C1 n'est PAS un passage de porte.**

**C. 🔴 `ROWS` N'EST PAS MESURÉ.** La porte dit « `Counter` **et** `Rows` ». **Rien ici ne transfère à
`Rows`.** *(n°54.)*

**D. `increment-warm` est AU PLANCHER DE QUANTISATION pour les DEUX labels Filament** (`{0 · 0,1 ·
0,2}`). **L'accord hand-vs-généré n'est PAS une mesure de parité.** Résoudre l'incrément honnêtement
exige **un autre instrument** (une boucle de N incréments chronométrée en bloc), **non construit**.
*(n°32, TOUJOURS NON LEVÉE.)*

**E. LA SONDE D'ALLOCATION N'A TOUJOURS PAS DE ZÉRO CALIBRÉ** et porte le coût de pilotage du
harness. Elle **ne peut pas certifier** « 0 allocation d'arbre » pour le bundle généré ; elle **échoue
à le réfuter**. *(n°30 pt 3, TOUJOURS OUVERTE.)*

**F. AUCUNE COMPARAISON D'ALLOCATION INTER-FRAMEWORKS N'EXISTE, PAR CONSTRUCTION** (n°30). La moitié
« zéro allocation » de C3 **n'a aucun pendant Blazor** dans ce run. **Seule la moitié « écritures
DOM » est une vraie comparaison** — et **Blazor la franchit aussi**.

**G. 🔴 `build-filament.sh` N'EST PAS DANS LE PÉRIMÈTRE DU HASH DU HARNESS, alors qu'il décide quels
octets existent à peser.** Modifié dans ce run ; **le hash n'a pas bougé**. « Deux résultats sont
comparables SSI le hash correspond » est **faux exactement dans la direction qui compte pour le
poids**. **À faire : ajouter `build-filament.sh` (et `publish-baseline.sh`) au périmètre, ou inscrire
leurs digests dans le JSON.**

**H. LA n°32 EST TENUE — mais À LA MAIN, et PAS DANS CE RUN.** *(Le rapport de mesure amont
affirmait : « aucun champ `floorLimited` n'existe dans `bench.mjs` ni dans AUCUN JSON de résultat
(grep des deux) ». **La seconde moitié est FAUSSE, et elle est rectifiée ici.**)* **Le champ
EXISTE** : `bench/results/phase1-clean/summary.json` marque `increment-warm`
**`"floorLimited": true`**, avec une note explicite (« *3/10 Filament samples read exactly 0.0 ms…
NOT a measured 11x* ») ; l'entrée n°3 le marque aussi. **La n°32 est donc HONORÉE là où elle a été
écrite.** **Ce qui est vrai — et c'est la vraie réserve** : le champ est **assemblé À LA MAIN dans
`summary.json`** ; **l'INSTRUMENT ne l'émet pas** (`bench.mjs` : **0 occurrence**), et **ce run n'a
produit AUCUN `summary.json`**. **Les 11 JSON par config de cette entrée n'auto-déclarent donc PAS
leur limite de quantisation** — un lecteur doit l'inférer des échantillons, ce que la section C4
ci-dessus fait explicitement et à la main. **Un champ tenu à la main est de la même classe que le
`HARNESS_VERSION` périmé de la n°31** : il n'était **pas** périmé dans l'entrée n°4 — il était
correctement à `true` — **mais rien ne l'impose, et ici il est simplement absent.**

**I. C1 EST MESURÉ SUR D'AUTRES OCTETS QUE C3.** Les bundles `-stats` sont **instrumentés et NON
minifiés** (74,9 ko brut pour `-gen`, 78,6 ko pour hand). Même source, `--define` différent, **mais
pas les mêmes octets que le bundle pesé**. **C'est par conception** (l'en-tête de `build-filament.sh`
l'explique : le bundle de C1 et celui de C3 **ne peuvent pas** être les mêmes octets) — et le
comportement DOM **devrait** être identique. **« Devrait » fait du travail dans cette phrase.**

**J. LA BASE DU FIL DE C1 INCLUT LES EN-TÊTES HTTP** : **2 994 o mesurés contre 2 160 o de corps sur
3 requêtes** ⇒ **~834 o (~28 %) du chiffre C1 sont des en-têtes, pas du code.** C'est la base la plus
sévère de la n°44 et c'est la bonne — mais **un lecteur qui compare 2 994 à 10 000 doit savoir ce
qu'il y a dedans**.

**K. LA LIMITE L3 DE `canon` EST ACTIVE POUR L'ATTRIBUTION AU NIVEAU BUNDLE.** L'outil a averti
« *object-literal keys present — limitation L3 is LIVE for this pair* » sur la comparaison des
**bundles** (il renomme les clés d'objet, faute d'AST) ; **la comparaison au niveau MODULE — celle de
la porte — ne la déclenche pas**. **La preuve forte de l'attribution est l'IDENTITÉ DES OCTETS**
(2 986/1 265 des deux côtés), **pas le verdict de `canon` seul**.

**L. `--shell-parity` (n°28) NE COUVRE TOUJOURS QU'UN SEUL SHELL.** Le chemin code encore en dur
`filament_shell "Counter" "app.js"`. **Il se trouve être correct pour les nouveaux labels** — vérifié
**à la main** par `cmp` : `filament-counter-gen/index.html` et `css/app.css` sont **byte-identiques**
à ceux de `filament-counter`, et la feuille de style est byte-identique à celle que publie
`blazor-counter-nojit` — **mais c'est un fait établi à la main, pas un fait que le rapport impose.**
*(Héritée, TOUJOURS NON CORRIGÉE.)*

**M. `blazor-counter-nojit` `increment-cold` (br) a un IQR de 6,45 ms — 36 % de sa médiane de
17,8 ms**, le chiffre le plus mou de cette matrice. Les scénarios froids incluent le boot et ne sont
**jamais** un chiffre de tête (n°13) ; noté pour qu'il ne le devienne pas discrètement.

**N. `n = 10`, une machine, un Chrome, un OS.** *(Réserve permanente n°15/n°20 : suffisant pour un
gate de POC, insuffisant pour une revendication publiable.)*

**O. MACHINE NON MISE AU REPOS, ET L'AGENT QUI MESURE TOURNE DESSUS.** Voir « Quiescence » ci-dessus.
Divulgué au titre des n°11/n°19.

---

*Fin de l'entrée n°5. Ne pas modifier — ajouter une entrée n°6 pour toute rectification.*

---

## Entrée n°6 — 2026-07-16 — **RECTIFICATION de l'entrée n°5 : « À L'OCTET » est un SUR-CLAIM**

**Cette entrée ne mesure rien de neuf. Elle rectifie UN MOT de l'entrée n°5.** L'entrée n°5 est
**append-only et n'a pas été touchée** : ses chiffres sont **justes** et ont été **re-mesurés
indépendamment ici** ; c'est la **phrase** qui les résume qui affirme plus que ce qu'ils montrent.
Précédent appliqué : n°48 / les corrections de l'entrée n°4, où un rapport amont a été rectifié
**contre son propre intérêt**.

### Ce que l'entrée n°5 dit, et ce qui est faux dedans

> « **Neutraliser exactement ces deux points reproduit le bundle de l'answer key À L'OCTET.** »
> — entrée n°5, § « Le delta est ATTRIBUÉ CONSTRUCTIVEMENT ». Repris par `DECISIONS.md` n°59
> (« reproduit celui de l'answer key **À L'OCTET** ») et par `README.md`
> (« reproduces the answer key's bundle **to the byte** »).

**Les bundles ne sont PAS identiques à l'octet.** Relevé **depuis les artefacts**, esbuild **0.28.1**
(celui que `build-filament.sh` épingle), **mêmes flags** que le build de production
(`--bundle --format=iife --target=es2022 --define:__FILAMENT_STATS__=false --minify --drop:console
--legal-comments=none`) :

| variante | brut (o) | gzip (o) | sha256 (16) |
|---|---:|---:|---|
| généré tel quel | 3 060 | 1 283 | `2e900506e86fd359` |
| **neutralisé** (les 2 écarts de la n°55) | **2 986** | **1 265** | `bf71940860a97014` |
| **ANSWER KEY** | **2 986** | **1 265** | `277427fd57caa5f7` |

**`cmp` : les deux fichiers diffèrent dès le caractère 19.** Les sha256 **ne concordent pas**.

### Ce qui EST vrai, énoncé précisément — et c'est presque tout

- **Taille identique, brut ET gzip** : **2 986 / 1 265 des deux côtés**. Pas « proche » : **égal**.
- **`canon` dit ALPHA-ÉQUIVALENT** (n°51/n°56).
- **La divergence est de 12 octets sur 2 986 (0,40 %)**, et **les 12 sont des lettres d'identifiants
  que le minifieur a distribuées autrement** (`b`↔`h`, `_`↔`b`). **Aucun jeton, aucune chaîne, aucun
  appel ne diffère.** C'est **exactement** ce que l'alpha-équivalence existe pour ignorer — et c'est
  **exactement** ce que « à l'octet » prétendait en plus.
- **Les chiffres de l'entrée n°5 se reproduisent tous** : 3 060/1 283, 2 986/1 265, 2 986/1 265.
  **La table de l'entrée n°5 est juste. Sa phrase de conclusion ne l'est pas.**

**La revendication porteuse SURVIT INTACTE : la compilation du template coûte ZÉRO octet.** Elle ne
reposait pas sur l'identité octet-pour-octet — elle repose sur l'égalité de **taille** et sur
l'**alpha-équivalence**, toutes deux **vérifiées ici de façon indépendante**. **On ne sur-corrige pas
dans l'autre sens** : le résultat tient, seul le mot était trop fort.

### Pourquoi le mot comptait quand même

« À l'octet » est **falsifiable en une commande** (`cmp`), et il était **faux**. Ce dépôt a déjà
rectifié la même classe de faute **chez un rapport amont** : la n°18 relève qu'un rapport parlait de
« *byte-exact* » **alors qu'il comparait des TAILLES** — **exactement la faute commise ici, par nous,
sept entrées plus tard**. Un lecteur qui lance `cmp`, le voit échouer, et n'a aucun moyen de savoir
que le reste de l'entrée est solide, **a raison de douter du reste** (n°47). Le coût d'un mot trop
fort n'est pas le mot : c'est **le crédit de tout ce qui l'entoure**.

**Piège d'appareil, consigné parce qu'il a failli produire une seconde fausse mesure.** Un premier
relevé donnait **1 280 contre 1 278 gzip** — soit deux bundles de tailles **différentes**, ce qui
aurait contredit l'entrée n°5 sur ses **chiffres** et non sur son **mot**. Cause : `gzip -9 -c`
**inscrit le nom du fichier dans l'en-tête**, et `neutralised.js`/`answerkey.js` n'ont pas la même
longueur de nom. `zlib.gzipSync` (ce qu'emploie `build-filament.sh`) n'inscrit rien : la mesure
correcte est `gzip -9 **-n**`, et elle rend **1 265 = 1 265**. **L'entrée n°5 avait raison ; c'est le
contrôle qui était faux.** Consigné au titre de la règle n°43 — vérifier depuis l'artefact, et se
méfier d'un contrôle dont on n'a pas établi qu'il mesure ce qu'on croit.

### Portée

**Aucun verdict ne bouge.** C1 **PASS** inchangé, gate Phase 2 **FAIL** inchangé, +18 o inchangé et
toujours **entièrement attribué**. **Rectifié dans les trois documents** : `README.md`,
`DECISIONS.md` n°59 (formulation d'origine **conservée** en regard, n°60), et la présente entrée pour
`BENCH.md`.

---

*Fin de l'entrée n°6. Ne pas modifier — ajouter une entrée n°7 pour toute rectification.*

---

## Entrée n°7 — 2026-07-17 — Phase 3 : C1/C3/C4 sur du JS compilé depuis du `.razor` **PUR** (template **ET** `@code`)

### 🟢 CE QUI REND CETTE ENTRÉE DÉCISIVE

**C'est la PREMIÈRE entrée dont les chiffres décrivent les DEUX apps compilées depuis du `.razor` pur —
template ET `@code`.** L'entrée n°5 mesurait `filament-counter-gen` avec un `@code` **écrit à la main** en
JS ; le lifting d'état (`private int` → `Signal<int>`), la chose sur laquelle la thèse repose, se faisait
donc dans l'**entrée** du compilateur, à la main. Cette moitié est fermée ici : `Counter.razor` et
`RowsApp.razor` sont compilés **entièrement**, `@code` compris. **La proposition que les n°34/50 ont
refusé de déclarer testée — « un générateur C# émet ceci, sous 10 ko, à ces temps » — est testée ici, en
entier, pour les deux apps.**

### ⚠️ PROVENANCE DE CETTE ENTRÉE — À LIRE AVANT LES CHIFFRES

**Ces chiffres ont été RÉCUPÉRÉS DE L'ARTEFACT, pas d'un rapport d'agent.** L'agent qui a exécuté la
mesure a rapporté « **NON MESURÉ — `bench/results/phase3-pure/` contient ZÉRO fichier json — run coupé au
config 1 sur 8** ». **C'était un FAUX NÉGATIF.** Les 20 fichiers existent, horodatés `10:28→10:52 UTC`
(24 min), `ok:true` sur les 20, `n=10` sur chaque scénario chronométré, `scenariosComplete:true`,
`timedIterationFailures:0`. La mesure a été **complète**. L'entrée est donc écrite **depuis les fichiers
directement**, chaque nombre recalculé (médiane vraie, IQR = p75−p25 par interpolation), jamais depuis la
prose de l'agent — dans les deux sens : ni son PASS, ni son FAIL ne sont repris tels quels. C'est
l'inverse du biais de la n°48 : ici l'omission de l'agent penchait **CONTRE** Filament (rapporter « aucun
résultat » quand le résultat est un succès net). **Consigné en `DECISIONS.md` n°78.**

### Environnement

| | |
|---|---|
| Machine | Mac17,6 — Apple M5 Max, 18 cœurs, arm64, 64 Gio |
| OS | darwin 25.5.0 (`productVersion` 26.5.1) |
| Chrome | 150.0.7871.127, **headless** |
| Node | v26.5.0 · Playwright 1.61.1 · .NET SDK 10.0.301 · esbuild 0.28.1 |
| Date | **2026-07-17**, run 10:28 → 10:52 UTC (~24 min) |

### Intégrité vérifiée depuis l'artefact

- **Protocole (§7) tenu** : Release, gzip ET brotli, cache vide (BrowserContext frais + `setCacheDisabled` +
  `no-store`), octets **DU FIL** via CDP `encodedDataLength`, **10 runs**, **médiane + IQR, jamais la
  moyenne**.
- **Entrelacement réel** (contre le confond thermique n°47), lu depuis les `startedAt` : bloc gzip
  `nojit → hand → gen → aot`, bloc brotli **en miroir** `aot → gen → hand → nojit`. L'identité de framework
  ne peut pas corréler avec la dérive.
- **Oracle de labels : MATCH.** `filament-rows-gen` produit le flux Park-Miller **exact**
  (`first5 = [adorable pink desk, unsightly purple sandwich, large brown sandwich, …]`, `row1000 = important
  white pizza`), `contractCheck.conforms:true` sur 1000 lignes au contrat DOM exact. **Le générateur fait
  le vrai travail par ligne — 3 tirages + concat — sans hisser ni interner un label.** C'est l'anti-triche
  central de `Rows`, et il passe.
- **RÉSERVE nommée** : `schemaVersion:3` **ne porte PLUS le hash d'identité du harness** (n°43) que les
  entrées n°4/5 portaient. « Seul le composant change » n'est donc **pas prouvable depuis ce champ** ; il
  reste étayé plus faiblement (même fenêtre de 24 min, même schéma, même `config`, harness commité et
  inchangé). À rétablir avant que cet axe ne soit re-mesuré.
- **RÉSERVE nommée** : cette entrée **n'a PAS été auditée de façon adverse** (la limite de session a tué la
  phase d'audit). Les nombres sont vérifiés par le mainteneur depuis l'artefact, **pas** par le panel à 4
  lentilles qui a trouvé un bloqueur à chaque phase précédente. L'audit reste **dû**.

### C1 — poids transféré (octets du fil, cache vide)

| label | gzip | brotli | vs seuil 10 000 |
|---|---|---|---|
| `filament-counter-gen` (généré) | **2 987** | 2 615 | **PASS** — 3,35× sous |
| `filament-rows-gen` (généré) | **4 373** | 3 911 | **PASS** — 2,29× sous |
| `blazor-counter-nojit` | 1 885 613 | 1 551 670 | — |
| `blazor-rows-nojit` | 1 888 029 | 1 553 388 | — |

**C1 : PASS pour les deux apps générées.** `filament-rows-gen` bat `blazor-rows-nojit` par **432× (gzip) /
397× (brotli)**. La cible C2 la plus dure (50×) passe très au large. Le coût du générateur vs l'answer key
est **+8 o gzip** sur `Rows` (4 373 vs 4 365) et **0 o** sur `Counter` (2 987 vs 2 987).

### C4 — temps `Rows`, médiane (IQR), n=10, gzip

| scénario | `blazor-rows-nojit` | `blazor-rows-aot` | `filament-rows` (main) | **`filament-rows-gen`** |
|---|---|---|---|---|
| create-cold | 47,95 (1,05) | 25,00 (9,88) | 6,45 (0,53) | **6,65 (0,80)** |
| create-warm | 16,15 (3,98) | 7,90 (2,80) | 3,40 (0,27) | **3,10 (0,10)** |
| update | 14,70 (2,20) | 4,35 (0,25) | 0,40 (0,08) | **0,40 (0,07)** |
| swap | 14,60 (0,37) | 3,90 (0,97) | 0,50 (0,10) | **0,50 (0,07)** |
| clear | 4,10 (0,25) | 3,25 (0,53) | 1,80 (0,07) | **1,80 (0,07)** |

**C4 : PASS sur les quatre scénarios de la spec.** `filament-rows-gen` n'est « pas plus lent » que le
**plus rapide** des deux Blazor (l'AOT) sur chacun — et le bat largement : create-warm (la tête, n°13/15)
**3,10 vs 7,90 ≈ 2,5×**, update **≈ 11×**, swap **≈ 8×**, clear **1,8×**, create-cold **3,8×**.

> **RÉSERVE n°32 (plancher de l'appareil).** `update`/`swap` à 0,40/0,50 ms sont 4-5 quanta du plancher
> `performance.now()` (0,1 ms). Les IQR (0,07) le confirment. Le VERDICT « pas plus lent » tient — Blazor
> est à 4,35/3,90 ms, loin du plancher — mais on ne cite **pas de ratio fin** sur ces deux lignes.

### C3 — 1 écriture DOM par incrément (compteur généré)

`filament-counter-gen` : `writesPerIncrement = [1,1,1,1,1]`, `medianWrites=1`, une seule écriture
`characterData` sur `#text in <span#counter-value>` — **identique** à `filament-counter` (main) et à
`blazor-counter-nojit`. Instrument : `MutationObserver` sur `body`, code **agnostique** identique sur les
deux frameworks. **PASS.** Rappel n°30 : Blazor franchit cette barre **exactement aussi bien** ; C3 est une
**barre de correction** que la sortie du générateur franchit, **pas un avantage** qu'elle marque. La sonde
d'allocation reste **complète pour Filament, aveugle à Blazor** — aucun ratio d'allocation inter-framework
n'est cité.

### « Les mesures sont inchangées » — VÉRIFIÉ

Sortie du générateur vs answer key écrite à la main, **même run**, même harness : `Rows` create-warm
3,10 vs 3,40, update 0,40 vs 0,40, swap 0,50 vs 0,50, clear 1,80 vs 1,80 ; `Counter` increment cold/warm
0,50/0,10 des **deux** côtés, poids **identique** au fil. **Le générateur n'est pas plus lent ni plus lourd
que le JS écrit à la main.** L'axe « inchangé » de la porte de la Phase 3 tient.

### Verdict de la porte Phase 3 (§6) — CONJONCTION DE TROIS TERMES

| terme | verdict |
|---|---|
| (a) les deux apps compilent depuis du `.razor` **pur** | **Counter : oui, porte VERTE** (alpha-équivalent à l'answer key ; la Phase 3 a fermé le trou n°55 en traduisant `@code`). **Rows : compile, porte ROUGE** — divergence à 3 points **nommés, aucun un bug de traduction** (voir ci-dessous). |
| (b) les mesures sont inchangées | **oui** (section ci-dessus). |
| (c) 20 cas hors sous-ensemble → 20 diagnostics corrects | **oui — 27/27**, chaque code dans `{FIL0001,FIL0002,FIL0003}`, chaque `(ligne,col)` sur le jeton fautif, `exit 1`, aucun fichier écrit. **3 familles de faux positifs PRÉ-EXISTANTES divulguées** (division `double/double`, composition de composant, `@if/@foreach` à la racine) — « erreur claire », non bloquantes, **appel du mainteneur**. |

**Les 3 divergences de la porte `Rows`**, toutes reportées par le test lui-même, aucune un bug :
1. **Le handler** — `rows.js` référence `run`/`update`/… ; la n°68 inline. Les deux answer keys spécifient
   des mappings de handler **différents**, disclosé d'avance.
2. **`+=` sur un signal** — `rows.js` développe `x.value = x.value + y` à la main (évalue `_rows[i]`
   **deux fois**) ; le générateur suit la règle « no syntactic desugaring » de `counter.js` et évalue
   **une** fois — sans doute le plus correct des deux.
3. **Les nœuds blancs** — `rows.js` ne construit aucun des quatre nœuds texte `\n    ` entre les boutons ;
   **Blazor les livre tous les quatre** (vérifié depuis son `BuildRenderTree`). **L'answer key diverge de la
   BASELINE**, exactement la situation de la n°64. Émettre ces nœuds rend Filament **plus gros** (+152 o) —
   la divergence **coûte** à Filament, ce qui prouve que le motif n'est pas de gonfler la porte.

> **Neutraliser exactement ces 3 points côté généré → `canon` rapporte ALPHA-ÉQUIVALENT à l'octet.** Tout
> le reste — le réassemblage du `@foreach` (accolades déséquilibrées de l'IR, n°54), record → objet,
> l'analyse d'échappement au site de construction, tableau + signal de version, `list()`, `@key`, le LCG,
> `batch`, l'ordre des méthodes, le hoisting — est **exact au jeton**. **La porte `Rows` est ROUGE parce
> que l'answer key et le générateur divergent, PAS parce que la traduction échoue.** Corriger `rows.js`
> comme la n°64 a corrigé `counter.js` est **l'appel du mainteneur** — le diff EST le résultat (n°21/51).

### §8 — LA PORTE DE DÉCISION FINALE

**C1 ET C4 passent sur la sortie du générateur, pour les deux apps.** C'est **exactement** la donnée que
les n°34/50 ont dit ne pas exister encore. **Elle existe : la variante RADICALE est VIABLE.** Le prix
nommé par la spec reste **la rupture totale avec l'écosystème de composants Blazor**, et deux apps démo ne
prouvent pas qu'un framework entier tient — mais la **condition de viabilité** de la §8 (« sortie du
générateur sous 10 ko aux temps C4 ») est **satisfaite et mesurée**, plus supposée.

**Ce que cette entrée N'établit PAS** (standard n°50) : la porte `Rows` reste rouge (appel `rows.js`
pendant) ; l'audit adverse n'a pas tourné ; le hash de harness manque au schéma 3 ; le sous-ensemble §5
est **étroit** — la §3 (async, LINQ, génériques, héritage, DI, routing, formulaires, `EventCallback`,
`RenderFragment`, paramètres cascadés) est **le prix** de ces chiffres et n'est pas près d'être payé.

---

*Fin de l'entrée n°7. Ne pas modifier — ajouter une entrée n°8 pour toute rectification.*

---

## Entrée n°8 — 2026-07-17 — **RECTIFICATION de l'entrée n°7** + résultat de l'audit adverse

L'audit adverse à 4 lentilles que la n°7 déclarait **dû** a tourné sur l'arbre commité `312118e`
(worktrees isolés). **Résultat : 4/4 lentilles `trustworthy`, ZÉRO bloqueur.** Le contenu mesuré de la
n°7 **survit à tout** : les deux lentilles de vérification ont recalculé **chaque** nombre C1/C4 depuis
les JSON — ratios 432×/397× (Rows), 631× (Counter), toutes les médianes/IQR C4, `gen ≤ AOT` sur les
quatre scénarios — **exacts**. La lentille « JS silencieusement faux » a **énuméré les 8 sites** où du C#
utilisateur atteint le JS émis et **n'a trouvé AUCUN 5ᵉ trou** de la n°41 (le correctif jumeau
`EmitAttribute` est **confirmé indépendamment**). **Première phase du projet où cette lentille revient
vide.** L'audit a néanmoins trouvé **4 défauts MAJEURS, tous dans la RÉDACTION de la n°7, aucun un nombre
faux.** Ils sont rectifiés ici.

### RECT-1 (majeur) — le hash d'identité du harness N'A PAS été perdu ; la réserve de la n°7 était FAUSSE

La n°7 nommait comme réserve : *« `schemaVersion:3` ne porte PLUS le hash d'identité du harness (n°43) »*.
**C'est faux, et vérifié depuis l'artefact** : les **19** fichiers portent tous
`environment.harness.sha256 = 47e7e46f372f8573e9a574713cee9cd6c125d55361fdb5d4b47755ac7d4536f8`,
**cardinalité 1** sur tout le run, **byte-identique** au hash que la n°43 nomme pour les entrées n°4/5. Le
per-fichier (`bench.mjs`, `server.mjs`, `expected-labels.json`) **matche le harness commité à `312118e`**.
**« Seul le composant change » est donc machine-prouvable — et l'évidence est la PLUS FORTE du projet**,
l'exact contraire de ce que la n°7 admettait. **C'était mon erreur, du type précis que le dépôt refuse :
affirmer depuis la croyance, pas depuis l'artefact — ma sonde cherchait une clé contenant « hash », le
champ s'appelle `environment.harness`.** L'audit m'a pris en flagrant délit du même défaut que la n°78
reproche à l'agent de mesure. Rien « à rétablir ».

### RECT-2 (majeur) — la CHARGE MACHINE, omise par la n°7, penchait dans le sens flatteur (n°47/48)

La n°7 ne divulguait **pas** l'état de charge, en rupture avec l'en-tête append-only de ce fichier (« son
environnement complet et ses réserves connues ») et avec les entrées n°1/2 qui portent toutes deux une
section « Charge machine ». **La machine n'était PAS au repos**, échantillonné juste avant le
chronométrage (2026-07-17 12:23:51+0200) :

| | |
|---|---|
| Load average | **3,10 / 3,60 / 4,96** sur 18 cœurs (6 P + 12 E) ; instantané ~1,5 cœur occupé (~92 % idle) |
| Rider **résident** | `rider` 2,6 Gio RSS, `Rider.Backend` (ReSharperHost) 1,47 Gio + 4 workers `dotnet` + un `VBCSCompiler` — **idle-résident** (0 % instantané), mais un rebuild ReSharper déclenché en cours de run tomberait dans une fenêtre chronométrée |
| Autres consommateurs (aucun à nous) | OrbStack Helper ~32 % CPU / 4,1-4,8 Gio, `logioptionsplus` ~25-33 %, WindowServer ~11 % — somme ~1,3-2,2 cœurs de 18 |
| Swap | **1 010 Mio utilisés / 2 048 Mio** — un défaut de page dans une fenêtre chronométrée gonfle l'IQR (n°19, verbatim) |

**Les verdicts survivent** — C1 est déterministe (octets, IQR 0) et insensible à la charge ; les marges
C4 sont 2,5-11× **sans recouvrement d'échantillons** ; l'entrelacement décorrèle l'identité de framework
de la dérive **monotone**. **Mais l'omission est un vrai trou de divulgation** dans un fichier dont
l'en-tête est entièrement la divulgation honnête, et il penchait vers Filament — c'est nommé, pas lissé.

### RECT-3 (majeur) — le §8 : « condition de viabilité satisfaite pour DEUX apps », pas « RADICAL est viable »

La n°7 (et surtout le message de commit `312118e`, en capitales) affirme **« la variante RADICALE est
VIABLE »** comme un verdict autoportant. **C'est plus que la donnée n'autorise.** RADICAL est une
assertion d'architecture sur un **framework entier** ; la preuve est **deux apps** exerçant un
sous-ensemble **§5 étroit** — les non-objectifs §3 (async, LINQ, génériques, héritage, DI, routing,
formulaires, `EventCallback`, `RenderFragment`, paramètres cascadés) sont **entièrement non exercés**. Le
verbe porteur correct, au standard n°34/50, est : **« la CONDITION de viabilité de la §8 (sortie du
générateur < 10 ko aux temps C4) est satisfaite et MESURÉE pour `Counter` et `Rows` »** — pas « le
framework est viable ». La thèse n'est **pas falsifiée** et RADICAL n'est **pas éliminée** ; elle n'est
pas **établie** comme architecture.

### RECT-4 (majeur) — le risque paquet-EOL (n°52) manquait au point de décision §8

La n°52 dit explicitement que le gel de `Microsoft.AspNetCore.Razor.Language` **6.0.36** (dernière version
publiée, hors support, .NET 10 a fermé l'API) **« pèse sur la §8 » et frappe RADICALE plus fort** — RADICAL
est *défini* comme un compilateur autonome sur ce paquet mort. Le verdict §8 de la n°7 et sa liste « ce que
cette entrée n'établit PAS » **l'omettaient**. **Il fait partie du prix de RADICAL et se lit ici** : opter
RADICAL, c'est bâtir sur un parser Razor gelé en 2021.

### Rectifications mineures

- **19 fichiers, pas 20.** La n°7 et la n°78 disent « les 20 fichiers existent » ; il y en a **19** (8
  poids Blazor + 8 Filament + 3 c3). Aucun nombre mesuré ne change ; le fait est corrigé.
- **`scenariosComplete:true` ne vaut que pour les 16 fichiers de timing.** Les 3 fichiers c3 sont des
  sondes mono-run (`runs=1`, `scenariosComplete:false`) — c'est correct pour C3 (5 incréments comptés),
  mais le « complet » de la n°7 ne les couvre pas.
- **README** : son verdict §8 (« Counter seul », `@code` manuel, « n°34 ouverte ») était **périmé** —
  la Phase 3 l'a dépassé ; mis à jour.
- **Deux familles de sur-refus non divulguées** (lentille n°41) : un initialiseur d'auto-propriété de
  record refuse un littéral **négatif** `= -1` (Blazor le compile) ; un champ de type record est admis mais
  **inutilisable** (aucun initialiseur de champ n'est dans le sous-ensemble). « Erreur claire », non
  silencieuse — mais des faux positifs, consignés pour la Phase 3+.

**Ce que l'audit N'A PAS pu casser** (déclaré parce que c'est le résultat) : la ratio d'allocation
interdite (n°30) — absente ; le plancher d'appareil (n°32) — les échantillons Blazor ne s'empilent pas au
minimum, le « pas plus lent » tient ; C1 sur bundle **production**, C3 sur bundle **-stats**, jamais
mélangés ; l'oracle de labels — **MATCH byte-exact**, pas de hoisting/interning ; la porte Counter
**verte**, la porte Rows **rouge** pour les 3 raisons nommées. **La mesure de la Phase 3 est solide.**

---

*Fin de l'entrée n°8. Ne pas modifier — ajouter une entrée n°9 pour toute rectification.*

---

## Entrée n°9 — 2026-07-18 — Phase 4 : la division `double` mesurée contre Blazor (CORRECTION, pas poids/vitesse)

Première entrée qui mesure un **élargissement du sous-ensemble §5**, et non un poids ou une vitesse. La
division `double` entre dans le sous-ensemble compilé (décision #87) ; comme **ni `counter.js` ni `rows.js`
ne contiennent de division**, il n'existait **aucun artefact mesuré** contre lequel la juger. Le
propriétaire a tranché : on ne l'admet pas sur le seul argument d'identité IEEE-754, on **fabrique
l'artefact** et on le passe à l'oracle de contrat DOM (l'instrument des décisions #29/#30). Cette entrée
est ce passage.

### Ce qui est mesuré, et pourquoi c'est le générateur et non l'arithmétique

`baseline/Divide.Blazor/App.razor` fait `value = value / 2.0` sur un `double` initialisé à `7.0`. **7.0 / 2.0
= 3.5** — une valeur que la division ENTIÈRE (`7 / 2 == 3`) **ne peut jamais produire**. L'oracle ne teste
donc pas la fidélité de l'IEEE-754 (elle est acquise a priori) : il teste que **le générateur ÉMET la bonne
division**. Un générateur qui aurait émis une sémantique entière rendrait `3`, et l'oracle le verrait. C'est
la classe de défaut qu'un argument sur les nombres flottants n'attrape pas ; la mesure, si.

### Environnement

- macOS (Darwin 25.5.0, arm64). **.NET SDK 10.0.301**. Node **v26.5.0**. **Playwright 1.61.1 / Chromium
  150.0.7871.127**, headless. **`HARNESS_VERSION` 1.4.0** — voir la réserve ci-dessous.
- Blazor publié en Release, `InvariantGlobalization=true` (le défaut WASM), pour que `3.5` ne se rende
  jamais `3,5` sous une locale.

### Protocole

Correction seulement. Le nouveau mode `--contract-only` de `bench.mjs` charge l'app, exécute `verifyContract`
et sort **sans** aucun run de poids/vitesse : pour une app triviale, C1/C4 ne portent aucun signal — la
seule question est *« rend-elle le nombre de Blazor ? »*. La branche `divide` de `verifyContract` lit
`#divide-value` (doit valoir `7`), clique `#halve`, relit (doit valoir `3.5`). L'app Blazor et l'app Filament
générée passent par **le même oracle**.

### Commande pour rejouer

```
# 1) navigateur (une fois)
(cd bench/harness && npm ci && npx playwright install chromium)
# 2) baseline Blazor -> WASM
dotnet publish baseline/Divide.Blazor -c Release -o bench/publish/blazor-divide
# 3) app Filament (le générateur émet Divide.g.js, esbuild bundle, bannière vérifiée depuis l'artefact)
./bench/build-filament.sh filament-divide-gen
# 4) l'oracle, les deux côtés
node bench/harness/bench.mjs --dir bench/publish/blazor-divide/wwwroot   --app divide --label blazor-divide       --headless --contract-only
node bench/harness/bench.mjs --dir bench/publish/filament-divide-gen     --app divide --label filament-divide-gen --headless --contract-only
```

### Résultat

| Label | `#divide-value` initial | après `#halve` | verdict |
|---|---|---|---|
| **blazor-divide** (autorité) | `7` | **`3.5`** | contrat OK |
| **filament-divide-gen** (générateur) | `7` | **`3.5`** | contrat OK |

**Les deux rendent `7 → 3.5`, à l'identique.** Le `3.5` (et non `3`) prouve que c'est une **vraie division
`double`** ; l'égalité avec Blazor prouve que **le générateur émet la division fidèlement**. L'élargissement
est **mesuré**, pas raisonné. `int/int` reste refusé (message corrigé : il nomme la troncature entière au
lieu de prétendre que « §5 admet les opérateurs arithmétiques »).

### Ce que cette entrée N'établit PAS, et ses réserves

- **CORRECTION seulement.** Aucun C1/C3/C4 sur `divide` — délibérément (décision du propriétaire). L'app
  existe pour l'oracle, pas pour la balance ni le chronomètre.
- **`HARNESS_VERSION` 1.3.0 → 1.4.0, DIVULGUÉ (n°31/43/59).** `bench.mjs` a changé (la branche `divide` + le
  mode `--contract-only`), donc son hash `HARNESS_SOURCE_FILES` change ; le numéro est monté en conséquence.
  Les mesures antérieures (n°1–8) portent 1.3.0 ou moins et **ne sont pas comparables ligne à ligne** à une
  future mesure de poids sous 1.4.0 — mais cette entrée ne mesure aucun poids, donc rien d'antérieur n'est
  invalidé.
- **Le §8 ne bouge pas comme ARCHITECTURE.** Le sous-ensemble §5 s'élargit d'**un** construct ; RADICAL
  reste « ni éliminée ni établie ». Les deux autres faux positifs de la n°77 (composition de composants,
  contrôle de flux à la racine) restent **ouverts**.

---

*Fin de l'entrée n°9. Ne pas modifier — ajouter une entrée n°10 pour toute rectification.*

---

## Entrée n°10 — 2026-07-18 — Phase 4 : la composition de composants (feuille statique) mesurée contre Blazor (CORRECTION)

Deuxième élargissement de §5 mesuré, et le deuxième des trois faux positifs de la n°77 fermé (après la
division, entrée n°9). La **composition de composants en feuille statique** entre dans le sous-ensemble
(décision #88) : `<Greeting Name="World" />` résout le frère `Greeting.razor`, plie le paramètre statique en
CONSTANTE, et **inline** l'unique racine statique de l'enfant dans le parent. Comme aucune answer key ne
contient d'enfant composé, l'artefact est **fabriqué et mesuré** — même protocole que la n°9.

### Ce qui est mesuré, et pourquoi c'est le générateur

`baseline/Compose.Blazor` : un parent `<div id="wrap"><Greeting Name="World" /></div>` et un enfant
`Greeting.razor` = `<span id="greeting">Hello, @Name</span>` avec `[Parameter] string Name`. Blazor
**instancie** l'enfant au RUNTIME ; Filament l'**expanse au COMPILE-TIME** (`@Name` → `'World'`). L'oracle
demande : *le DOM composé de Filament est-il celui que Blazor rend ?* Un générateur qui aurait laissé
`<Greeting>` comme élément inconnu, rendu `Hello, @Name` littéral, ou perdu le paramètre, rendrait un
`#greeting` différent — et l'oracle le verrait.

### Environnement

- macOS (Darwin 25.5.0, arm64). **.NET SDK 10.0.301**. Node **v26.5.0**. **Playwright 1.61.1 / Chromium**,
  headless. **`HARNESS_VERSION` 1.5.0** — voir la réserve. Blazor Release, `InvariantGlobalization=true`.

### Protocole

Correction seulement (mode `--contract-only`, aucun run de poids/vitesse). La branche `compose` de
`verifyContract` lit `#greeting.textContent` (doit valoir `Hello, World`). Une **feuille statique n'a aucune
interaction** : le rendu INITIAL EST la mesure — c'est exactement la question de la composition (le
compile-time de Filament reproduit-il le runtime de Blazor ?).

### Commande pour rejouer

```
(cd bench/harness && npm ci && npx playwright install chromium)
dotnet publish baseline/Compose.Blazor -c Release -o bench/publish/blazor-compose
./bench/build-filament.sh filament-compose-gen
node bench/harness/bench.mjs --dir bench/publish/blazor-compose/wwwroot   --app compose --label blazor-compose        --headless --contract-only
node bench/harness/bench.mjs --dir bench/publish/filament-compose-gen     --app compose --label filament-compose-gen  --headless --contract-only
```

### Résultat

| Label | `#greeting` | verdict |
|---|---|---|
| **blazor-compose** (autorité) | **`Hello, World`** | contrat OK |
| **filament-compose-gen** (générateur) | **`Hello, World`** | contrat OK |

**Les deux rendent `Hello, World`, à l'identique.** L'expansion compile-time de Filament reproduit la
composition runtime de Blazor. L'élargissement est **mesuré**, pas raisonné.

### Ce que cette entrée N'établit PAS, et ses réserves

- **CORRECTION seulement**, aucun C1/C3/C4 (app triviale, décision du propriétaire).
- **`HARNESS_VERSION` 1.4.0 → 1.5.0, DIVULGUÉ (n°31/43/59).** `bench.mjs` a changé (branche `compose`), donc
  son hash change ; le numéro monte. Aucune mesure de poids antérieure n'est invalidée (cette entrée n'en
  mesure aucune).
- **SLICE ÉTROITE.** Seule la composition **feuille statique / paramètre string / un niveau** est admise.
  Restent refusés, **loud et localisés** : paramètre lié (`Name="@x"`), paramètre non-string, enfant avec
  état/événements, composition imbriquée, enfant multi-racine, et un frère `.razor` absent
  (`unresolved-component`). Le §8 ne bouge pas comme architecture : RADICAL reste « ni éliminée ni établie ».
  Il reste le TROISIÈME faux positif de la n°77 : le contrôle de flux à la racine du template.

---

*Fin de l'entrée n°10. Ne pas modifier — ajouter une entrée n°11 pour toute rectification.*

---

## Entrée n°11 — 2026-07-18 — Phase 4 : le contrôle de flux À LA RACINE mesuré contre Blazor (CORRECTION)

Troisième élargissement de §5 mesuré, et le TROISIÈME et DERNIER des trois faux positifs de la n°77 fermé
(après la division, entrée n°9 ; la composition, entrée n°10). Le **contrôle de flux à la racine du template**
entre dans le sous-ensemble (décision #89) : un `@foreach`/`@if` posé à la RACINE du composant — sans élément
qui l'enveloppe — se mappe désormais sur `target`, le point de montage, au lieu de refuser
`[template-code-at-root]`. Comme aucune answer key ne contient de contrôle de flux racine, les artefacts sont
**fabriqués et mesurés** — même protocole que les n°9/n°10, et cette fois DEUX apps (une par construction),
sur le choix explicite du propriétaire de fermer le faux positif en entier.

### Ce qui est mesuré, et pourquoi c'est le générateur

Deux apps, chacune une pure tranche racine :

- `baseline/RootForeach.Blazor` : `<button id="add">` + un `@foreach (string item in items)` racine sans
  wrapper, la liste `items` mutée au clic. `@foreach` exige une collection RÉACTIVE (une liste jamais mutée
  est refusée : pas de signal de version pour relancer `list()`), donc la liste part vide et le clic la
  peuple. Le clic EST la mesure : trois `<li>` réconciliés **directement dans `#app`** (le `target`), sans
  élément wrapper.
- `baseline/RootIf.Blazor` : `<button id="toggle">` + un `@if (show)` racine sans wrapper. Le clic bascule
  `show` ; le `<span id="cond">` se **monte et se démonte** directement sur `#app`. Un balisage inconditionnel
  resterait en place ; un vrai `@if` racine conditionne — c'est ce que le mount/unmount mesure.

L'oracle demande, pour chacune : *le DOM que Filament produit est-il celui que Blazor produit ?* Un générateur
qui aurait attaché la `list()` à un élément créé plutôt qu'à `target`, perdu une ligne, ou laissé le balisage
inconditionnel, rendrait un DOM différent — et l'oracle le verrait contre le DOM que Blazor rend lui-même.

### Environnement

- macOS (Darwin 25.5.0, arm64). **.NET SDK 10.0.301**. **Playwright / Chromium 150.0.7871.127**, headless.
  **`HARNESS_VERSION` 1.6.0** — voir la réserve. Blazor Release, `InvariantGlobalization=true`.

### Protocole

Correction seulement (mode `--contract-only`, aucun run de poids/vitesse). La branche `rootforeach` de
`verifyContract` vérifie que `#app li` part **vide**, clique `#add`, et exige `[alpha, beta, gamma]`. La
branche `rootif` vérifie `#cond` présent (`show=true`), clique `#toggle` → `#cond` **absent** (`show=false`),
re-clique → `#cond` de nouveau présent. Deux constructions, deux mesures, un même élargissement.

### Commande pour rejouer

```
(cd bench/harness && npm ci && npx playwright install chromium)
dotnet publish baseline/RootForeach.Blazor -c Release -o bench/publish/blazor-rootforeach
dotnet publish baseline/RootIf.Blazor      -c Release -o bench/publish/blazor-rootif
./bench/build-filament.sh filament-rootforeach-gen filament-rootif-gen
node bench/harness/bench.mjs --dir bench/publish/blazor-rootforeach/wwwroot --app rootforeach --label blazor-rootforeach       --headless --contract-only
node bench/harness/bench.mjs --dir bench/publish/filament-rootforeach-gen   --app rootforeach --label filament-rootforeach-gen --headless --contract-only
node bench/harness/bench.mjs --dir bench/publish/blazor-rootif/wwwroot      --app rootif      --label blazor-rootif            --headless --contract-only
node bench/harness/bench.mjs --dir bench/publish/filament-rootif-gen        --app rootif      --label filament-rootif-gen      --headless --contract-only
```

### Résultat

| Label | `#app li` après `#add` | verdict |
|---|---|---|
| **blazor-rootforeach** (autorité) | `[alpha, beta, gamma]` (initial `[]`) | contrat OK |
| **filament-rootforeach-gen** (générateur) | `[alpha, beta, gamma]` (initial `[]`) | contrat OK |

| Label | `#cond` initial → toggle → toggle | verdict |
|---|---|---|
| **blazor-rootif** (autorité) | présent → absent → présent | contrat OK |
| **filament-rootif-gen** (générateur) | présent → absent → présent | contrat OK |

**Les deux paires rendent à l'identique.** La `list(target, …)` de Filament réconcilie dans `#app` la même
liste que Blazor ; son `@if` racine monte/démonte `#cond` sur `#app` exactement comme Blazor. L'élargissement
est **mesuré**, pas raisonné.

### Ce que cette entrée N'établit PAS, et ses réserves

- **CORRECTION seulement**, aucun C1/C3/C4 (apps triviales, décision du propriétaire).
- **`HARNESS_VERSION` 1.5.0 → 1.6.0, DIVULGUÉ (n°31/43/59).** `bench.mjs` a changé (branches `rootforeach`
  et `rootif` + entrées `APPS`), donc son hash change ; le numéro monte. Aucune mesure de poids antérieure
  n'est invalidée (cette entrée n'en mesure aucune).
- **NŒUD-COMMENTAIRE (`@if`).** Le `<!-- -->` ancre du `@if` racine est une divergence +1-nœud DIVULGUÉE
  vis-à-vis de Blazor (catégorie des marqueurs `<!--!-->` de la décision #20) ; la retirer demande l'ancrage
  next-sibling, différé. L'oracle vérifie la présence/absence de `#cond`, insensible au commentaire.
- **MAPPING GÉNÉRAL, mesuré sur deux formes.** Le mapping racine → `target` est général (toute région de
  contrôle de flux racine), car `RegionOps` refuse toute instruction hors `@foreach`/`@if`
  (`unsupported-template-statement`) — le re-parse est son propre garde. Mesuré ici sur les deux
  constructions ; le §8 ne bouge pas comme architecture : RADICAL reste « ni éliminée ni établie ».
- **LES TROIS FAUX POSITIFS DE LA n°77 SONT MAINTENANT FERMÉS** (division n°9, composition n°10, contrôle de
  flux racine n°11) — chacun fermé pour sa tranche et mesuré, ou différé avec raison.

---

*Fin de l'entrée n°11. Ne pas modifier — ajouter une entrée n°12 pour toute rectification.*

---

## Entrée n°12 — 2026-07-18 — Phase 4 : la composition à PARAMÈTRE LIÉ (réactif) mesurée contre Blazor (CORRECTION)

Première **sous-tranche différée de la n°88** fermée : un paramètre de composant **lié** (`<Display Value="@count" />`)
entre dans le sous-ensemble (décision #90). Là où la n°88 pliait un paramètre STATIQUE en constante, ici la valeur
est **réactive** : le `@Value` de l'enfant est une **liaison vivante** sur le signal `count` du parent. C'est
exactement le « parent→child reactive plumbing » que la n°88 disait « not implemented ». Comme aucune answer key
ne contient de paramètre lié, l'artefact est **fabriqué et mesuré** — même protocole que les n°9/10/11.

### Ce qui est mesuré, et pourquoi c'est le générateur

`baseline/BoundCompose.Blazor` : un parent `<div id="wrap"><button id="inc">…</button><Display Value="@count" /></div>`
avec `count` incrémenté au clic, et un enfant `Display.razor` = `<span id="out">@Value</span>` avec
`[Parameter] int Value`. Blazor **instancie** l'enfant au runtime et lui **repasse** `count` à chaque rendu ; Filament
**inline** l'enfant au compile-time (n°88) et son `@Value` devient `effect(() => setText(_tx, count.value))` — une
lecture VIVANTE du signal `count` DU PARENT, directement en portée, **sans repassage de prop ni instance runtime**.
`count` est **hissé en signal** parce que l'expression liée compte comme une lecture du template (récoltée dans la
compilation du parent, n°90). L'oracle demande : *le `#out` de Filament suit-il `count` comme celui de Blazor ?* Un
générateur qui aurait plié la valeur au montage laisserait `#out` à « 0 » au clic — et l'oracle le verrait.

### Environnement

- macOS (Darwin 25.5.0, arm64). **.NET SDK 10.0.301**. **Playwright / Chromium 150.0.7871.127**, headless.
  **`HARNESS_VERSION` 1.7.0** — voir la réserve. Blazor Release, `InvariantGlobalization=true`.

### Protocole

Correction seulement (mode `--contract-only`, aucun run de poids/vitesse). La branche `boundcompose` de
`verifyContract` lit `#out` (doit valoir « 0 »), clique `#inc`, et exige « 1 ». L'interaction est LA mesure : elle
prouve que la liaison traverse la frontière de composition, ce qu'un rendu initial seul ne montrerait pas.

### Commande pour rejouer

```
(cd bench/harness && npm ci && npx playwright install chromium)
dotnet publish baseline/BoundCompose.Blazor -c Release -o bench/publish/blazor-boundcompose
./bench/build-filament.sh filament-boundcompose-gen
node bench/harness/bench.mjs --dir bench/publish/blazor-boundcompose/wwwroot --app boundcompose --label blazor-boundcompose       --headless --contract-only
node bench/harness/bench.mjs --dir bench/publish/filament-boundcompose-gen   --app boundcompose --label filament-boundcompose-gen --headless --contract-only
```

### Résultat

| Label | `#out` initial → `#inc` | verdict |
|---|---|---|
| **blazor-boundcompose** (autorité) | `0` → `1` | contrat OK |
| **filament-boundcompose-gen** (générateur) | `0` → `1` | contrat OK |

**Les deux rendent `0 → 1`, à l'identique.** Le `@Value` de l'enfant, lié au signal `count` du parent, suit sa valeur
au clic — la liaison réactive traverse la frontière de composition. L'élargissement est **mesuré**, pas raisonné.

### Ce que cette entrée N'établit PAS, et ses réserves

- **CORRECTION seulement**, aucun C1/C3/C4 (app triviale, décision du propriétaire).
- **`HARNESS_VERSION` 1.6.0 → 1.7.0, DIVULGUÉ (n°31/43/59).** `bench.mjs` a changé (branche `boundcompose` + entrée
  `APPS`), donc son hash change ; le numéro monte. Aucune mesure de poids antérieure n'est invalidée.
- **TRANCHE ÉTROITE — liaison RÉACTIVE seulement.** Seul un paramètre scalaire lié à un **état parent réactif**, dans
  un enfant **feuille-display**, en **affichage texte**, est admis. Refusés loud+localisés : liaison NON réactive
  (« bind-once » d'une constante, `bound-parameter`), enfant avec état/événements (`composition-out-of-subset`),
  attribut d'enfant lié (`class="@x"` — les attributs réactifs ne sont pas encore dans le sous-ensemble de base).
  Restent différées : enfant avec état, composition imbriquée, paramètre non-scalaire. §8 inchangé : RADICAL reste
  « ni éliminée ni établie ».

---

*Fin de l'entrée n°12. Ne pas modifier — ajouter une entrée n°13 pour toute rectification.*

---

## Entrée n°13 — 2026-07-19 — Phase 4 : l'attribut `class` RÉACTIF mesuré contre Blazor (CORRECTION)

**Sous-tranche différée de la n°12 fermée** : un **attribut `class` réactif** (`class="@statusClass"`) entre dans le
sous-ensemble (décision #94). La n°12 refusait explicitement « attribut d'enfant lié (`class="@x"`) — les attributs
réactifs ne sont pas encore dans le sous-ensemble de base » ; c'est cette réserve-là qui tombe. La règle est celle de
la liaison de TEXTE (`EmitBinding`), la cible d'écriture étant un attribut (`setAttr`) au lieu d'un nœud texte
(`setText`). `setAttr` **est déjà** dans le runtime (et dans `RuntimeExports`) : **aucun octet de runtime ne change**,
l'élargissement est **générateur seul**. Comme aucune answer key préexistante ne portait d'attribut dynamique,
l'artefact est **fabriqué et mesuré** — même protocole que les n°9/10/11/12.

### Ce qui est mesuré, et pourquoi c'est le générateur

`baseline/ReactiveAttr.Blazor` : un compteur `<p id="status" class="@statusClass">…<span id="counter-value">@currentCount</span></p>`
avec `statusClass` (« zero ») et `currentCount` (0) réassignés au clic (« counting », 1). `statusClass` est **lu par le
template** (l'attribut `class`) ET **assigné hors construction** (dans `Increment`) : il est donc **hissé en signal**, et
la liaison `class` devient `effect(() => setAttr(_el, 'class', statusClass.value))` — la MÊME règle réactive que le
texte, l'écriture visant un attribut. Blazor **re-rend** l'attribut à chaque changement d'état ; Filament abonne un
**seul effect** qui écrit l'attribut. `Increment` écrit deux fois → le handler `batch()` : un seul flush pour les deux
signaux (texte + attribut). L'oracle demande : *le `class` de `#status` suit-il l'état comme celui de Blazor, en phase
avec le texte ?* Un générateur qui aurait écrit l'attribut au montage (sans effect) laisserait `class="zero"` au clic —
et l'oracle le verrait.

### Environnement

- macOS (Darwin 25.5.0, arm64). **.NET SDK 10.0.301**. **Playwright / Chromium 150.0.7871.127**, headless.
  **`HARNESS_VERSION` 1.8.0** — voir la réserve. Blazor Release, `InvariantGlobalization=true`.

### Protocole

Correction seulement (mode `--contract-only`, aucun run de poids/vitesse). La branche `reactiveattr` de
`verifyContract` lit le `class` de `#status` (doit valoir « zero ») et le texte de `#counter-value` (« 0 »), clique
`#increment`, et exige « counting » / « 1 ». L'interaction est LA mesure : elle prouve que l'attribut réactif SUIT
l'état, ce qu'un rendu initial seul ne montrerait pas.

### Commande pour rejouer

```
(cd bench/harness && npm ci && npx playwright install chromium)
dotnet publish baseline/ReactiveAttr.Blazor -c Release -o bench/publish/blazor-reactiveattr
./bench/build-filament.sh filament-reactiveattr-gen
node bench/harness/bench.mjs --dir bench/publish/blazor-reactiveattr/wwwroot --app reactiveattr --label blazor-reactiveattr       --headless --contract-only
node bench/harness/bench.mjs --dir bench/publish/filament-reactiveattr-gen   --app reactiveattr --label filament-reactiveattr-gen --headless --contract-only
```

### Résultat

| Label | `#status` class / `#counter-value` : initial → `#increment` | verdict |
|---|---|---|
| **blazor-reactiveattr** (autorité) | `zero` / `0` → `counting` / `1` | contrat OK |
| **filament-reactiveattr-gen** (générateur) | `zero` / `0` → `counting` / `1` | contrat OK |

**Les deux rendent `zero/0 → counting/1`, à l'identique.** L'attribut `class`, lié au signal `statusClass`, suit l'état
au clic, en phase avec la liaison de texte — la réactivité d'attribut est **mesurée**, pas raisonnée.

### Ce que cette entrée N'établit PAS, et ses réserves

- **CORRECTION seulement**, aucun C1/C3/C4 (app triviale, décision du propriétaire).
- **`HARNESS_VERSION` 1.7.0 → 1.8.0, DIVULGUÉ (n°31/43/59).** `bench.mjs` a changé (branche `reactiveattr` + entrée
  `APPS`), donc son hash change ; le numéro monte. Aucune mesure de poids antérieure n'est invalidée.
- **RUNTIME INCHANGÉ.** `setAttr` existait déjà (rows.js l'émet en statique) ; l'élargissement n'a touché QUE le
  générateur. `git diff -- src/filament-runtime` est vide.
- **TRANCHE ÉTROITE — `class` seulement, valeur `@expr` pure.** L'émission dynamique est réservée à une **liste
  blanche d'attributs** (`{ class }`, comme `PropertyAttributes`) : tout autre nom reste refusé `dynamic-attribute`
  (dont le `value=` de `@bind` — d'où `BindConverter` toujours cité, test `Bind`). Refusés loud+localisés :
  attributs booléens présence/absence (`disabled`), valeur mixte littéral+expression (`class="box @x"`), contrôle de
  flux dans une valeur d'attribut. Restent différés : attributs booléens, autres noms d'attributs, valeurs
  concaténées. §8 inchangé : RADICAL reste « ni éliminée ni établie ».

---

*Fin de l'entrée n°13. Ne pas modifier — ajouter une entrée n°14 pour toute rectification.*

---

## Entrée n°14 — 2026-07-19 — Phase 4 : l'attribut booléen `disabled` (présence/absence) mesuré contre Blazor (CORRECTION)

**Sous-tranche différée de la n°13 fermée** : un **attribut booléen `disabled`** (`disabled="@locked"`) entre dans le
sous-ensemble (décision #95). La n°13 refusait explicitement « attributs booléens présence/absence (`disabled`) » ; c'est
cette réserve-là qui tombe. Un attribut booléen est une **sémantique différente** de la n°13 : Blazor rend `<button disabled>`
quand la valeur est vraie et **omet l'attribut** quand elle est fausse — jamais `disabled="true"` ni `disabled="false"`. La
primitive présence/absence **existe déjà** dans le runtime : `setAttr(el, name, v)` fait `removeAttribute` quand `v == null`
(« null/undefined le retire — c'est ainsi qu'un compilateur exprime "absent" »). L'émission mappe donc le booléen vers
`('' | null)` — `vrai → '' → setAttribute` (présent), `faux → null → removeAttribute` (absent) — via un ternaire au-dessus de
`setAttr`. **Aucun octet de runtime ne change**, l'élargissement est **générateur seul** ; l'artefact est **fabriqué et
mesuré** — même protocole que les n°9→13.

### Ce qui est mesuré, et pourquoi c'est le générateur

`baseline/BoolAttr.Blazor` : deux boutons, `<button id="target" disabled="@locked">` et `<button id="toggle" @onclick="Toggle">`,
avec `locked` (vrai) inversé au clic. `locked` est **lu par le template** (l'attribut `disabled`) ET **assigné hors
construction** (dans `Toggle`) : il est donc **hissé en signal**, et la liaison devient
`effect(() => setAttr(_el0, 'disabled', locked.value ? '' : null))` — la MÊME règle réactive que la n°13, la valeur passant
par le ternaire présence/absence. `locked` démarre **vrai** : le premier run de l'effect (dans l'arbre détaché, avant
attach) écrit `setAttribute` → `#target` disabled présent, sans MutationRecord ; le clic bascule à faux → l'effect ré-exécute
→ **`removeAttribute`** → `#target` disabled absent (le chemin qu'un attribut chaîne ne prend JAMAIS). Un générateur qui aurait
écrit `setAttr(_el0, 'disabled', locked.value)` naïvement laisserait `disabled="false"` (présent) au clic — et l'oracle le
verrait.

### Environnement

- macOS (Darwin 25.5.0, arm64). **.NET SDK 10.0.301**. **Playwright / Chromium 150.0.7871.127**, headless.
  **`HARNESS_VERSION` 1.9.0** — voir la réserve. Blazor Release, `InvariantGlobalization=true`.

### Protocole

Correction seulement (mode `--contract-only`, aucun run de poids/vitesse). La branche `boolattr` de `verifyContract` lit
`#target` (doit avoir `disabled` présent — `hasAttribute` ET la propriété IDL `.disabled` toutes deux vraies), clique
`#toggle`, et exige l'attribut **absent** (les deux fausses). L'oracle vérifie les DEUX : la sérialisation DOM
(`hasAttribute`) et la propriété `.disabled`, pour épingler ce que Blazor fait *réellement*, pas ce qu'on suppose. L'interaction
est LA mesure : elle prouve que le booléen **retire** l'attribut à faux (`removeAttribute`), ce qu'un rendu initial seul ne
montrerait pas.

### Commande pour rejouer

```
(cd bench/harness && npm ci && npx playwright install chromium)
dotnet publish baseline/BoolAttr.Blazor -c Release -o bench/publish/blazor-boolattr
./bench/build-filament.sh filament-boolattr-gen
node bench/harness/bench.mjs --dir bench/publish/blazor-boolattr/wwwroot --app boolattr --label blazor-boolattr       --headless --contract-only
node bench/harness/bench.mjs --dir bench/publish/filament-boolattr-gen   --app boolattr --label filament-boolattr-gen --headless --contract-only
```

### Résultat

| Label | `#target` disabled `{hasAttr, prop}` : initial → `#toggle` | verdict |
|---|---|---|
| **blazor-boolattr** (autorité) | `{true, true}` → `{false, false}` | contrat OK |
| **filament-boolattr-gen** (générateur) | `{true, true}` → `{false, false}` | contrat OK |

**Les deux rendent `présent → absent`, à l'identique** (`hasAttribute` ET `.disabled`). L'attribut `disabled`, lié au signal
`locked`, passe de présent à absent au clic via `removeAttribute` — la sémantique booléenne présence/absence est **mesurée**,
pas raisonnée.

### Ce que cette entrée N'établit PAS, et ses réserves

- **CORRECTION seulement**, aucun C1/C3/C4 (app triviale, décision du propriétaire).
- **`HARNESS_VERSION` 1.8.0 → 1.9.0, DIVULGUÉ (n°31/43/59).** `bench.mjs` a changé (branche `boolattr` + entrée `APPS`),
  donc son hash change ; le numéro monte. Aucune mesure de poids antérieure n'est invalidée.
- **RUNTIME INCHANGÉ.** `setAttr` et sa branche `null → removeAttribute` existaient déjà ; l'élargissement n'a touché QUE le
  générateur (un ternaire `? '' : null`). `git diff -- src/filament-runtime` est vide.
- **TRANCHE ÉTROITE — `disabled` seulement, valeur `@expr` pure.** L'émission booléenne est réservée à une **deuxième liste
  blanche** disjointe (`BooleanAttributes = { disabled }`, à côté de `{ class }`) : tout autre nom reste refusé
  `dynamic-attribute` (dont `value=` de `@bind`, test `Bind`, et les booléens hors liste comme `hidden`, test
  `NonAllowedBooleanAttribute`). Liste **fondée sur le nom** car le générateur ne fait aucune inférence de type : `disabled`
  est **engagé** vers présence/absence — un `disabled` de type chaîne est donc différé, distinct. Restent différés : autres
  noms booléens (`checked`/`readonly`/`hidden`), `disabled` chaîne, valeur mixte littéral+expression (`class="box @x"`).
  §8 inchangé : RADICAL reste « ni éliminée ni établie ».

---

*Fin de l'entrée n°14. Ne pas modifier — ajouter une entrée n°15 pour toute rectification.*

---

## Entrée n°15 — 2026-07-19 — Phase 4 : la valeur `class` MIXTE (littéral+expression) mesurée contre Blazor (CORRECTION)

**Sous-tranche différée des n°13/n°14 fermée** : une **valeur `class` mixte** (`class="badge @statusClass rounded"`)
entre dans le sous-ensemble (décision #96). Les n°13/n°14 refusaient explicitement « valeur mixte littéral+expression
(`class="box @x"`) » ; c'est cette réserve-là qui tombe. C'est la forme `class` la plus courante du monde réel
(`"btn @variant"`, `"badge @statusClass rounded"`). Razor livre la valeur en **parties ordonnées, chacune portant un
`Prefix`** (le texte qui la précède) ; le compilateur les **plie** en une seule concaténation. La n°13 admettait le
`@expr` **pur** ; le pur est le pli **dégénéré** (une expression, aucun littéral). L'élargissement est **générateur
seul** ; l'artefact est **fabriqué et mesuré** — même protocole que les n°9→14.

### Ce qui est mesuré, et pourquoi c'est le générateur

`baseline/MixedAttr.Blazor` : le compteur de la n°13 dont l'attribut devient `class="badge @statusClass rounded"` — un
littéral en tête (`badge `), une expression réactive (`statusClass`), un littéral en queue (` rounded`). `statusClass`
est **lu par le template** ET **assigné hors construction**, donc **hissé en signal**, et la liaison entière devient
`effect(() => setAttr(_el1, 'class', 'badge ' + statusClass.value + ' rounded'))` — la composition préfixe-consciente :
chaque partie apporte son `Prefix` puis son corps ; un littéral s'accumule dans un tampon, une expression vide le tampon
en terme chaîne puis émet `SlotJs`. La forme mesurée exerce **les deux vidages** (au milieu à l'expression, en queue au
littéral final). Un générateur qui aurait laissé tomber un littéral ou mal ordonné un préfixe rendrait une classe autre
que `badge counting rounded` — et l'oracle le verrait.

### Environnement

- macOS (Darwin 25.5.0, arm64). **.NET SDK 10.0.301**. **Playwright / Chromium 150.0.7871.127**, headless.
  **`HARNESS_VERSION` 1.10.0** — voir la réserve. Blazor Release, `InvariantGlobalization=true`.

### Protocole

Correction seulement (mode `--contract-only`, aucun run de poids/vitesse). La branche `mixedattr` de `verifyContract`
lit la **chaîne `class` entière** de `#status` (doit valoir « badge zero rounded ») et le texte de `#counter-value`
(« 0 »), clique `#increment`, et exige « badge counting rounded » / « 1 ». Vérifier la chaîne ENTIÈRE est la mesure :
elle prouve que les littéraux survivent autour du jeton réactif, dans l'ordre et l'espacement exacts de Blazor.

### Commande pour rejouer

```
(cd bench/harness && npm ci && npx playwright install chromium)
dotnet publish baseline/MixedAttr.Blazor -c Release -o bench/publish/blazor-mixedattr
./bench/build-filament.sh filament-mixedattr-gen
node bench/harness/bench.mjs --dir bench/publish/blazor-mixedattr/wwwroot --app mixedattr --label blazor-mixedattr       --headless --contract-only
node bench/harness/bench.mjs --dir bench/publish/filament-mixedattr-gen   --app mixedattr --label filament-mixedattr-gen --headless --contract-only
```

### Résultat

| Label | `#status` class / `#counter-value` : initial → `#increment` | verdict |
|---|---|---|
| **blazor-mixedattr** (autorité) | `badge zero rounded` / `0` → `badge counting rounded` / `1` | contrat OK |
| **filament-mixedattr-gen** (générateur) | `badge zero rounded` / `0` → `badge counting rounded` / `1` | contrat OK |

**Les deux rendent `badge zero rounded / 0 → badge counting rounded / 1`, à l'identique.** La valeur `class` mixte se
recompose au clic — les littéraux survivant autour du signal `statusClass`, en phase avec la liaison de texte — la
composition est **mesurée**, pas raisonnée.

### Ce que cette entrée N'établit PAS, et ses réserves

- **CORRECTION seulement**, aucun C1/C3/C4 (app triviale, décision du propriétaire).
- **`HARNESS_VERSION` 1.9.0 → 1.10.0, DIVULGUÉ (n°31/43/59).** `bench.mjs` a changé (branche `mixedattr` + entrée
  `APPS`), donc son hash change ; le numéro monte. Aucune mesure de poids antérieure n'est invalidée.
- **RUNTIME INCHANGÉ.** `setAttr` existait déjà ; la composition est une concaténation de chaînes JS dans le code émis.
  `git diff -- src/filament-runtime` est vide.
- **LE PUR EST INCHANGÉ, PROUVÉ.** Le pli **remplace** (généralise) la branche `class` ; le `class="@x"` pur émet
  `setAttr(_el, 'class', statusClass.value)` **octet pour octet** comme avant — la porte et le snapshot `ReactiveAttr`
  restent verts sans modification, c'est la preuve que la généralisation est transparente.
- **TRANCHE ÉTROITE — `class` seulement.** Un prédicat unique `ComposableValue` (récolte + émission, n°53) ; `DynamicValue`
  reste pour la voie booléenne `disabled`. La valeur mixte sur un **autre nom** reste refusée `dynamic-attribute` (test
  `MixedValueOnNonAllowed` à `(1,12)`) ; le **contrôle de flux** dans un attribut (`class="@if(c){…}"`) reste refusé
  `unaccounted-attribute-value` (`ComposableValue` renvoie null pour un `CSharpCodeAttributeValue` — tranche distincte).
  Général à N parties, mesuré sur une expression (deux vidages). Restent différés : contrôle de flux, `disabled` chaîne,
  autres noms. §8 inchangé : RADICAL reste « ni éliminée ni établie ».

---

*Fin de l'entrée n°15. Ne pas modifier — ajouter une entrée n°16 pour toute rectification.*

---

## Entrée n°16 — 2026-07-19 — Phase 4 : les NOMS d'attributs chaîne réactifs (title/href/aria-label) mesurés contre Blazor (CORRECTION)

**Sous-tranche différée des n°13/n°15 fermée** : trois **noms d'attributs chaîne réactifs** — `title`, `href`,
`aria-label` — rejoignent la liste blanche `DynamicAttributes` (décision #97). Les n°13→15 réservaient « autres noms
d'attributs » ; c'est cette réserve-là qui tombe (pour ce lot représentatif). Comme la récolte et l'émission sont déjà
**agnostiques au nom**, l'admission est un **changement d'une ligne** ; chaque nom compile vers la MÊME émission composée
que `class` (`effect(() => setAttr(el, nom, x.value))`, `aria-label` avec tiret inclus). Générateur seul ; artefact
**fabriqué et mesuré**.

### Validité Blazor vérifiée EN AMONT (la leçon RZ9979)

Avant toute conception, `dotnet build` d'une app `<a href="@url" title="@tip" aria-label="@label" data-testid="@tid">`
**réussit** — les attributs chaîne réactifs, tirets compris, sont du Blazor valide (contrairement au contrôle de flux
dans un attribut, retiré par RZ9979, cf. l'entrée précédente non enregistrée). La leçon : construire l'app Blazor de
base pour confirmer la validité de la source AVANT de concevoir.

### Ce qui est mesuré, et pourquoi c'est le générateur

`baseline/StringAttrs.Blazor` : un `<a id="link" href="@url" title="@tip" aria-label="@label">` dont les trois champs
(`url`/`tip`/`label`) sont réassignés au clic. Chacun est **lu par le template** ET **assigné hors construction** (dans
`Toggle`), donc **hissé en signal** ; les trois liaisons sont trois `effect(() => setAttr(a, nom, x.value))` en ordre
document (href, title, aria-label). `Toggle` écrit trois champs → handler `batch()`. Un générateur qui n'aurait pas fait
suivre un attribut (ou aurait mal géré le nom à tiret `aria-label`) laisserait une valeur périmée — l'oracle le verrait.

### Environnement

- macOS (Darwin 25.5.0, arm64). **.NET SDK 10.0.301**. **Playwright / Chromium 150.0.7871.127**, headless.
  **`HARNESS_VERSION` 1.11.0** — voir la réserve. Blazor Release, `InvariantGlobalization=true`.

### Protocole

Correction seulement (mode `--contract-only`, aucun run de poids/vitesse). La branche `stringattrs` de `verifyContract`
lit les trois attributs de `#link` (`getAttribute`), exige l'initial `{href:"/a", title:"first", aria:"one"}`, clique
`#toggle`, et exige `{href:"/b", title:"second", aria:"two"}`. Vérifier les TROIS est la mesure.

### Commande pour rejouer

```
(cd bench/harness && npm ci && npx playwright install chromium)
dotnet publish baseline/StringAttrs.Blazor -c Release -o bench/publish/blazor-stringattrs
./bench/build-filament.sh filament-stringattrs-gen
node bench/harness/bench.mjs --dir bench/publish/blazor-stringattrs/wwwroot --app stringattrs --label blazor-stringattrs       --headless --contract-only
node bench/harness/bench.mjs --dir bench/publish/filament-stringattrs-gen   --app stringattrs --label filament-stringattrs-gen --headless --contract-only
```

### Résultat

| Label | `#link` href/title/aria-label : initial → `#toggle` | verdict |
|---|---|---|
| **blazor-stringattrs** (autorité) | `/a·first·one` → `/b·second·two` | contrat OK |
| **filament-stringattrs-gen** (générateur) | `/a·first·one` → `/b·second·two` | contrat OK |

**Les deux rendent `/a·first·one → /b·second·two`, à l'identique.** Les trois attributs chaîne réactifs suivent l'état
en phase — la généralisation de la liste blanche chaîne est **mesurée**, pas raisonnée.

### Ce que cette entrée N'établit PAS, et ses réserves

- **CORRECTION seulement**, aucun C1/C3/C4 (app triviale, décision du propriétaire).
- **`HARNESS_VERSION` 1.10.0 → 1.11.0, DIVULGUÉ (n°31/43/59).** `bench.mjs` a changé (branche `stringattrs` + entrée
  `APPS`), donc son hash change ; le numéro monte. Aucune mesure de poids antérieure n'est invalidée.
- **RUNTIME INCHANGÉ.** `setAttr` prend n'importe quel nom d'attribut ; l'admission n'a touché QUE la liste blanche du
  générateur. `git diff -- src/filament-runtime` est vide.
- **FAIBLE NOUVEAUTÉ, DIVULGUÉE.** Aucune forme d'émission nouvelle — la valeur est la **preuve mesurée** que la liste
  blanche chaîne se généralise à d'autres noms, et la fermeture de la réserve « autres noms ».
- **TRANCHE ÉTROITE.** Seuls `title`/`href`/`aria-label`. `value` est **délibérément exclu** (garde `@bind` refusé, test
  `Bind` inchangé) ; le témoin de refus a **migré `title → role`** (`DynamicRole`/`MixedNonAllowed`, toujours refusés à
  `(1,12)`). Restent différés : `data-*`, `style`, `value`, autres noms. §8 inchangé : RADICAL reste « ni éliminée ni
  établie ».

---

*Fin de l'entrée n°16. Ne pas modifier — ajouter une entrée n°17 pour toute rectification.*

---

## Entrée n°17 — 2026-07-19 — Phase 4 : le corps MULTI-NŒUD d'un `@if` (branche unique) mesuré contre Blazor (CORRECTION)

**Réserve différée de la n°81 fermée pour le cas sans `@else`** : un `@if (cond) { <a><b> }` — une branche unique dont
le corps est **plus d'un élément** — rejoint le sous-ensemble compilé (décision #98). La n°81 refusait le corps
multi-nœud (`[unsupported-if-body]`) **délibérément** ; c'est cette réserve-là qui tombe, pour la branche sans `@else`.
L'abaissement plain-`@if` est **généralisé** de « un nœud par branche » à « **un item de liste par nœud du corps** » :
`list(c, () => cond ? [0, 1] : [], (i) => i, (i) => i===0 ? f0() : f1(), anchor)`. Générateur seul, **runtime
INCHANGÉ** ; les émissions de la n°81 (nœud unique) et de la n°82 (multi-branche) restent **octet pour octet**
identiques (le chemin neuf ne s'active que pour `Body.Count > 1` sur un `@if` sans `else`). Artefact **fabriqué et
mesuré**.

### Validité Blazor vérifiée EN AMONT (la leçon RZ9979)

Avant toute conception, `dotnet build baseline/IfMultiBody.Blazor` **réussit** — un `@if` à branche unique avec un
corps de deux `<span>` est du Blazor ordinaire. Le `BuildRenderTree` de Blazor (lu depuis `App_razor.g.cs`, méthode
n°64) confirme le contrat DOM : **deux `AddMarkupContent` (seq 6, 7)** dans `if (show)`, soit deux `<span>` adjacents,
enfants directs de `#w`, dans l'ordre a puis b, **sans conteneur** et **sans nœud texte intercalé**.

### Ce qui est mesuré, et pourquoi c'est le générateur

`baseline/IfMultiBody.Blazor` : `<div id="w"><button id="toggle">…</button>@if (show) { <span id="a">a</span><span
id="b">b</span> }</div>`. `show` est **lu par la condition `@if`** ET **assigné dans `Toggle`**, donc **hissé en
signal**. Le corps compile en **deux fonctions de création** (`ifBody0_0`, `ifBody0_1`) et une source
`() => (show.value) ? [0, 1] : []`, à clé identité, dispatchée par `IfCreate`. Au clic, les **deux** `<span>`
montent/démontent **ensemble**, dans l'ordre. Un générateur qui n'aurait démonté qu'un nœud, ou inversé l'ordre, ou
enveloppé le corps dans un conteneur, l'oracle le verrait (il lit `#w > span` comme chaîne d'ids jointe).

### Environnement

- macOS (Darwin 25.5.0, arm64). **.NET SDK 10.0.301**. **Playwright / Chromium 150.0.7871.127**, headless.
  **`HARNESS_VERSION` 1.12.0** — voir la réserve. Blazor Release, `InvariantGlobalization=true`.

### Protocole

Correction seulement (mode `--contract-only`, aucun run de poids/vitesse). La branche `ifmulti` de `verifyContract` lit
`#w > span` comme chaîne d'ids jointe, exige l'initial `"a,b"`, clique `#toggle` et exige `""` (les deux retirés
ENSEMBLE), reclique et exige `"a,b"` (les deux restaurés, dans l'ordre). L'ordre (`a,b`, pas `b,a`) mesure que la liste
à un-item-par-nœud préserve l'ordre ; la chaîne vide mesure que les deux nœuds démontent ensemble.

### Commande pour rejouer

```
(cd bench/harness && npm ci && npx playwright install chromium)
dotnet publish baseline/IfMultiBody.Blazor -c Release -o bench/publish/blazor-ifmulti
./bench/build-filament.sh filament-ifmulti-gen
node bench/harness/bench.mjs --dir bench/publish/blazor-ifmulti/wwwroot --app ifmulti --label blazor-ifmulti       --headless --contract-only
node bench/harness/bench.mjs --dir bench/publish/filament-ifmulti-gen   --app ifmulti --label filament-ifmulti-gen --headless --contract-only
```

### Résultat

| Label | `#w > span` ids : initial → `#toggle` → `#toggle` | verdict |
|---|---|---|
| **blazor-ifmulti** (autorité) | `a,b` → `` (vide) → `a,b` | contrat OK |
| **filament-ifmulti-gen** (générateur) | `a,b` → `` (vide) → `a,b` | contrat OK |

**Les deux rendent `a,b → «» → a,b`, à l'identique.** Les deux `<span>` du corps multi-nœud montent et démontent
**ensemble**, dans l'ordre, sans conteneur — l'abaissement à un-item-par-nœud est **mesuré**, pas raisonné. Cela ferme
aussi le « À RE-MESURER si `@if` entre un jour dans une app mesurée » laissé ouvert par la n°81.

### Ce que cette entrée N'établit PAS, et ses réserves

- **CORRECTION seulement**, aucun C1/C3/C4 (app triviale, décision du propriétaire).
- **`HARNESS_VERSION` 1.11.0 → 1.12.0, DIVULGUÉ (n°31/43/59).** `bench.mjs` a changé (branche `ifmulti` + entrée
  `APPS`), donc son hash change ; le numéro monte. Aucune mesure de poids antérieure n'est invalidée.
- **RUNTIME INCHANGÉ.** Le corps multi-nœud réemploie `list()` exactement comme `@foreach`/`@if` simple ; l'ancre-
  commentaire est un builtin DOM. `git diff -- src/filament-runtime` est vide.
- **ANCRE-COMMENTAIRE `+1` nœud.** Toujours la seule divergence divulguée d'avec Blazor (n°81/20) ; l'ancrage au frère
  suivant reste différé.
- **TRANCHE ÉTROITE.** Branche unique seulement. Le corps multi-nœud d'un `@else`/`@else if` (`IfElseMultiBody`) et le
  contrôle de flux imbriqué dans une branche (`IfNested`) **restent refusés** `[unsupported-if-body]`, témoins intacts.
  Les nœuds texte intercalés dans un corps restent refusés (`ops.Count != markup.Count`). §8 inchangé : RADICAL reste
  « ni éliminée ni établie ».

---

*Fin de l'entrée n°17. Ne pas modifier — ajouter une entrée n°18 pour toute rectification.*

---

## Entrée n°18 — 2026-07-19 — Phase 4 : le corps MULTI-NŒUD d'une branche `@if`/`@else` mesuré contre Blazor (CORRECTION)

**Réserve de la n°17 fermée pour les chaînes `@if`/`@else`** : un corps multi-nœud dans **n'importe quelle**
branche d'une chaîne `@if`/`@else if`/`@else` (pas seulement le `@if` sans `else` de la n°17/#98) rejoint le
sous-ensemble compilé (décision #99). L'émission multi-branche est **généralisée** de « un item par branche
(index `i`) » à « un item par **nœud**, chaque branche possédant une **plage d'indices** ». Cela subsume la n°82
(un nœud/branche) et la n°98 (branche unique multi-nœud) **octet pour octet**. Générateur seul, **runtime
INCHANGÉ**. Artefact **fabriqué et mesuré**.

### Validité Blazor vérifiée EN AMONT (la leçon RZ9979)

`dotnet build baseline/IfElseMultiBody.Blazor` **réussit** ; le `BuildRenderTree` donne le contrat : `if (show)`
→ un `AddMarkupContent` (`<span id="a">`) ; le bloc `else` → **deux** `AddMarkupContent` (`<span id="b">`,
`<span id="c">`), adjacents, enfants directs de `#w`, sans conteneur ni nœud texte intercalé.

### Ce qui est mesuré, et pourquoi c'est le générateur

`baseline/IfElseMultiBody.Blazor` : `<div id="w"><button id="toggle">…</button>@if (show) { <span id="a"> }
else { <span id="b"><span id="c"> }</div>`. `show` est **lu par la condition** ET **assigné dans `Toggle`**,
donc **hissé en signal**. La branche `@if` compile en une plage `[0]`, la branche `@else` en une plage `[1, 2]`
(trois fonctions de création `ifBody0_0/1/2`), source `() => (show.value) ? [0] : [1, 2]`, clé identité. Au clic,
la **branche entière** est échangée : `a` sort, `b` ET `c` entrent, dans l'ordre. Un générateur qui n'aurait
monté qu'un nœud de l'`@else`, ou inversé l'ordre, ou laissé `a`, l'oracle le verrait (il lit `#w > span` comme
chaîne d'ids jointe).

### Environnement

- macOS (Darwin 25.5.0, arm64). **.NET SDK 10.0.301**. **Playwright / Chromium 150.0.7871.127**, headless.
  **`HARNESS_VERSION` 1.13.0** — voir la réserve. Blazor Release, `InvariantGlobalization=true`.

### Protocole

Correction seulement (mode `--contract-only`). La branche `ifelsemulti` de `verifyContract` lit `#w > span`
comme chaîne d'ids jointe, exige l'initial `"a"` (branche `@if`), clique `#toggle` et exige `"b,c"` (branche
`@else`, deux nœuds dans l'ordre), reclique et exige `"a"` (retour à la branche `@if`). Le `"b,c"` mesure que la
plage `@else` entière monte, dans l'ordre ; le retour à `"a"` que l'échange est propre des deux côtés.

### Commande pour rejouer

```
(cd bench/harness && npm ci && npx playwright install chromium)
dotnet publish baseline/IfElseMultiBody.Blazor -c Release -o bench/publish/blazor-ifelsemulti
./bench/build-filament.sh filament-ifelsemulti-gen
node bench/harness/bench.mjs --dir bench/publish/blazor-ifelsemulti/wwwroot --app ifelsemulti --label blazor-ifelsemulti       --headless --contract-only
node bench/harness/bench.mjs --dir bench/publish/filament-ifelsemulti-gen   --app ifelsemulti --label filament-ifelsemulti-gen --headless --contract-only
```

### Résultat

| Label | `#w > span` ids : initial → `#toggle` → `#toggle` | verdict |
|---|---|---|
| **blazor-ifelsemulti** (autorité) | `a` → `b,c` → `a` | contrat OK |
| **filament-ifelsemulti-gen** (générateur) | `a` → `b,c` → `a` | contrat OK |

**Les deux rendent `a → b,c → a`, à l'identique.** La branche multi-nœud entière s'échange, dans l'ordre, sans
conteneur — la généralisation à plages d'indices est **mesurée**, pas raisonnée.

### Ce que cette entrée N'établit PAS, et ses réserves

- **CORRECTION seulement**, aucun C1/C3/C4 (app triviale, décision du propriétaire).
- **`HARNESS_VERSION` 1.12.0 → 1.13.0, DIVULGUÉ (n°31/43/59).** `bench.mjs` a changé (branche `ifelsemulti` +
  entrée `APPS`), donc son hash change ; le numéro monte. Aucune mesure de poids antérieure n'est invalidée.
- **RUNTIME INCHANGÉ.** La plage d'indices réemploie `list()` ; `git diff -- src/filament-runtime` est vide. Les
  émissions n°81/n°82/n°98 restent octet pour octet (le chemin rapide de la n°81 est intact ; la n°82 et la n°98
  passent par le chemin général qui reproduit leurs octets).
- **ANCRE-COMMENTAIRE `+1` nœud.** Toujours la seule divergence divulguée (n°81/20).
- **TRANCHE ÉTROITE.** Le contrôle de flux **imbriqué** dans une branche (`IfNested`) reste refusé
  `[unsupported-if-body]` à `(2,1)` ; `Foreach.razor` reste refusé `[unsupported-foreach]` ; les nœuds texte
  intercalés restent refusés. §8 inchangé : RADICAL reste « ni éliminée ni établie ».

---

*Fin de l'entrée n°18. Ne pas modifier — ajouter une entrée n°19 pour toute rectification.*

---

## Entrée n°19 — 2026-07-19 — Phase 4 : le `@if` IMBRIQUÉ dans une branche mesuré contre Blazor (CORRECTION)

**Réserve de la n°17/n°18 fermée pour le contrôle de flux imbriqué** : un `@if (other)` imbriqué dans une
branche `@if (show)` rejoint le sous-ensemble compilé (décision #100). Toute la structure imbriquée est
**aplatie** en un seul `list()` dont la source est un **arbre de décision** (ternaire imbriqué) sur toutes les
conditions, un constructeur par nœud feuille indexé globalement. `IfSourceRanges` (n°99) est remplacé par un
`IfExpr` **récursif** qui **reproduit les octets des n°82/n°98** quand il n'y a pas d'imbrication. Générateur
seul, **runtime INCHANGÉ**. Artefact **fabriqué et mesuré**.

### Validité Blazor vérifiée EN AMONT (la leçon RZ9979)

`dotnet build baseline/IfNested.Blazor` **réussit** ; le `BuildRenderTree` donne le contrat : `if (show) { if
(other) { AddMarkupContent(<span a>) } }` — `#a` présent **ssi** `show && other`, sans conteneur.

### Ce qui est mesuré, et pourquoi c'est le générateur

`baseline/IfNested.Blazor` : `<div id="w">…deux boutons…@if (show) { @if (other) { <span id="a"> } }</div>`.
`show` ET `other` sont lus par des conditions (`MarkConditionReads` descend récursivement) ET assignés, donc
**hissés en signaux**. Le nœud feuille compile en une source arbre-de-décision `() => (show.value) ?
((other.value) ? [0] : []) : []` : le `?:` court-circuité **reproduit l'évaluation du `@if` imbriqué** (`other`
n'est lu que si `show` est vrai, donc l'effet ne s'abonne à `other` que dans ce cas — exactement comme deux
`list()` imbriqués). Deux bascules exercent les **quatre** combinaisons. Un générateur qui aurait ignoré la
condition interne (ou externe) laisserait `#a` visible quand il ne devrait pas — l'oracle le verrait.

### Environnement

- macOS (Darwin 25.5.0, arm64). **.NET SDK 10.0.301**. **Playwright / Chromium 150.0.7871.127**, headless.
  **`HARNESS_VERSION` 1.14.0** — voir la réserve. Blazor Release, `InvariantGlobalization=true`.

### Protocole

Correction seulement (mode `--contract-only`). La branche `ifnested` de `verifyContract` lit la présence de
`#w > span#a`, exige l'initial `"a"` (`show && other`), clique `#tother` (`other=false`) et exige `""` (feuille
partie malgré `show` vrai — **condition interne**), reclique `#tother` → `"a"`, clique `#tshow` (`show=false`) et
exige `""` (feuille partie malgré `other` vrai — **condition externe**), reclique `#tshow` → `"a"`.

### Commande pour rejouer

```
(cd bench/harness && npm ci && npx playwright install chromium)
dotnet publish baseline/IfNested.Blazor -c Release -o bench/publish/blazor-ifnested
./bench/build-filament.sh filament-ifnested-gen
node bench/harness/bench.mjs --dir bench/publish/blazor-ifnested/wwwroot --app ifnested --label blazor-ifnested       --headless --contract-only
node bench/harness/bench.mjs --dir bench/publish/filament-ifnested-gen   --app ifnested --label filament-ifnested-gen --headless --contract-only
```

### Résultat

| Label | `#a` : init → other✗ → other✓ → show✗ → show✓ | verdict |
|---|---|---|
| **blazor-ifnested** (autorité) | `a` → `` → `a` → `` → `a` | contrat OK |
| **filament-ifnested-gen** (générateur) | `a` → `` → `a` → `` → `a` | contrat OK |

**Les deux rendent `a → «» → a → «» → a`, à l'identique.** `#a` suit la conjonction `show && other`, les deux
conditions (interne et externe) mesurées séparément — l'aplatissement en arbre de décision est **mesuré**, pas
raisonné.

### Ce que cette entrée N'établit PAS, et ses réserves

- **CORRECTION seulement**, aucun C1/C3/C4 (app triviale, décision du propriétaire).
- **`HARNESS_VERSION` 1.13.0 → 1.14.0, DIVULGUÉ (n°31/43/59).** `bench.mjs` a changé (branche `ifnested` + entrée
  `APPS`), donc son hash change ; le numéro monte. Aucune mesure de poids antérieure n'est invalidée.
- **RUNTIME INCHANGÉ.** L'arbre de décision réemploie `list()` ; `git diff -- src/filament-runtime` est vide. Les
  émissions n°81/n°82/n°98 restent octet pour octet (25 tests de régression `@if` verts après le changement).
- **ANCRE-COMMENTAIRE `+1` nœud.** Toujours la seule divergence divulguée (n°81/20).
- **TRANCHE ÉTROITE.** Une branche ne peut être QUE tout-markup OU un unique `@if` imbriqué. Une branche
  **mélangeant** markup et `@if` imbriqué (nouveau témoin `IfNestedMixed.razor`, refusé `[unsupported-if-body]` à
  `(2,1)`), **plusieurs** `@if` imbriqués frères, un `@foreach` imbriqué, restent refusés (différés).
  `Foreach.razor` reste refusé `[unsupported-foreach]`. §8 inchangé : RADICAL reste « ni éliminée ni établie ».

---

*Fin de l'entrée n°19. Ne pas modifier — ajouter une entrée n°20 pour toute rectification.*

---

## Entrée n°20 — 2026-07-19 — Phase 4 : la division ENTIÈRE (`int/int` via `Math.trunc`) mesurée contre Blazor (CORRECTION)

**Réserve de la n°9/décision #87 fermée** : `int / int` rejoint le §5 avec un abaissement **fidèle par troncature**
— `a / b` (résultat int) → `Math.trunc(a / b)`, qui tronque vers zéro **exactement** comme la division entière de
C# (`7/2 → 3`, `-7/2 → -3`). La n°87 admettait la division `double` (`/` verbatim, même op IEEE-754) mais
**refusait `int/int`** faute d'abaissement fidèle — `/` nu aurait rendu `3.5` là où C# rend `3` (nombre
silencieusement faux, §10). `Math.trunc` répare cela ; pour des int 32 bits le quotient est exact dans un double
JS. Générateur seul, **runtime INCHANGÉ** (`Math.trunc` est un builtin JS). Ceci est une tranche du **sous-ensemble
C#** (première de la frontière IntDivision → While → Switch → DoWhile), pas du contrôle de flux template.

### Ce qui est mesuré, et pourquoi c'est le générateur

`baseline/DivideInt.Blazor` (calqué sur `Divide.Blazor` mais `int`) : `value = 7`, `Halve()` fait `value = value /
2`. C# tronque à `3` ; un générateur émettant `/` nu rendrait `3.5`. Le générateur émet `value.value =
Math.trunc(value.value / 2)`. L'admission est **dépendante du TYPE** (`IsIntegerDivision`, résultat Int32),
mono-sourcée dans `ConstructSubset` — donc l'analyseur suit (le témoin `IntegerDivision_IsFlagged` devient
`IntegerDivision_IsNotFlagged`, mesuré au temps d'écriture).

### Environnement

- macOS (Darwin 25.5.0, arm64). **.NET SDK 10.0.301**. **Playwright / Chromium 150.0.7871.127**, headless.
  **`HARNESS_VERSION` 1.15.0** — voir la réserve. Blazor Release, `InvariantGlobalization=true`.

### Protocole

Correction seulement (mode `--contract-only`). La branche `divideint` de `verifyContract` lit `#divide-value`,
exige l'initial `"7"`, clique `#halve` et exige `"3"` (troncature entière). Un `"3.5"` signalerait un `/` nu au
lieu de `Math.trunc`.

### Commande pour rejouer

```
(cd bench/harness && npm ci && npx playwright install chromium)
dotnet publish baseline/DivideInt.Blazor -c Release -o bench/publish/blazor-divideint
./bench/build-filament.sh filament-divideint-gen
node bench/harness/bench.mjs --dir bench/publish/blazor-divideint/wwwroot --app divideint --label blazor-divideint       --headless --contract-only
node bench/harness/bench.mjs --dir bench/publish/filament-divideint-gen   --app divideint --label filament-divideint-gen --headless --contract-only
```

### Résultat

| Label | `#divide-value` : initial → `#halve` | verdict |
|---|---|---|
| **blazor-divideint** (autorité) | `7` → `3` | contrat OK |
| **filament-divideint-gen** (générateur) | `7` → `3` | contrat OK |

**Les deux rendent `7 → 3`, à l'identique.** La division entière tronque, `Math.trunc` se comporte comme la
division `int/int` de C# — l'abaissement fidèle est **mesuré**, pas raisonné.

### Ce que cette entrée N'établit PAS, et ses réserves

- **CORRECTION seulement**, aucun C1/C3/C4 (app triviale, décision du propriétaire).
- **`HARNESS_VERSION` 1.14.0 → 1.15.0, DIVULGUÉ.** `bench.mjs` a changé (branche `divideint` + entrée `APPS`).
- **RUNTIME INCHANGÉ.** `Math.trunc` est un builtin JS ; `git diff -- src/filament-runtime` est vide.
- **DIVISION PAR ZÉRO DIVULGUÉE.** C# `int/0` **lève** `DivideByZeroException` ; JS `Math.trunc(a/0)` donne
  `Infinity`. Même catégorie que la divergence de débordement pré-existante `int`→`number` (les doubles JS ne
  bouclent pas à 2^31). Le comportement mesuré (opérandes normaux) est fidèle ; le chemin exceptionnel est un bord
  divulgué et accepté.
- **TRANCHE ÉTROITE.** Seules les divisions `int` et `double`. `long`/`decimal` restent refusées (types hors §5).
  §8 inchangé : RADICAL reste « ni éliminée ni établie ».

---

*Fin de l'entrée n°20. Ne pas modifier — ajouter une entrée n°21 pour toute rectification.*

---

## Entrée n°21 — 2026-07-19 — Phase 4 : les instructions `while`/`do-while`/`switch` mesurées contre Blazor (CORRECTION)

**La FAMILLE d'instructions boucle/branchement entre dans le §5** (décision #102) : `while`, `do-while`, `switch`
(+`break`, requis par `switch`) rejoignent le sous-ensemble C#, chacune vers son homologue JS
(`while`→`while`, `do-while`→`do…while`, `switch`→`switch/case/break`). Tranche combinée « famille d'instructions »
(cf. n°84). Générateur seul, **runtime INCHANGÉ** (ce sont des mots-clés JS). Artefact **fabriqué et mesuré**.

### Ce qui est mesuré, et pourquoi c'est le générateur

`baseline/Loops.Blazor` : trois handlers `@code` — `DoWhile` (boucle `while`, `n → 5`), `DoSwitch` (`switch` à
labels constants + `break`, `n(5) → 9`), `DoDo` (boucle `do…while`, `n → 3`). `n` est lu par le template et
assigné dans les handlers → hissé en signal ; chaque handler écrit `n` plus d'une fois → `batch()` (n°68). Les
labels PATTERN/`when` et les switch-expressions restent refusés (mono-sourcé dans `ConstructSubset`, donc
l'analyseur suit : `WhileLoop_IsFlagged` devient `IsNotFlagged`).

### Environnement

- macOS (Darwin 25.5.0, arm64). **.NET SDK 10.0.301**. **Playwright / Chromium 150.0.7871.127**, headless.
  **`HARNESS_VERSION` 1.16.0** — voir la réserve. Blazor Release, `InvariantGlobalization=true`.

### Protocole

Correction seulement (mode `--contract-only`). La branche `loops` de `verifyContract` lit `#v`, exige l'initial
`"0"`, clique `#bwhile` → `"5"` (while), `#bswitch` → `"9"` (switch case 5), `#bdo` → `"3"` (do-while). Chaque
construction est mesurée par son effet observable sur `n`.

### Commande pour rejouer

```
(cd bench/harness && npm ci && npx playwright install chromium)
dotnet publish baseline/Loops.Blazor -c Release -o bench/publish/blazor-loops
./bench/build-filament.sh filament-loops-gen
node bench/harness/bench.mjs --dir bench/publish/blazor-loops/wwwroot --app loops --label blazor-loops       --headless --contract-only
node bench/harness/bench.mjs --dir bench/publish/filament-loops-gen   --app loops --label filament-loops-gen --headless --contract-only
```

### Résultat

| Label | `#v` : init → while → switch → do | verdict |
|---|---|---|
| **blazor-loops** (autorité) | `0` → `5` → `9` → `3` | contrat OK |
| **filament-loops-gen** (générateur) | `0` → `5` → `9` → `3` | contrat OK |

**Les deux rendent `0 → 5 → 9 → 3`, à l'identique.** Les trois instructions se comportent comme leurs homologues
C# — l'abaissement est **mesuré**, pas raisonné.

### Ce que cette entrée N'établit PAS, et ses réserves

- **CORRECTION seulement**, aucun C1/C3/C4.
- **`HARNESS_VERSION` 1.15.0 → 1.16.0, DIVULGUÉ.** `bench.mjs` a changé (branche `loops` + entrée `APPS`).
- **RUNTIME INCHANGÉ.** `while`/`do`/`switch`/`break` sont des mots-clés JS ; `git diff -- src/filament-runtime`
  est vide. `for`/`foreach`/`if` restent octet pour octet.
- **BOUCLE + SIGNAL.** Une boucle qui mute un signal est fidèle sur le DOM FINAL que l'oracle lit ; d'éventuels
  runs d'effet intermédiaires (si non batchés) n'ajoutent que des rendus, jamais un état final faux.
- **TRANCHE ÉTROITE.** `continue`, `goto`, `goto case`, les instructions étiquetées, les labels PATTERN/`when` de
  `switch`, et les switch-EXPRESSIONS (`x switch { … }`) restent refusés (différés). §8 inchangé : RADICAL reste
  « ni éliminée ni établie ».

---

*Fin de l'entrée n°21. Ne pas modifier — ajouter une entrée n°22 pour toute rectification.*

---

## Entrée n°22 — 2026-07-19 — Phase 4 : l'élargissement des LISTES BLANCHES d'attributs (booléen `hidden` + chaîne `role`/`style`/`data-*`) mesuré contre Blazor (CORRECTION)

**Les réserves « autres noms » des n°14/n°16 fermées** (décision #103) : le booléen `hidden` (+`required`) rejoint
`BooleanAttributes` (présence/absence, comme `disabled`), et les noms chaîne `role`/`style` + le **préfixe `data-*`**
rejoignent la liste blanche des attributs chaîne réactifs. Comme la récolte et l'émission sont **agnostiques au
nom** (n°95/n°97), les deux allowlists sont des changements d'allowlist ; `data-*` est admis par PRÉFIXE (tout nom
`data-*` porte une valeur chaîne, sûr à composer). Générateur seul, **runtime INCHANGÉ**. Tranche combinée
booléen+chaîne. Artefact **fabriqué et mesuré**.

### Ce qui est mesuré, et pourquoi c'est le générateur

`baseline/MoreAttrs.Blazor` : `<span id="s" hidden="@hid" role="@r" style="@st" data-count="@d">`. Le
`BuildRenderTree` (n°64) confirme que Blazor traite `hidden` (bool) comme un attribut booléen présence/absence et
`role`/`style`/`data-count` (chaînes) comme des attributs chaîne. `hidden` compile en
`setAttr(el, 'hidden', hid.value ? '' : null)` (le ternaire présence/absence, n°95) ; les trois autres en
`setAttr(el, nom, x.value)` (l'émission composée, n°94/n°97). Les quatre sont réactifs (lus par le template,
assignés dans `Toggle`) → effets ; `Toggle` écrit quatre champs → `batch()`. `value` reste délibérément exclu
(garde `@bind` refusé).

### Environnement

- macOS (Darwin 25.5.0, arm64). **.NET SDK 10.0.301**. **Playwright / Chromium 150.0.7871.127**, headless.
  **`HARNESS_VERSION` 1.17.0** — voir la réserve. Blazor Release, `InvariantGlobalization=true`.

### Protocole

Correction seulement (mode `--contract-only`). La branche `moreattrs` de `verifyContract` lit `#s` : `hidden` via
`hasAttribute`, `role`/`style`/`data-count` via `getAttribute`. Initial `{hidden:true, role:"alert", style:"color:
red", data:"1"}`, clique `#toggle`, exige `{hidden:false, role:"status", style:"color: blue", data:"2"}`. Le
`hidden:false` mesure le RETRAIT de l'attribut (pas un `hidden="false"`).

### Commande pour rejouer

```
(cd bench/harness && npm ci && npx playwright install chromium)
dotnet publish baseline/MoreAttrs.Blazor -c Release -o bench/publish/blazor-moreattrs
./bench/build-filament.sh filament-moreattrs-gen
node bench/harness/bench.mjs --dir bench/publish/blazor-moreattrs/wwwroot --app moreattrs --label blazor-moreattrs       --headless --contract-only
node bench/harness/bench.mjs --dir bench/publish/filament-moreattrs-gen   --app moreattrs --label filament-moreattrs-gen --headless --contract-only
```

### Résultat

| Label | `#s` hidden/role/style/data-count : initial → `#toggle` | verdict |
|---|---|---|
| **blazor-moreattrs** (autorité) | `présent·alert·color: red·1` → `absent·status·color: blue·2` | contrat OK |
| **filament-moreattrs-gen** (générateur) | `présent·alert·color: red·1` → `absent·status·color: blue·2` | contrat OK |

**Les deux rendent à l'identique.** Le booléen `hidden` passe présent→absent et les trois attributs chaîne
(dont le nom à préfixe `data-count`) suivent l'état — la généralisation des deux listes blanches est **mesurée**.

### Ce que cette entrée N'établit PAS, et ses réserves

- **CORRECTION seulement**, aucun C1/C3/C4.
- **`HARNESS_VERSION` 1.16.0 → 1.17.0, DIVULGUÉ.** `bench.mjs` a changé (branche `moreattrs` + entrée `APPS`).
- **RUNTIME INCHANGÉ.** `setAttr` prend n'importe quel nom ; l'admission n'a touché QUE les allowlists (+ le
  prédicat de préfixe `data-*`). `git diff -- src/filament-runtime` est vide.
- **`value` EXCLU.** `value` reste hors des listes blanches, gardant le `value=` lowered de `@bind` refusé. Les
  témoins de refus ont migré : `hidden→readonly` (`BooleanNotAllowed`), `role→placeholder` (`DynamicRole`,
  `MixedNonAllowed`), toujours refusés à `(1,12)`.
- **FAIBLE NOUVEAUTÉ, DIVULGUÉE.** Aucune forme d'émission nouvelle — preuve mesurée que les deux listes blanches
  se généralisent. Restent différés : `value` (pour @bind), et tout autre nom non mesuré. §8 inchangé : RADICAL
  reste « ni éliminée ni établie ».

---

*Fin de l'entrée n°22. Ne pas modifier — ajouter une entrée n°23 pour toute rectification.*

---

## Entrée n°23 — 2026-07-19 — Phase 4 : la LIAISON BIDIRECTIONNELLE `@bind` (sur un champ chaîne signal) mesurée contre Blazor (CORRECTION)

**`@bind` entre dans le sous-ensemble** (décision #104) : `@bind="text"` sur un `<input>`, pour un champ `text`
**chaîne** qui est **déjà un signal**, rejoint le sous-ensemble. Razor abaisse `@bind` en une paire synthétisée
`value=`/`onchange` (`BindConverter.FormatValue` + `CreateBinder`) ; pour une chaîne le convertisseur est
l'IDENTITÉ, donc le générateur reconnaît le motif et émet **une liaison à DEUX sens** : un effet sur la propriété
`value` (`effect(() => { input.value = text.value; })`) + un écouteur `change` qui écrit le signal
(`listen(input, 'change', e => text.value = e.target.value)`). Générateur seul, **runtime INCHANGÉ**. Artefact
**fabriqué et mesuré**.

### Ce qui est mesuré, et pourquoi c'est le générateur

`baseline/Bind.Blazor` : `<input id="box" @bind="text" />`, un `<span id="echo">@text</span>`, et un bouton `#set`
qui fait `text = "world"`. `text` est **lu par le template** (`@text` ET `@bind`) ET **assigné** (`Set`), donc un
**signal chaîne**. Les DEUX sens sont mesurés : (1) champ→input : `#set` change `text` → l'effet met à jour
`input.value` ; (2) input→champ : un évènement `change` sur l'input → `text` (et `#echo`) suivent. Un générateur
qui n'aurait câblé qu'un sens, ou lié l'ATTRIBUT `value` au lieu de la PROPRIÉTÉ, l'oracle le verrait.

### Environnement

- macOS (Darwin 25.5.0, arm64). **.NET SDK 10.0.301**. **Playwright / Chromium 150.0.7871.127**, headless.
  **`HARNESS_VERSION` 1.18.0** — voir la réserve. Blazor Release, `InvariantGlobalization=true`.

### Protocole

Correction seulement (mode `--contract-only`). La branche `bind` de `verifyContract` lit `#box.value` et
`#echo.textContent`. Initial `{value:"hi", echo:"hi"}`, clique `#set` → `{value:"world", echo:"world"}` (champ→
input), puis met `#box.value="typed"` et dispatche un `change` → `{echo:"typed"}` (input→champ).

### Commande pour rejouer

```
(cd bench/harness && npm ci && npx playwright install chromium)
dotnet publish baseline/Bind.Blazor -c Release -o bench/publish/blazor-bind
./bench/build-filament.sh filament-bind-gen
node bench/harness/bench.mjs --dir bench/publish/blazor-bind/wwwroot --app bind --label blazor-bind       --headless --contract-only
node bench/harness/bench.mjs --dir bench/publish/filament-bind-gen   --app bind --label filament-bind-gen --headless --contract-only
```

### Résultat

| Label | `#box.value`/`#echo` : initial → `#set` → change | verdict |
|---|---|---|
| **blazor-bind** (autorité) | `hi/hi` → `world/world` → `typed/typed` | contrat OK |
| **filament-bind-gen** (générateur) | `hi/hi` → `world/world` → `typed/typed` | contrat OK |

**Les deux rendent à l'identique, dans les DEUX sens.** `#set` propage le champ vers l'input ; un `change` propage
l'input vers le champ (et `#echo`) — la liaison bidirectionnelle est **mesurée**, pas raisonnée.

### Ce que cette entrée N'établit PAS, et ses réserves

- **CORRECTION seulement**, aucun C1/C3/C4.
- **`HARNESS_VERSION` 1.17.0 → 1.18.0, DIVULGUÉ.** `bench.mjs` a changé (branche `bind` + entrée `APPS`).
- **RUNTIME INCHANGÉ.** `effect`/`listen` shippent déjà ; `.value` et `e.target.value` sont des builtins DOM.
  `git diff -- src/filament-runtime` est vide.
- **TRANCHE ÉTROITE.** `@bind` seulement sur un champ **chaîne** qui est **déjà un signal** (le convertisseur est
  l'identité, et exiger un signal établi évite de marquer la réactivité depuis le lowering template). Restent
  différés : `@bind` non-chaîne (int/bool → `BindConverter` parse), `@bind` sur un champ non-lu-ailleurs (marquage
  de réactivité), `@bind:event="oninput"`, `@bind` sur un composant. Le témoin `Bind.razor` (champ non déclaré)
  reste refusé, désormais `[unsupported-bind]` à `(1,24)`. §8 inchangé : RADICAL reste « ni éliminée ni établie ».

---

*Fin de l'entrée n°23. Ne pas modifier — ajouter une entrée n°24 pour toute rectification.*

---

## Entrée n°24 — 2026-07-19 — Phase 4 : le HANDLER LAMBDA en ligne (`() => count++`) mesuré contre Blazor (CORRECTION)

**Le handler lambda sans argument entre dans le sous-ensemble** (décision #105) : `@onclick="() => count++"`
— un lambda **en ligne**, pas un nom de méthode `@code` — rejoint le sous-ensemble. Le corps du lambda est
**enveloppé** en une méthode synthétique `void __filament_lambda_k() { … }` dans la compilation, puis **traduit
par le modèle sémantique** (comme un corps de méthode `@code` : `count++` → `count.value++` quand `count` est un
signal), et émis en `listen(el, 'click', () => { … })` — une flèche émise, PAS une épissure verbatim (la leçon
n°57 : « ce nom est-il un signal ? » se répond par le compilateur, pas par l'orthographe). Générateur seul,
**runtime INCHANGÉ**. Artefact **fabriqué et mesuré**.

### Ce qui est mesuré, et pourquoi c'est le générateur

`baseline/LambdaHandler.Blazor` : un compteur piloté par le lambda en ligne, `<span id="count">@count</span>` +
`<button id="inc" @onclick="() => count++">`. `count` est lu par `@count` ET assigné par le lambda → **hissé en
signal** (le marquage de réactivité passe automatiquement sur la méthode synthétique du lambda). Le clic EST la
mesure : `#count` va `0 → 1 → 2`. Une épissure verbatim (`() => count++` splicé) laisserait `#count` à `0` (le
bouton mort de la n°68). Les formes `e => …` (objet évènement) et `async () => …` restent refusées.

### Environnement

- macOS (Darwin 25.5.0, arm64). **.NET SDK 10.0.301**. **Playwright / Chromium 150.0.7871.127**, headless.
  **`HARNESS_VERSION` 1.19.0** — voir la réserve. Blazor Release, `InvariantGlobalization=true`.

### Protocole

Correction seulement (mode `--contract-only`). La branche `lambdahandler` de `verifyContract` lit `#count`, exige
l'initial `"0"`, clique `#inc` → `"1"`, reclique → `"2"`.

### Commande pour rejouer

```
(cd bench/harness && npm ci && npx playwright install chromium)
dotnet publish baseline/LambdaHandler.Blazor -c Release -o bench/publish/blazor-lambdahandler
./bench/build-filament.sh filament-lambdahandler-gen
node bench/harness/bench.mjs --dir bench/publish/blazor-lambdahandler/wwwroot --app lambdahandler --label blazor-lambdahandler       --headless --contract-only
node bench/harness/bench.mjs --dir bench/publish/filament-lambdahandler-gen   --app lambdahandler --label filament-lambdahandler-gen --headless --contract-only
```

### Résultat

| Label | `#count` : initial → `#inc` → `#inc` | verdict |
|---|---|---|
| **blazor-lambdahandler** (autorité) | `0` → `1` → `2` | contrat OK |
| **filament-lambdahandler-gen** (générateur) | `0` → `1` → `2` | contrat OK |

**Les deux rendent `0 → 1 → 2`, à l'identique.** Le lambda en ligne est **traduit** (`count → count.value`) et
tire au clic — le pont sémantique (envelopper-traduire-émettre) est **mesuré**, pas raisonné.

### Ce que cette entrée N'établit PAS, et ses réserves

- **CORRECTION seulement**, aucun C1/C3/C4.
- **`HARNESS_VERSION` 1.18.0 → 1.19.0, DIVULGUÉ.** `bench.mjs` a changé (branche `lambdahandler` + entrée `APPS`).
- **RUNTIME INCHANGÉ.** `listen` shippe déjà ; une flèche est un builtin JS. `git diff -- src/filament-runtime`
  est vide. Les handlers à nom de méthode (Counter) restent octet pour octet.
- **PONT SÉMANTIQUE, PAS UNE ÉPISSURE.** Le corps du lambda passe par la MÊME compilation + traduction que les
  méthodes `@code`, donc un corps hors sous-ensemble se refuse tout seul (localisation approximative au niveau de
  l'attribut, divulguée). La leçon n°57 (Roslyn, pas la regex) tenue.
- **TRANCHE ÉTROITE.** Lambda **sans argument, non-async** seulement. `e => …` (objet évènement, `HandlerLambdaArgs`)
  et `async () => …` (`HandlerAsync`) **restent refusés** `[compound-expression]`, témoins de bord. §8 inchangé :
  RADICAL reste « ni éliminée ni établie ».

---

*Fin de l'entrée n°24. Ne pas modifier — ajouter une entrée n°25 pour toute rectification.*

---

## Entrée n°25 — 2026-07-19 — Phase 4 : `List<T>.Clear()` mesuré contre Blazor (CORRECTION)

**La dernière opération `List<T>` du §5** (décision #106) : `.Clear()` rejoint `.Add`/`.RemoveAt`/`.Count`/indexation.
Une `List<T>` mappe un tableau vivant + un signal de version (rows.js) ; `.Clear()` vide le tableau **en place**
(`items.length = 0`) et le bump de version re-lance le `list()`, donc `@foreach` se réconcilie à VIDE. Générateur
seul, **runtime INCHANGÉ**. Artefact **fabriqué et mesuré**.

### Ce qui est mesuré

`baseline/ListOps.Blazor` : un `@foreach` sur une `List<string>`, `#add` la peuple (trois `push` → 3 `<li>`),
`#clear` fait `items.Clear()`. La branche `listops` de `verifyContract` clique `#add` (exige 3 `<li>`) puis
`#clear` (exige 0 `<li>`). **`HARNESS_VERSION` 1.19.0 → 1.20.0**, divulgué.

### Commande pour rejouer

```
dotnet publish baseline/ListOps.Blazor -c Release -o bench/publish/blazor-listops
./bench/build-filament.sh filament-listops-gen
node bench/harness/bench.mjs --dir bench/publish/blazor-listops/wwwroot --app listops --label blazor-listops       --headless --contract-only
node bench/harness/bench.mjs --dir bench/publish/filament-listops-gen   --app listops --label filament-listops-gen --headless --contract-only
```

### Résultat

| Label | `#list` <li> : initial → `#add` → `#clear` | verdict |
|---|---|---|
| **blazor-listops** (autorité) | `0 → 3 → 0` | contrat OK |
| **filament-listops-gen** (générateur) | `0 → 3 → 0` | contrat OK |

**Les deux rendent `0 → 3 → 0`, à l'identique.** `Clear()` réconcilie la liste à vide — mesuré. Runtime inchangé
(`git diff -- src/filament-runtime` vide). Le témoin `Gate/ListOp.razor` bascule de refusé à compilé. §8 inchangé.

---

*Fin de l'entrée n°25. Ne pas modifier — ajouter une entrée n°26 pour toute rectification.*

---

## Entrée n°26 — 2026-07-19 — Phase 4 : la liaison `@bind` sur une CASE À COCHER (bool) mesurée contre Blazor (CORRECTION)

**`@bind` s'étend au type `bool` sur une case à cocher** (décision #107) : `@bind="on"` sur un `<input
type="checkbox">`, pour un champ `on` **bool** déjà signal. Razor abaisse en `checked`=FormatValue(on) +
onchange=CreateBinder ; pour un bool le convertisseur est la propriété `.checked` (identité, PAS de parsing → PAS
de bord d'échec de parse). Le générateur émet `effect(() => { input.checked = on.value; })` +
`listen(input, 'change', e => on.value = e.target.checked)`. Générateur seul, **runtime INCHANGÉ**. La voie `@bind`
chaîne (n°104) reste octet pour octet.

### Ce qui est mesuré

`baseline/CheckBind.Blazor` : `@bind` sur une case à cocher ; `on` est **lu par le ternaire de classe `#status`**
(`on ? "on" : "off"` — une chaîne fidèle, là où un `@on` brut rendrait « false » vs « False » de C#) ET assigné
par `@bind`/`Set`, donc **signal bool**. Les DEUX sens : `#set` met `on=true` → `box.checked` et la classe suivent ;
un `change` sur la case → `on` (et la classe) suivent. **`HARNESS_VERSION` 1.20.0 → 1.21.0**, divulgué.

### Résultat

| Label | `#box.checked`/`#status` : init → `#set` → décoche | verdict |
|---|---|---|
| **blazor-checkbind** (autorité) | `false/off → true/on → false/off` | contrat OK |
| **filament-checkbind-gen** (générateur) | `false/off → true/on → false/off` | contrat OK |

**Les deux rendent à l'identique, dans les deux sens.** La liaison bool à deux sens est **mesurée**. `git diff --
src/filament-runtime` vide. §8 inchangé.

---

*Fin de l'entrée n°26. Ne pas modifier — ajouter une entrée n°27 pour toute rectification.*

---

## Entrée n°27 — 2026-07-19 — Phase 4 : la liaison `@bind` sur un ENTIER (parse fidèle) mesurée contre Blazor (CORRECTION)

**`@bind` s'étend au type `int`** (décision #108) : `@bind="count"` sur un `<input>` texte, `count` un int déjà
signal. Contrairement à chaîne/bool, ceci **PARSE** : le handler de `change` **reproduit `int.TryParse`**
(invariant, `NumberStyles.Integer`) — une regex `/^\s*[+-]?\d+\s*$/` pour la forme acceptée, un contrôle de
plage int32, et un **revert-sur-invalide** qui GARDE le champ et re-rend l'ancienne valeur. La valeur se formate
via `String(count.value)` ; `@count` (int) s'affiche fidèlement. Générateur seul, **runtime INCHANGÉ**. Les voies
chaîne (n°104) et bool (n°107) restent octet pour octet.

### Ce qui est mesuré — Y COMPRIS LES BORDS

`baseline/IntBind.Blazor`. La branche `intbind` de `verifyContract` teste QUATRE entrées : `#set` (champ→input,
`0→42`), une entrée VALIDE (`"7"` → champ=7), une entrée INVALIDE (`"notanumber"` → **revert**, champ garde 7), et
un DÉBORDEMENT (`"99999999999"` > int.MaxValue → **revert**, champ garde 7). Tester l'invalide et le débordement EST
la mesure : c'est ce qui vérifie que la regex + plage + revert reproduit `BindConverter` exactement.
**`HARNESS_VERSION` 1.21.0 → 1.22.0**, divulgué.

### Résultat

| Label | `#box.value`/`#echo` : init → set → « 7 » → « notanumber » → « 99999999999 » | verdict |
|---|---|---|
| **blazor-intbind** (autorité) | `0/0 → 42/42 → 7/7 → 7/7 → 7/7` | contrat OK |
| **filament-intbind-gen** (générateur) | `0/0 → 42/42 → 7/7 → 7/7 → 7/7` | contrat OK |

**Les deux rendent à l'identique, invalide ET débordement compris.** L'entrée invalide et le débordement
**reviennent à 7** dans les DEUX — le parse fidèle à `int.TryParse` est **mesuré**, pas raisonné. C'est
précisément le bord (« un nombre silencieusement faux ») que la §10 interdit, et l'oracle prouve qu'il n'existe pas
ici. `git diff -- src/filament-runtime` vide. §8 inchangé.

---

*Fin de l'entrée n°27. Ne pas modifier — ajouter une entrée n°28 pour toute rectification.*

---

## Entrée n°28 — 2026-07-19 — Phase 4 : le bloc de code racine `@{ }` (déclaration locale) mesuré contre Blazor (CORRECTION)

**Un bloc `@{ }` à la racine déclarant une LOCALE** (décision #109) : `@{ int total = 3 + 4; }` lu par `@total`
rejoint le sous-ensemble. Une déclaration locale s'exécute **UNE FOIS** dans `mount()` (là où l'arbre est construit)
— c'est un `const total = 3 + 4;` unique, pas « une instruction sans lieu où tourner ». Le read `@total` résout la
locale (statique : elle ne change jamais → pas d'effet). Générateur seul, **runtime INCHANGÉ**. Une déclaration
locale devient un `CodeOp` dans `RegionOps` ; toute AUTRE instruction reste refusée.

### Ce qui est mesuré

`baseline/CodeBlock.Blazor` : `@{ int total = 3 + 4; }` + `<span id="out">@total</span>`. Statique (comme compose) :
le rendu initial EST la mesure. La branche `codeblock` de `verifyContract` exige `#out` = `"7"`. **`HARNESS_VERSION`
1.22.0 → 1.23.0**, divulgué.

### Résultat

| Label | `#out` | verdict |
|---|---|---|
| **blazor-codeblock** (autorité) | `7` | contrat OK |
| **filament-codeblock-gen** (générateur) | `7` | contrat OK |

**Les deux rendent `7`, à l'identique.** La locale du bloc `@{ }` s'exécute et `@total` la lit — mesuré.
`git diff -- src/filament-runtime` vide. Le témoin `RootCodeBlock.razor` bascule de refusé à compilé ; une
instruction non-déclarative à la racine reste refusée. §8 inchangé.

---

*Fin de l'entrée n°28. Ne pas modifier — ajouter une entrée n°29 pour toute rectification.*

---

## Entrée n°29 — 2026-07-19 — Phase 4 : `try`/`catch`/`throw`/`lock` mesurés contre Blazor (CORRECTION)

**Les instructions `try`/`catch`/`finally`, `throw` et `lock`** (décision #110) rejoignent le sous-ensemble §5,
mappées sur leurs formes JS :

- `try`/`catch`/`finally` → l'homonyme JS (un `catch` sans liaison → un `catch {}` sans liaison).
- `throw new Exception(msg)` → `throw new Error(msg)` : `Exception.Message` (C#) et `Error.message` (JS) portent
  la même chaîne. `new Exception(...)` est la SEULE création d'objet admise en §5 (`IsExceptionCreation`) —
  tout autre `new` reste refusé (un module Filament n'a pas de BCL). Un `throw` **INTERCEPTÉ** est fidèle ;
  un `throw` **NON intercepté** est le bord divulgué (C# fait remonter une exception .NET, JS un `Error` nu) et
  n'est PAS ce que cette app mesure.
- `lock (x) { … }` → un bloc **NU** `{ … }` : JS est mono-thread, donc un verrou ne peut jamais être disputé et
  la cible du verrou (`this`) est abandonnée — elle n'a aucun sens dans la fermeture `mount()`.

Générateur seul, **runtime INCHANGÉ** : ce sont trois formes JS ordinaires, aucune nouvelle primitive.

### Ce qui est mesuré

`baseline/TryLock.Blazor` : le gestionnaire `#go` **LANCE** dans un `try`, l'**INTERCEPTE** (`+5`), puis exécute
un corps de `lock` (`+1`) — donc chaque clic ajoute 6. `count` est lu par `@count` et assigné DEUX fois par `Go`,
donc c'est un signal ET le gestionnaire batch (#68). La branche `trylock` de `verifyContract` clique `#go` deux fois
et exige `#count` : `0 → 6 → 12`. **`HARNESS_VERSION` 1.23.0 → 1.24.0**, divulgué.

```
dotnet publish baseline/TryLock.Blazor -c Release -o bench/publish/blazor-trylock
./bench/build-filament.sh filament-trylock-gen
node bench/harness/bench.mjs --dir bench/publish/blazor-trylock/wwwroot --app trylock --label blazor-trylock       --headless --contract-only
node bench/harness/bench.mjs --dir bench/publish/filament-trylock-gen   --app trylock --label filament-trylock-gen --headless --contract-only
```

### Résultat

| Label | `#count` | verdict |
|---|---|---|
| **blazor-trylock** (autorité) | `0` → `6` → `12` | contrat OK |
| **filament-trylock-gen** (générateur) | `0` → `6` → `12` | contrat OK |

**Les deux vont à `6` puis `12`, à l'identique.** Le `try` a lancé, le `catch` a intercepté (`+5`) et le corps du
`lock` a tourné (`+1`) — Blazor exécute le même C# jusqu'à exactement 6 par clic. `git diff -- src/filament-runtime`
vide. Les témoins `Code/TryCatch.razor`, `Code/Throw.razor`, `Code/Lock.razor` basculent de refusés à compilés ;
`using` et `goto` restent refusés (pas d'`IDisposable` à disposer, pas d'abaissement de `goto` étiqueté). §8 inchangé.

---

*Fin de l'entrée n°29. Ne pas modifier — ajouter une entrée n°30 pour toute rectification.*

---

## Entrée n°30 — 2026-07-19 — Phase 4 : le RECORD POSITIONNEL mesuré contre Blazor (CORRECTION)

**Un record POSITIONNEL** (décision #111) : `record Item(string Name, int Rank)` rejoint le §5. Un record
positionnel est la MÊME forme de données qu'un record à corps, écrite plus court, donc il compile vers le MÊME
littéral objet (`{ name, rank }`) — son constructeur/`Equals`/`GetHashCode`/`Deconstruct` générés sont
simplement inutilisés (une forme en lecture seule ; le sous-ensemble n'admet ni égalité de valeur ni
déconstruction). La construction est EN LIGNE et mappe les arguments positionnels aux propriétés par ORDRE DU
CONSTRUCTEUR : `new Item("alpha", 1)` → `{ name: 'alpha', rank: 1 }`. C'est la première construction d'objet-
record admise en position d'expression (avant #111, un record ne se construisait QUE via le repli du site de
construction `new Row(); row.X = …`, jamais en ligne). Générateur seul, **runtime INCHANGÉ**.

### Ce qui est mesuré

`baseline/PositionalRecord.Blazor` : un `@foreach` sur une `List<Item>` amorcée avec un item construit en ligne
(`new List<Item> { new Item("alpha", 1) }`) ; `#add` en ajoute un second construit en ligne dans `.Add(new
Item("beta", 2))`. `items` est lu par `@foreach` ET muté par `Add` → signal de version (chaque `Item` en lecture
seule → jamais signal). La branche `positionalrecord` de `verifyContract` clique `#add` et exige `#list` :
`["alpha: 1"]` → `["alpha: 1", "beta: 2"]`. **`HARNESS_VERSION` 1.24.0 → 1.25.0**, divulgué.

```
dotnet publish baseline/PositionalRecord.Blazor -c Release -o bench/publish/blazor-positionalrecord
./bench/build-filament.sh filament-positionalrecord-gen
node bench/harness/bench.mjs --dir bench/publish/blazor-positionalrecord/wwwroot --app positionalrecord --label blazor-positionalrecord       --headless --contract-only
node bench/harness/bench.mjs --dir bench/publish/filament-positionalrecord-gen   --app positionalrecord --label filament-positionalrecord-gen --headless --contract-only
```

### Résultat

| Label | `#list` | verdict |
|---|---|---|
| **blazor-positionalrecord** (autorité) | `["alpha: 1"]` → `["alpha: 1", "beta: 2"]` | contrat OK |
| **filament-positionalrecord-gen** (générateur) | `["alpha: 1"]` → `["alpha: 1", "beta: 2"]` | contrat OK |

**Les deux rendent une puis deux `<li>`, à l'identique.** Le record positionnel devient un littéral objet et sa
construction en ligne est mappée par ordre du constructeur — Blazor exécute le même C#. Le chemin PHARE Rows
(record à corps, repli du site de construction) reste octet pour octet (snapshot Rows vert, changement additif).
`git diff -- src/filament-runtime` vide. Le témoin `Code/RecordDecl.razor` bascule de refusé à compilé ; un record
portant une MÉTHODE (`Code/RecordMember.razor`) reste refusé (c'est du comportement, pas une forme). §8 inchangé.

---

*Fin de l'entrée n°30. Ne pas modifier — ajouter une entrée n°31 pour toute rectification.*

---

## Entrée n°31 — 2026-07-20 — Phase 4 : le type `long` (→ BigInt) mesuré contre Blazor (CORRECTION)

**Le type `long`** (décision #112) rejoint le §5, dont le foyer JS est **BigInt**, pas `number`. Deux raisons de
fidélité : (1) l'affichage entier de BigInt est EXACT au-delà de 2⁵³ (= 9007199254740992), là où un double JS
perd de la précision ; (2) la division BigInt tronque vers zéro EXACTEMENT comme `long`/`long` en C# (`7n / 2n`
= 3n, `-7n / 2n` = -3n). Un littéral entier en contexte long devient un littéral BigInt (`5` → `5n`) ; l'élargissement
implicite int→long devient `BigInt(x)`, long→double devient `Number(x)` (JS ne peut mélanger BigInt et number).
Le DOM COERCE un BigInt en sa chaîne décimale exacte quand `setText` assigne `node.data` — donc **runtime INCHANGÉ**
(`setText` existe déjà). Générateur seul. Bord divulgué : le débordement à 2⁶³ (C# boucle, BigInt non).

### Ce qui est mesuré

`baseline/LongCounter.Blazor` : `total` (un `long`) part de 9007199254740990 (juste sous 2⁵³) ; chaque `#add`
ajoute 3, donc après un clic `total` vaut 9007199254740993 — une valeur qu'un double NE PEUT PAS tenir (il
l'arrondirait à …992). `total` est lu par `@total` ET assigné par `Add` → signal (de BigInt). La branche
`longcounter` de `verifyContract` clique `#add` deux fois et exige `#value` :
`9007199254740990 → 9007199254740993 → 9007199254740996`. **`HARNESS_VERSION` 1.25.0 → 1.26.0**, divulgué.

```
dotnet publish baseline/LongCounter.Blazor -c Release -o bench/publish/blazor-longcounter
./bench/build-filament.sh filament-longcounter-gen
node bench/harness/bench.mjs --dir bench/publish/blazor-longcounter/wwwroot --app longcounter --label blazor-longcounter       --headless --contract-only
node bench/harness/bench.mjs --dir bench/publish/filament-longcounter-gen   --app longcounter --label filament-longcounter-gen --headless --contract-only
```

### Résultat

| Label | `#value` | verdict |
|---|---|---|
| **blazor-longcounter** (autorité) | `…990 → …993 → …996` | contrat OK |
| **filament-longcounter-gen** (générateur) | `…990 → …993 → …996` | contrat OK |

**Les deux rendent `9007199254740993` puis `…996`, à l'identique.** C'est la mesure QUI PROUVE le mapping : la
valeur franchit 2⁵³, donc une implémentation adossée à `number` aurait rendu `9007199254740992` (le double le plus
proche) et ÉCHOUÉ le contrat contre Blazor — BigInt la rend exactement, comme le `long` C# de Blazor. `git diff --
src/filament-runtime` vide (le DOM coerce le BigInt lui-même). Les témoins `Code/TypeLong.razor` (champ long) et
`TypeSubset`/`List<long>` basculent de refusés à compilés ; `float`/`decimal`/`DateTime` restent refusés (pas de
représentation JS fidèle). §8 inchangé.

---

*Fin de l'entrée n°31. Ne pas modifier — ajouter une entrée n°32 pour toute rectification.*

---

## Entrée n°32 — 2026-07-20 — Phase 4 : le type `float` (→ Math.fround) mesuré contre Blazor (CORRECTION)

**Le type `float`** (décision #113) rejoint le §5, mappé sur un nombre JS **arrondi à la simple précision par
`Math.fround`**. Deux moitiés de fidélité, chacune VÉRIFIÉE empiriquement contre C# : (1) **l'arithmétique** — C#
calcule le `float` en simple précision et arrondit à CHAQUE opération, donc chaque op est enveloppée dans
`Math.fround` (un littéral est stocké arrondi) ; `(a+b)*c` arrondit la somme interne d'abord, exactement comme
`Math.fround(Math.fround(a+b)*c)`. (2) **l'affichage** — un `float` est un double arrondi dont la coercion nue
imprimerait la chaîne DOUBLE (`0.1f` → "0.10000000149011612"), pas la chaîne float de C# ("0.1") ; il passe donc
par un formateur `__f32` qui trouve la plus COURTE décimale qui fait l'aller-retour à travers float32 — exactement
`float.ToString` de C#. `__f32` est ÉMIS dans le module (pas un export runtime), donc **runtime INCHANGÉ**.

### Ce qui est mesuré

`baseline/FloatCounter.Blazor` : `total` (un `float`) part de 0.1f ; chaque `#add` ajoute 0.2f. En `float` C#,
0.1f + 0.2f = 0.3f (imprimé "0.3") — mais un double JS calcule 0.1 + 0.2 = 0.30000000000000004. `total` est lu par
`@total` ET assigné par `Add` → signal. La branche `floatcounter` de `verifyContract` clique `#add` deux fois et
exige `#value` : `"0.1" → "0.3" → "0.5"`. **`HARNESS_VERSION` 1.26.0 → 1.27.0**, divulgué.

```
dotnet publish baseline/FloatCounter.Blazor -c Release -o bench/publish/blazor-floatcounter
./bench/build-filament.sh filament-floatcounter-gen
node bench/harness/bench.mjs --dir bench/publish/blazor-floatcounter/wwwroot --app floatcounter --label blazor-floatcounter       --headless --contract-only
node bench/harness/bench.mjs --dir bench/publish/filament-floatcounter-gen   --app floatcounter --label filament-floatcounter-gen --headless --contract-only
```

### Résultat

| Label | `#value` | verdict |
|---|---|---|
| **blazor-floatcounter** (autorité) | `"0.1" → "0.3" → "0.5"` | contrat OK |
| **filament-floatcounter-gen** (générateur) | `"0.1" → "0.3" → "0.5"` | contrat OK |

**Les deux rendent `"0.3"` puis `"0.5"`, à l'identique.** C'est la mesure QUI PROUVE le mapping : une
implémentation adossée à `number` aurait rendu `"0.30000000000000004"` et ÉCHOUÉ le contrat contre Blazor — le
`Math.fround` par opération PLUS le formateur `__f32` reproduisent le `float` C# exactement. `git diff --
src/filament-runtime` vide (le formateur est émis dans le module). Le témoin `Code/TypeFloat.razor` (champ float)
et `TypeSubset`/`float` basculent de refusés à compilés ; `decimal`/`DateTime` restent refusés (pas de type natif /
pas de BCL). §8 inchangé.

---

*Fin de l'entrée n°32. Ne pas modifier — ajouter une entrée n°33 pour toute rectification.*

---

## Entrée n°33 — 2026-07-20 — Phase 4 : le type `decimal` (→ { m, s } boxé) mesuré contre Blazor (CORRECTION)

**Le type `decimal`** (décision #114) rejoint le §5, mappé sur un **objet boxé `{ m: mantisse BigInt, s: échelle }`**.
C#'s decimal est un type base-10 128 bits qui SUIT L'ÉCHELLE (1.10m garde son zéro final — c'est mantisse 110,
échelle 2). JS n'a AUCUN type décimal natif, donc une valeur decimal est cette paire, et son arithmétique passe
par les helpers `__dec*` (base-10 exacte) : `__decAdd` aligne les échelles et additionne les mantisses ; `__decStr`
rend la mantisse avec la virgule à `s`, zéros finaux préservés. Ils reproduisent System.Decimal pour +, -, *, la
comparaison et l'affichage ; la DIVISION (arrondi à 28-29 chiffres significatifs) est refusée, pas émise. Seuls les
helpers RÉELLEMENT utilisés sont émis (ici `__decAdd`, `__decStr`), dans le module — donc **runtime INCHANGÉ**.

### Ce qui est mesuré

`baseline/DecimalCounter.Blazor` : `total` (un `decimal`) part de 1.10m ; chaque `#add` ajoute 1.05m. En C#
decimal, 1.10m + 1.05m = 2.15m puis 3.20m — le zéro final PRÉSERVÉ et la somme base-10 EXACTE (un double donnerait
"1.1" puis 3.2000000000000002). `total` est lu par `@total` ET assigné par `Add` → signal. La branche
`decimalcounter` de `verifyContract` clique `#add` deux fois et exige `#value` : `"1.10" → "2.15" → "3.20"`.
**`HARNESS_VERSION` 1.27.0 → 1.28.0**, divulgué.

```
dotnet publish baseline/DecimalCounter.Blazor -c Release -o bench/publish/blazor-decimalcounter
./bench/build-filament.sh filament-decimalcounter-gen
node bench/harness/bench.mjs --dir bench/publish/blazor-decimalcounter/wwwroot --app decimalcounter --label blazor-decimalcounter       --headless --contract-only
node bench/harness/bench.mjs --dir bench/publish/filament-decimalcounter-gen   --app decimalcounter --label filament-decimalcounter-gen --headless --contract-only
```

### Résultat

| Label | `#value` | verdict |
|---|---|---|
| **blazor-decimalcounter** (autorité) | `"1.10" → "2.15" → "3.20"` | contrat OK |
| **filament-decimalcounter-gen** (générateur) | `"1.10" → "2.15" → "3.20"` | contrat OK |

**Les deux rendent `"1.10"` puis `"2.15"` puis `"3.20"`, à l'identique.** C'est la mesure QUI PROUVE le mapping :
une implémentation adossée à `number` échouerait DÈS `"1.10"` (elle rend "1.1" — le zéro final est perdu) et donnerait
3.2000000000000002 ; le `{ m, s }` boxé plus l'arithmétique base-10 reproduisent le `decimal` C# exactement — le zéro
final ET l'exactitude. `git diff -- src/filament-runtime` vide (les helpers sont émis dans le module). Les témoins
`Code/TypeDecimal.razor` et `decimal`/`List<decimal>` basculent de refusés à compilés ; `object`/`DateTime` restent
refusés (`object` n'est pas typé ; `DateTime` n'a pas de BCL). §8 inchangé.

---

*Fin de l'entrée n°33. Ne pas modifier — ajouter une entrée n°34 pour toute rectification.*

---

## Entrée n°34 — 2026-07-20 — Phase 4 : le type `DateTime` (→ ticks BigInt) mesuré contre Blazor (CORRECTION)

**Le type `DateTime`** (décision #115) rejoint le §5, mappé sur un **BigInt de ticks** (100ns depuis 0001-01-01).
Un DateTime C# EST un compte de ticks 64 bits, qu'un BigInt tient exactement. La construction `new DateTime(y,m,d)`
est calculée à la GÉNÉRATION à partir des arguments CONSTANTS (le générateur construit le DateTime dans son propre
runtime et lit `.Ticks`) ; `.AddDays(n)` (n constant int) ajoute n·TicksPerDay ; la comparaison est un compare
BigInt (gratuit) ; l'affichage par défaut rend le `"MM/dd/yyyy HH:mm:ss"` de C# via un formateur `__dtStr` émis
(ticks → ms epoch → Date UTC). Sous-ensemble ÉTROIT et honnête : `DateTime.Now` est refusé (NON déterministe →
non mesurable contre Blazor), l'arithmétique `dt - dt` (TimeSpan) refusée, les autres membres (Add*, propriétés,
ToString(format)) différés. `__dtStr` est émis dans le module → **runtime INCHANGÉ**.

### Ce qui est mesuré

`baseline/DateTimeCounter.Blazor` : `when` (un `DateTime`) part de `new DateTime(2026, 7, 20)` ; chaque `#add`
l'avance de 5 jours (`when.AddDays(5)`). `when` est lu par `@when` ET assigné par `Add` → signal. La branche
`datetimecounter` de `verifyContract` clique `#add` deux fois et exige `#value` :
`"07/20/2026 00:00:00" → "07/25/2026 00:00:00" → "07/30/2026 00:00:00"`. **`HARNESS_VERSION` 1.28.0 → 1.29.0**, divulgué.

```
dotnet publish baseline/DateTimeCounter.Blazor -c Release -o bench/publish/blazor-datetimecounter
./bench/build-filament.sh filament-datetimecounter-gen
node bench/harness/bench.mjs --dir bench/publish/blazor-datetimecounter/wwwroot --app datetimecounter --label blazor-datetimecounter       --headless --contract-only
node bench/harness/bench.mjs --dir bench/publish/filament-datetimecounter-gen   --app datetimecounter --label filament-datetimecounter-gen --headless --contract-only
```

### Résultat

| Label | `#value` | verdict |
|---|---|---|
| **blazor-datetimecounter** (autorité) | `07/20 → 07/25 → 07/30 (2026, 00:00:00)` | contrat OK |
| **filament-datetimecounter-gen** (générateur) | `07/20 → 07/25 → 07/30 (2026, 00:00:00)` | contrat OK |

**Les deux avancent `07/20 → 07/25 → 07/30`, à l'identique**, avec le rollover de calendrier correct et le format
par défaut fidèle. C'est la mesure QUI PROUVE le mapping : une coercion nue du BigInt afficherait le nombre de ticks
brut, pas la date. `git diff -- src/filament-runtime` vide (le formateur est émis dans le module). Les témoins
`Code/TypeDateTime.razor` et `DateTime`/`List<DateTime>` basculent de refusés à compilés ; `object` reste refusé
(non typé — aucune représentation JS fidèle). §8 inchangé.

---

*Fin de l'entrée n°34. Ne pas modifier — ajouter une entrée n°35 pour toute rectification.*

---

## Entrée n°35 — 2026-07-20 — Phase 4 : LINQ sur une List mesuré contre Blazor (CORRECTION)

**Les opérateurs LINQ courants sur une `List`** (décision #116) rejoignent le §5, mappés sur les méthodes de
tableau JS : `Where` → `filter`, `Select` → `map`, `Count` → `length`, `Any` → `some`, `All` → `every`,
`ToList`/`ToArray` → le tableau. Une List EST déjà un tableau JS matérialisé (décision rows.js 1), donc les méthodes
EAGER de JS sont fidèles à une chaîne LINQ qui se termine par un scalaire (Count/Any/All) ou ToList. Le lambda
prédicat `x => x > 0` se traduit par la MÊME machinerie que le reste de @code (son paramètre est un local ordinaire,
qu'`Identifier` résout par son nom), donc il reste `x => x > 0`. **AUCUNE primitive runtime ajoutée** : ce sont des
méthodes de tableau JS pures. Le reste de LINQ (GroupBy, OrderBy, Range, surcharges d'agrégat) est refusé.

### Ce qui est mesuré

`baseline/Linq.Blazor` : `_nums = [-2, 3, -1, 5, 0]` ; `#go` fait `count = _nums.Where(x => x > 0).Count()` →
`_nums.filter(x => x > 0).length` = 2. `count` est lu par `@count` ET assigné par `Go` → signal ; `_nums` n'est
jamais muté → tableau nu. La branche `linq` de `verifyContract` clique `#go` et exige `#value` : `"0" → "2"`.
**`HARNESS_VERSION` 1.29.0 → 1.30.0**, divulgué.

```
dotnet publish baseline/Linq.Blazor -c Release -o bench/publish/blazor-linq
./bench/build-filament.sh filament-linq-gen
node bench/harness/bench.mjs --dir bench/publish/blazor-linq/wwwroot --app linq --label blazor-linq       --headless --contract-only
node bench/harness/bench.mjs --dir bench/publish/filament-linq-gen   --app linq --label filament-linq-gen --headless --contract-only
```

### Résultat

| Label | `#value` | verdict |
|---|---|---|
| **blazor-linq** (autorité) | `"0" → "2"` | contrat OK |
| **filament-linq-gen** (générateur) | `"0" → "2"` | contrat OK |

**Les deux comptent `2` positifs, à l'identique.** `_nums.Where(x => x > 0).Count()` devient
`_nums.filter(x => x > 0).length` — Blazor exécute le même LINQ. `git diff -- src/filament-runtime` vide (méthodes
de tableau pures, aucun helper). Le témoin `Gate/Linq.razor` bascule de refusé à compilé ; `Enumerable.Range` et le
reste de LINQ restent refusés (`Code/Linq.razor` : Range → une locale `IEnumerable`, refusée à son type). §8 inchangé.

---

*Fin de l'entrée n°35. Ne pas modifier — ajouter une entrée n°36 pour toute rectification.*

---

## Entrée n°36 — 2026-07-20 — Phase 4 : le tableau `T[]` (indexation) mesuré contre Blazor (CORRECTION)

**Le type tableau `T[]`** (décision #117) rejoint le §5, mappé sur le MÊME tableau JS qu'une `List<T>` — la seule
différence est la mutabilité : un tableau est de taille fixe, donc admis EN LECTURE SEULE (indexation, `.Length`,
itération ; l'assignation d'élément `arr[i] = v` est REFUSÉE — une collection mutable est une `List<T>`, dont
l'écriture d'élément incrémente la version réactive). Un littéral `new int[]{…}` → un littéral tableau JS
`[…]` ; `@items[i]` → `items[i]` (l'indexeur propre du tableau) ; `.Length` → `.length`. **AUCUNE primitive
runtime** : indexation et `.length` sont celles du tableau JS. Tableaux dimensionnés `new int[n]` (sans
initialiseur) et multi-dimensionnels différés.

### Ce qui est mesuré

`baseline/ArrayIndex.Blazor` : `items = new int[]{10,20,30}` (tableau constant) indexé par `i` (un signal) ;
`#next` avance `i` (mod `items.Length`), donc `@items[i]` parcourt le tableau. `items` est lu mais jamais muté →
tableau nu ; `i` est lu ET assigné → signal. La branche `arrayindex` de `verifyContract` clique `#next` et exige
`#value` : `"10" → "20" → "30" → "10"`. **`HARNESS_VERSION` 1.30.0 → 1.31.0**, divulgué.

```
dotnet publish baseline/ArrayIndex.Blazor -c Release -o bench/publish/blazor-arrayindex
./bench/build-filament.sh filament-arrayindex-gen
node bench/harness/bench.mjs --dir bench/publish/blazor-arrayindex/wwwroot --app arrayindex --label blazor-arrayindex       --headless --contract-only
node bench/harness/bench.mjs --dir bench/publish/filament-arrayindex-gen   --app arrayindex --label filament-arrayindex-gen --headless --contract-only
```

### Résultat

| Label | `#value` | verdict |
|---|---|---|
| **blazor-arrayindex** (autorité) | `"10" → "20" → "30" → "10"` | contrat OK |
| **filament-arrayindex-gen** (générateur) | `"10" → "20" → "30" → "10"` | contrat OK |

**Les deux parcourent `10 → 20 → 30 → 10`, à l'identique.** `@items[i]` indexe le tableau `[10,20,30]` par l'index
réactif — Blazor indexe le même tableau C#. `git diff -- src/filament-runtime` vide (indexeur JS natif). Le témoin
`Code/TypeArray.razor` bascule de refusé à compilé ; `object`/`Dictionary` restent refusés. §8 inchangé.

---

*Fin de l'entrée n°36. Ne pas modifier — ajouter une entrée n°37 pour toute rectification.*

---

## Entrée n°37 — 2026-07-20 — Phase 4 : le `Dictionary<K,V>` (→ Map JS) mesuré contre Blazor (CORRECTION)

**Le type `Dictionary<K,V>`** (décision #118) rejoint le §5, mappé sur une **`Map` JS**, admis EN LECTURE SEULE
(comme un tableau : pas de signal de version, donc l'écriture d'entrée `d[key] = v` est REFUSÉE). Construction
`new Dictionary<K,V>(){ {k,v}, … }` → `new Map([[k, v], …])` ; `@d[key]` → `d.get(key)` ; `.Count` → `.size` ;
`.ContainsKey(k)` → `.has(k)`. Admis quand K et V sont des scalaires (clé scalaire : `Map` utilise SameValueZero,
qui correspond au défaut C# pour les types primitifs ; une clé record diverge par identité). **AUCUNE primitive
runtime** : `Map` est un builtin JS. Add/Remove/TryGetValue différés.

### Ce qui est mesuré

`baseline/DictLookup.Blazor` : `labels = {1:"one", 2:"two", 3:"three"}` (Map constante) indexée par `key` (un
signal) ; `#next` avance `key`, donc `@labels[key]` parcourt la Map. `labels` lu mais jamais muté → Map nue ;
`key` lu ET assigné → signal. La branche `dictlookup` de `verifyContract` clique `#next` et exige `#value` :
`"one" → "two" → "three" → "one"`. **`HARNESS_VERSION` 1.31.0 → 1.32.0**, divulgué.

```
dotnet publish baseline/DictLookup.Blazor -c Release -o bench/publish/blazor-dictlookup
./bench/build-filament.sh filament-dictlookup-gen
node bench/harness/bench.mjs --dir bench/publish/blazor-dictlookup/wwwroot --app dictlookup --label blazor-dictlookup       --headless --contract-only
node bench/harness/bench.mjs --dir bench/publish/filament-dictlookup-gen   --app dictlookup --label filament-dictlookup-gen --headless --contract-only
```

### Résultat

| Label | `#value` | verdict |
|---|---|---|
| **blazor-dictlookup** (autorité) | `"one" → "two" → "three" → "one"` | contrat OK |
| **filament-dictlookup-gen** (générateur) | `"one" → "two" → "three" → "one"` | contrat OK |

**Les deux parcourent `one → two → three → one`, à l'identique.** `@labels[key]` fait `labels.get(key)` sur la Map —
Blazor indexe le même `Dictionary` C#. `git diff -- src/filament-runtime` vide (Map est un builtin JS). Le témoin
`Code/TypeDict.razor` bascule de refusé à compilé ; `object` reste refusé. §8 inchangé.

---

*Fin de l'entrée n°37. Ne pas modifier — ajouter une entrée n°38 pour toute rectification.*
