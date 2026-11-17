# Recept: MALÁ PRAHA – testovací projekt pro stavbu města

> **Účel:** nový samostatný Unity projekt se **skutečným výřezem Prahy ze stejných dat (~1,46 × 1,25 km kolem Národního divadla)**.
> Výřez obsahuje **každý prvek města aspoň jednou, včetně těžkých případů** (ověřeno na OSM).
> V Malé Praze se vyladí stavba a úpravy.
> Hotové moduly se pak **předají zpět** do velkého projektu *PrahaOpenWorld* a tam se pustí na celé město.
>
> Jak recept použít: do nové konverzace Claude Code vlož **jediný prompt z kap. 9**. Projekt, data i stavbu zařídí Claude sám.
> Mluv s ní česky.

---

## 1. Princip: skutečný výřez Prahy, menší území, objekty 1 : 1

- **Skutečná data, ne vymyšlená čtvrť.**
  - Malá Praha je **reálný výřez Prahy** stažený **stejnými skripty a ze stejných zdrojů** jako velké město:
    - OSM (ulice, tramvaje, metro, značky, schody, podniky);
    - IPR Praha (3D budovy LOD2, mosty, stromy, fotomesh na barvy fasád);
    - Metropolitní plán a vrstevnice (terén).
  - Co tu funguje, funguje i na celém městě. Chyby dat (nepřesné OSM, mosty IPR, svahy) se objeví už tady.
- **Stejný počátek souřadnic jako velké město** (`ORIGIN` = střed Karlova mostu).
  - Bod v Malé Praze má **stejné x, z** jako ve velkém projektu, takže snímky, testovací body i opravy dat jdou přenést 1 : 1.
- **Objekty mají skutečnou velikost (1 : 1).**
  - Auto 4,3 m, člověk 1,75 m, rozchod 1 435 mm, schod 16 cm, obrubník 12 cm, patro 3,4 m.
  - „Menší měřítko“ znamená **menší území** (asi 1/6 plochy velkého projektu), ne zmenšené modely. Zmenšenina by rozbila fyziku i chůzi a nic by nešlo přenést.
- **Rychlá iterace.** Start < 5 s (geometrie zapečená v editoru), ≥ 90 FPS (rezerva pro velké město).

## 2. Založení projektu (krok za krokem)

1. **Unity Hub → New project → Universal 3D (URP)**, verze **6000.6.9f1** (stejná jako PrahaOpenWorld).
   - Název `MalaPraha`, složka `C:\Users\dominik\documents\Hry\MalaPraha`.
2. **Balíčky** (Window → Package Manager, nebo zkopíruj řádky do `Packages/manifest.json`):
   - `com.unity.pipeline` 1.0.0 (Claude Code přes něj ovládá editor: script eval, snímky);
   - `com.unity.inputsystem` 1.20.1;
   - `com.unity.render-pipelines.universal` 17.6.0;
   - `com.unity.cinemachine` 3.1.7;
   - `com.unity.ugui` 2.6.0 (TextMeshPro).
   - **Nepřidávej** cloud služby (cloudsave, remote-config, authentication). V testovacím projektu jen zdržují a sypou chyby.
3. **Project Settings:**
   - **Player:** Active Input Handling = *Input System Package (New)*.
   - **Editor:** Enter Play Mode Options zapnuto a **Reload Domain vypnuto** (stejně jako ve velkém projektu, rychlejší play). Statické proměnné se pak musí resetovat přes `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]`.
   - **Quality:** jen jedna úroveň (PC). URP asset: stíny 4 kaskády do 150 m, SSAO zapnuté, HDR, MSAA 4×.
   - **Tags & Layers:** vrstvy shodné s velkým projektem, aby šly moduly přenést:
     - 8 = `Car`, 9 = `Body`, 10 = `Ground`, 11 = `Building`, 12 = `Water`, 13 = `Interior`.
