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