4. **Struktura složek:**
   ```
   Assets/
     PrahaCore/            ← VŠE, co se bude předávat zpět (asmdef Praha.Core, namespace Praha)
       Data/               schéma dat, načítání JSON, Height/Surface API
       Surface/            jednotný model povrchů (terén, chodník, obrubník, vozovka, most)
       Roads/              pruhy, značení, koleje, značky, semafory
       Buildings/          budovy, fasády, sokly, dveře, výlohy
       Landmarks/          významné budovy (prefaby + umístění z dat)
       Interiors/          interiéry podniků a budov, vstup/výstup
       People/             chodci, pěší síť, MHD nástupy
       Vehicles/           auta, tramvaje, metro, vlaky, lodě (modely + řízení)
       Traffic/            síť, AI, semafory, parkování
       Transit/            linky, zastávky, metro stanice, přístaviště
       Config/             ScriptableObject nastavení (žádná magická čísla v kódu)
     PrahaCore.Editor/     bakery (pečení geometrie do assetů), validátory (asmdef Praha.Core.Editor)
     MalaPraha/            jen věci tohoto testbedu: scéna, testovací scénáře, debug UI
       Scenes/MalaPraha.unity
       Tests/              PlayMode testy (Unity Test Framework)
     Resources/              PrahaMap.json, Ipr*.bytes, PrahaSteps/Signs/Rail.json (stažený výřez)
   Tools/                  Python + Blender skripty z PrahaOpenWorld (fetch_*.py, make_vehicles.py, …)
   Docs/PREDANI.md         co se mění a jak to přenést (kap. 8)
   ```
5. **Git:** `git init`, `.gitignore` pro Unity (Library, Temp, Logs, UserSettings), vlastní GitHub repo. Commit po každé fázi.
6. **Data výřezu:** zkopíruj celou složku `Tools/` z PrahaOpenWorld a stáhni data výřezu. Výstupy padají do `Assets/Resources/` nového projektu.
   ```bash
   export PRAHA_BBOX="14.401,50.0745,14.4215,50.0858"   # Malá Praha (kap. 3)
   python3 Tools/fetch_map.py        # → PrahaMap.json  (ulice, terén, tramvaje, metro, budovy OSM, podniky)
   python3 Tools/add_courtyards.py   # dvory budov
   python3 Tools/fetch_ipr3d.py      # → IprBuildings/IprFootprints/IprTrees.bytes (IPR 3D, CC BY)
   python3 Tools/fetch_steps.py      # → PrahaSteps.json
   python3 Tools/fetch_signs.py      # → PrahaSigns.json
   python3 Tools/fetch_rail.py       # → PrahaRail.json (ve výřezu prázdné, viz kap. 3.3)
   python3 Tools/sample_colors.py && python3 Tools/color_tones.py   # barvy fasád z IPR fotomeshe
   ```
   Mosty IPR (`IprBridges.bytes`) stahuje `fetch_ipr3d.py` nebo je zkopíruj z velkého projektu; jsou ve stejných souřadnicích.
   `PRAHA_BBOX` čte `fetch_map.py`; ostatní skripty berou `BBOX` z něj.
   User-Agent posílej `PrahaOpenWorld-game/0.1`, žádné osobní údaje.
7. **Převzetí kódu z PrahaOpenWorld** (repo, větev `worktree-phase7-property`):
   - `Tools/make_vehicles.py`, `Tools/make_people.py` + `Assets/Resources/People/*` (lidé CC0), `Tools/make_textures.py`;
   - z kódu jako *inspirace*, ne kopie 1 : 1: `HumanMotion` (procedurální chůze a sed), `Net` + A* a `Driver` (AI aut), `CarController`, `Vehicles.Glass` (průhledné URP sklo), `GameClock` + hodinové profily, `Places` (otevírací doby).
   - **Nepřebírat** `MapBuilder.Ribbon` a lifty povrchů (kořen všech švů, viz `Docs/PREDANI_MESTO.md` kap. 5).
8. **Blender 4.2.3** (portable, `…\Hry\tools\blender-4.2.3-windows-x64\blender.exe -b --factory-startup --python <skript>`) na modely aut, MHD, lidí a landmarků.

---

## 3. Výřez Malé Prahy: „Národní divadlo – most Legií – ostrovy – Tančící dům – Národní třída – Újezd“

**BBOX (WGS84): lon 14.401–14.4215, lat 50.0745–50.0858.** V herních souřadnicích je to x −741 … +723, z −1 323 … −74, tedy **≈ 1,46 × 1,25 km**. Velký projekt má ≈ 4,9 × 2,4 km, takže výřez je asi 1/6 plochy.

Ověřeno dotazem na OSM 17. 11. 2026. Ve výřezu je:

| Prvek | Kde (herní x, z) | Počet / pozn. |
|---|---|---|
| **Vltava**, nábřeží, náplavka, mola, jez, plavební komora | Smetanovo, Masarykovo, Rašínovo nábřeží; komora Mánes | 18 mol (OSM `man_made=pier`), výletní lodě, šlapadla |
| **Ostrovy** | Střelecký (−149, −549), Slovanský + Žofín (94, −826), Dětský | lávky, park, šlapadla u Slovanského |
| **Mosty** | most Legií (−99, −583): tramvaj + auta + chodci; Jiráskův most (−27, −1157): auta | **nájezdy na most** = hlavní test povrchů |
| **Tramvaje** | Národní, Újezd, Myslíkova, Lazarská, Karlovo náměstí, nábřeží | 97 úseků trati, **13 zastávek** (Národní divadlo, Újezd, Karlovo nám., Lazarská…), odbočení na křižovatkách |
| **Metro** | Národní třída B (609, −682), Karlovo náměstí B (473, −1235) | 2 stanice, vstupy, vestibuly, eskalátory |
| **Lanovka na Petřín** | Újezd (−484, −605) | bonus: svah + kolejová lanovka |
| **Významné budovy** | Národní divadlo (144, −605), Nová scéna (244, −627), Tančící dům (194, −1224), Mánes, palác Žofín, Museum Kampa | modely podle fotek + interiéry (foyer, hlediště, galerie, sál Žofína) |
| **Podniky** | Národní, Spálená, Myslíkova, Újezd, Karmelitská | **453** hospod, kaváren, restaurací a barů (kavárna **Slavia** (159, −549)), **390** obchodů |
| **Křižovatky a značky** | Národní × Smetanovo nábř., Národní × Spálená, Jiráskovo nám., Karlovo nám. | **48 semaforů, 302 přechodů**, značky z OSM |
| **Svah a schody** | úpatí Petřína, Újezd, Malá Strana | **124 schodišť** (OSM `highway=steps`) |
| **Parky** | Kampa (−277, −218), Karlovo náměstí (544, −1102), ostrovy | stromy z IPR |
| **Staré uličky** | okolí Betlémského náměstí (473, −163), Náprstkova, Karoliny Světlé | kostky, jednosměrky, úzké chodníky |
| **Hotspoty chodců** | Národní třída, most Legií, Kampa, náplavka | dav turistů a místních |

### 3.1 Co v reálných datech výřezu otestuješ

| Těžký případ (chyby z velkého projektu) | Konkrétní místo |
|---|---|
| Nájezd na most bez skoku (auto, tramvaj, chodec) | most Legií z obou stran, Jiráskův most |
| Koleje vždy navrchu, auto přes ně plynule | Národní (kolejový pás), křižovatka Národní × Smetanovo nábř. (oblouky) |
| Povrchy se nesmí prolínat | křižovatky na Národní, Karlovo náměstí (velká plocha + park + tramvaj) |
| Budovy na svahu bez zaboření, okna ke střeše | Újezd, úpatí Petřína, Malá Strana |
| Značky vpravo ve směru jízdy | jednosměrky Starého Města, Spálená |
| Chodci po chodnících a přechodech, ne přes vozovku | Národní, Spálená, Karlovo náměstí |
| Nástup chodců do tramvaje, metra a lodě | zastávka Národní divadlo, metro Národní třída, mola na náplavce |
| Dav v metru chodí a má kolize | stanice Národní třída |
| Vstup do podniku a do landmarku | Slavia, Národní divadlo, Tančící dům (galerie, bar nahoře) |
| Výkon v hustém místě | Národní třída v poledne |

### 3.2 Doprava ve výřezu (z dat)

- **Tramvaje:** jen úseky linek uvnitř výřezu.
  - Na hranici výřezu tramvaj **zmizí do portálu** (zajede za hranici, počká a vrátí se s jinou linkou).
  - Čísla linek podle reality (Národní: 2, 9, 18, 22, 23; Újezd: 9, 12, 15, 20, 22, 23…). Ověř na OSM `route=tram`.
- **Auta:** síť ulic z OSM. Na hranici výřezu jsou spawn a despawn body (auto přijede a odjede „z města“).
- **Metro:** linka B mezi stanicemi Národní třída a Karlovo náměstí. Tunel za hranicí končí portálem.
- **Lodě:** mola na náplavce a u ostrovů, plavební komora Mánes.

### 3.3 Volitelné rozšíření: železnice

Hlavní železnice ve výřezu není. Pro test vlaků posuň jižní hranici na **lat 50.0690**, asi o 600 m. Tím se přidá **železniční most na Výtoni** (Vyšehrad – Smíchov) a zastávka Praha-Výtoň. Udělej to až ve fázi F6/F7, ať je výřez zpočátku co nejmenší.

## 4. Data: stejné soubory jako velké město + pár nových polí

Builder v `PrahaCore` čte **stejné soubory jako velký projekt** (`PrahaMap.json`, `Ipr*.bytes`, `PrahaSteps.json`, `PrahaSigns.json`, `PrahaRail.json`). Malá Praha je jen jiný `BBOX`. Klíče `PrahaMap.json` zachovej přesně:

```json
{
  "credits": "…", "origin": [lat, lon], "bbox": [...], "riverLevel": …,
  "terrain": { … výšková mapa 8 m … },
  "streets":   [ { "p": [x,z, x,z, …], "w": 14.0, "foot": false } ],
  "squares":   [ { "n": "…", "p": [ … ] } ], "parks": [ … ], "districts": [ … ],
  "trams":     [ { "p": [ … ] } ], "metro": { … }, "signals": [ { "p": [x,z] } ],
  "tramStops": [ { "n": "Národní divadlo", "p": [x,z] } ],
  "buildings": [ { "p": [ … ], "h": 18.0 } ],
  "places":    [ { "n": "Kavárna Slavia", "k": "cafe", "p": [x,z] } ]
}
```

Nová pole doplň do `Tools/fetch_map.py`. Bude to **stejná změna pro velké město**, které ji pak jen znovu stáhne. Zdroj je v závorce:

```json
"streets":   [ { …, "lanes": [2,2] (OSM lanes:forward/backward), "oneway": false, "tram": "center",
                 "sidewalk": [3.0, 3.0] (sidewalk=*, šířka z MPP nebo odhad), "surface": "asphalt|sett|paving" (surface=*),
                 "parking": ["parallel","none"] (parking:lane:*), "kerb": 0.12 } ],
"bridges":   [ { "street": 12, "s0": 140.0, "s1": 225.0, "deck": 9.5 } ]        (IPR Mosty3D + OSM bridge=yes),
"crossings": [ { "p": [x,z], "street": 4, "signal": true } ]                    (highway=crossing),
"signs":     [ { "code": "P4", "p": [x,z], "facing": 90.0, "side": "right" } ]  (traffic_sign + direction),
"landmarks": [ { "id": "narodni_divadlo", "osm": 123, "p": [x,z], "yaw": 0.0, "enter": [[x,z]] } ],
"places":    [ { …, "door": [x,z], "facing": 180.0, "interior": "pub" } ]      (entrance=* uzel nejblíž POI),
"piers":     [ { "p": [ … ] } ]                                                 (man_made=pier)
```

Pravidla:
- Žádný modul nesmí znát „Malou Prahu“. Všechno bere z dat a ze `ScriptableObject` konfigurace.
- Kde jsou data špatně (OSM chyba, chybějící `lanes`), oprav to **v Python skriptu jako pravidlo nebo override soubor** (`Tools/overrides.json`, klíč OSM id). Neopravuj to ručně ve scéně, jinak se oprava na velké město nepřenese.
- Rozměry podle reality: pruh 3,25 m, chodník 2–4 m, obrubník 0,12 m, patro 3,4 m, tramvajová zastávková hrana 0,22 m.

## 5. Jednotný povrchový model (nejdůležitější část)

Ve velkém projektu vzniká většina chyb tím, že **každá vrstva si počítá výšku sama**:
- povrchy se prolínají;
- koleje jsou pod silnicí;
- na nájezdu na most je schod;
- domy jsou zapadlé.

V Malé Praze to musí být takto:

1. `Surface` modul vyrobí **jednu souvislou „hotovou“ plochu** (finished grade). Terén, vozovka, chodník, obrubník, nájezdy a mostovky jsou **jeden mesh na dlaždici** se sdílenými vrcholy.
   - Obrubník je skutečný stupeň (vozovka −12 cm proti chodníku).
   - U přechodů a vjezdů je obrubník snížený.
2. Jediné API pro výšku: `Surface.HeightAt(x, z, layer)`, kde `layer` je Road, Walk nebo Ground. Používají ho **všichni** (auta, chodci, značky, koleje, budovy, stromy, parkování).
3. **Vrstvy jsou materiály a decaly, ne nové plochy nad terénem.** Vodorovné značení, koleje, kanály a záplaty jsou URP decaly nebo meshe přilepené ke stejné ploše. Koleje jsou nízký mesh žlábkových kolejnic zarovnaný s vozovkou (+0–5 mm) a nemají kolider.
4. **Most** je součást téže sítě: nájezd = plynulá rampa ≤ 6 % do mostovky. Model mostu (oblouky, pilíře, zábradlí) je jen vizuál pod a kolem.
5. **Budovy** dostanou sokl k nejnižšímu bodu půdorysu. Přízemí se zarovná k nejvyššímu bodu chodníku, schody ke dveřím dorovnají rozdíl.
6. Vše statické se **peče v editoru do assetů** (`PrahaCore.Editor/Bake`). Za běhu se nic negeneruje, takže start je rychlý a geometrie stabilní.

---

## 6. Pořadí stavby (fáze) a akceptační testy

Každá fáze končí: commit, 1–2 snímky, numerický test (PlayMode test nebo script eval) a záznam do `Docs/PREDANI.md`.

| Fáze | Obsah | Akceptační test (číslo, ne pocit) |
|---|---|---|
| F0 | Projekt, složky, asmdef, stažení dat výřezu (kap. 2 bod 6), prázdná scéna s debug kamerou | data se načtou; validátor schématu bez chyb; počty prvků odpovídají tabulce v kap. 3 |
| F1 | **Povrch**: terén, vozovky, chodníky, obrubníky, nájezdy, mostovka | 0 míst, kde se dvě plochy liší o < 2 cm (z-fighting test na mřížce 0,5 m); auto přes most: svislé zrychlení < 3 m/s² |
| F2 | Značení, koleje, značky, semafory, lampy, stromy, mobiliář | značky vždy vpravo ve směru jízdy (test úhlem); koleje = vozovka ± 5 mm |
| F3 | Budovy: fasády po patrech, sokly, dveře, výlohy, střechy | 0 budov s mezerou > 5 cm nad nebo pod terénem; okna nikdy přes římsu |
| F4 | Významné budovy (modely podle fotek) + jejich interiéry | vstup E → interiér → výstup; vzdálenost dveří od dat < 0,5 m |
| F5 | Podniky: 6 typů interiérů, otevírací doby, vstup | v 12:00 jde vejít do všech otevřených; v 3:00 zavřeno (kromě nonstop) |
| F6 | Vozidla podle fotek: 5 aut + policie, 15T, T3, metro, vlak (+ rozšíření kap. 3.3), loď; sklo, interiér, řidič, LOD | LOD1 ≤ 400 trojúhelníků; 25 aut + MHD ≤ 3 ms CPU |
| F7 | Doprava: pruhy, semafory, přednost, parkování, MHD jízdní řády | 10 min simulace: 0 nárazů do budov, < 1 náraz auto–auto |
| F8 | Chodci: pěší síť (chodníky, přechody, schody, zóny), cíle, nástup do MHD | 0 chodců na vozovce mimo přechod; ≥ 90 % nástupů úspěšných |
| F9 | Hráč (model stejného stylu jako NPC), vstup do aut a MHD, kamera | hráč sedí v autě; projde všechny dveře z F4 a F5 |
| F10 | Den a noc, světla, timecycle, zvuky CC0, post-processing | snímky v 8:00, 13:00, 20:00 a 2:00; ≥ 90 FPS v poledne na Národní třídě |

---

## 7. Výkonový rozpočet (Malá Praha)

- **CPU:** PlayerLoop ≤ 11 ms (≥ 90 FPS) na Národní třídě v poledne.
- **GPU:** ≤ 1,5 M trojúhelníků a ≤ 1 500 draw callů včetně stínů.
- **Postavy:**
  - Chodci: GPU instancing nebo sdílený atlas materiálů. Skinned jen do 60 m, dál animované impostory.
  - Řidiči jen do 45 m.
- **Auta:** LOD0 (~3–5 k trojúhelníků), LOD1 (~400), LOD2 = impostor. Zaparkovaná auta jsou sloučená po dlaždicích a stíny mají jen do 60 m.
- **Měření:**
  - CPU přes `ProfilerRecorder` („PlayerLoop“).
  - GPU přes `UnityStats` (trojúhelníky, drawCalls, setPassCalls).
  - Nikdy neměř `1/deltaTime` během script evalu.

---

## 8. Předání zpět do velkého projektu

Hotová fáze se předává takto:

1. **Kód** je jen ve složce `Assets/PrahaCore/` (+ `PrahaCore.Editor/`), namespace `Praha`, asmdef `Praha.Core`.
   - Žádné závislosti na `Assets/MalaPraha/`.
   - Žádné natvrdo zapsané souřadnice ani názvy ulic.
2. **Assety** (modely, textury, materiály, prefaby) jsou ve stejné složce. Blender modely mají i zdrojové skripty v `Tools/`.
3. **`Docs/PREDANI.md`**, pro každý modul:
   ```
   ## Modul: Surface (F1)            stav: hotovo / rozpracováno
   Co dělá:                          …
   Vstupní data (klíče JSON):        streets[].sidewalk, kerb, bridges…
   Co je nutné doplnit do PrahaMap:  (např. fetch_map.py: OSM tag sidewalk=*, lanes=*)
   Konfigurace (ScriptableObject):   SurfaceConfig.asset – hodnoty a proč
   Výkon v Malé Praze:               x ms CPU, y trojúhelníků
   Testy:                            názvy PlayMode testů + výsledky
   Známá omezení:                    …
   Snímky:                           Docs/img/F1_*.png
   ```
4. **Postup přenosu** (udělám já ve velkém projektu):
   - zkopírovat `PrahaCore`;
   - rozšířit `fetch_map.py` o nová pole;
   - přepnout `MapBuilder` na nové moduly modul po modulu;
   - pustit stejné testy na výřezu skutečné Prahy;
   - teprve potom na celé město.

---

## 9. Jediný prompt pro novou konverzaci

Spusť Claude Code ve složce `C:\Users\dominik\documents\Hry` (ve WSL `/mnt/c/Users/dominik/documents/Hry`) a vlož tento prompt. Zbytek udělá Claude sám.

```
Postav úplně sám nový Unity projekt „Malá Praha“ – skutečný výřez Prahy, na kterém se ladí
stavba města, než se přenese do velkého projektu PrahaOpenWorld. Mluv se mnou česky.

Podklady jsou ve velkém repozitáři, větev worktree-phase7-property (čti je přes git, ne z worktree):
  git -C /mnt/c/Users/dominik/documents/Hry/PrahaOpenWorld show worktree-phase7-property:Docs/RECEPT_MALA_PRAHA.md
  git -C /mnt/c/Users/dominik/documents/Hry/PrahaOpenWorld show worktree-phase7-property:Docs/PREDANI_MESTO.md
  git -C /mnt/c/Users/dominik/documents/Hry/PrahaOpenWorld show worktree-phase7-property:Docs/GTA5_research.md

Postup:
1. Přečti RECEPT_MALA_PRAHA.md a PREDANI_MESTO.md celé. Recept je zadání, PREDANI_MESTO.md jsou chyby
   a poučení z velkého projektu – neopakuj je.
2. Založ projekt C:\Users\dominik\documents\Hry\MalaPraha (Unity 6000.6.9f1, URP) přes unity CLI
   a nastav ho podle kap. 2 receptu (balíčky, Project Settings, vrstvy, složky, asmdef, git init + .gitignore).
   Oba dokumenty ulož do MalaPraha/Docs/.
3. Převezmi z větve worktree-phase7-property složku Tools/ a Assets/Resources/People/
   (git archive worktree-phase7-property Tools Assets/Resources/People | tar -x -C <MalaPraha>)
   a stáhni data výřezu s PRAHA_BBOX="14.401,50.0745,14.4215,50.0858" (kap. 2 bod 6).
4. Stav fáze F0–F10 podle kap. 5–7. Každá fáze končí numerickým akceptačním testem, 1–2 snímky
   ze hry (sám je prohlédni a oprav, co nesedí s reálnými fotkami Prahy), commitem a zápisem
   do Docs/PREDANI.md ve formátu z kap. 8. Pokračuj fázi za fází bez čekání na mě; zastav se jen,
   když potřebuješ něco, co nezvládneš sám (přihlášení, licence, zásadní rozhodnutí).
5. Pravidla: přenositelný kód jen v Assets/PrahaCore (namespace Praha); nic natvrdo na souřadnice,
   vše z dat výřezu; opravy dat v Python skriptech (Tools/overrides.json); vzhled podle reálných fotek
   Prahy (jen jako reference, respektuj licence), modely v Blenderu
   (/mnt/c/Users/dominik/documents/Hry/tools/blender-4.2.3-windows-x64/blender.exe) nebo CC0;
   výkon ≥ 90 FPS; externí API s User-Agent „PrahaOpenWorld-game/0.1“ a bez mých osobních údajů.
   Do repozitáře PrahaOpenWorld nic nezapisuj ani nepushuj.
6. Na konci každé fáze mi napiš krátce česky, co je hotové, co ne a jaká čísla vyšla.
```
