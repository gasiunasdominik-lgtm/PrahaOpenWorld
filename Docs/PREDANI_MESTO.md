# STOVĚŽATÁ – předávací dokument: co stavíme, co je rozbité, co je pomalé, jak to udělat pořádně

> **Pro koho:** pro novou konverzaci (Claude Code), která má postavit **menší výřez Prahy, ale pořádně a se všemi prvky města**.
> Tento dokument shrnuje zhruba tři dny práce na celém městě (15.–17. 11. 2026). Stavíme hru *PrahaOpenWorld* („STOVĚŽATÁ“).
> Popisuje, co funguje, co ne a proč. Hlavně popisuje, které zkratky se nevyplatily.
> **Mluv s uživatelem česky.**

---

## 0. TL;DR pro novou konverzaci

1. **Celé město se generuje procedurálně z dat** (OSM, IPR Praha, DEM) v jednom obřím skriptu `MapBuilder.cs`.
   - Pokrytí je skvělé, ale v detailu je to „skoro správně“ všude a „úplně správně“ nikde.
   - Uživatel si stěžuje hlavně na **švy**:
     - povrchy se prolínají;
     - nájezd na most má schod;
     - značky stojí na špatné straně;
     - okna nesedí ke střechám;
     - domy jsou zapadlé do terénu;
     - chybí dveře;
     - lidé v metru stojí jako duchové.
2. **Nová úloha:** vezmi **malý výřez** (doporučení v kap. 6) a postav ho **jako ručně řízený „hero“ blok**.
   - Data mají sloužit jako podklad, ne jako finální geometrie.
   - Každý prvek dodělej na úroveň „definition of done“ z kap. 7.
3. **Jeden výškový model pro všechno** (terén, chodník, obrubník, vozovka, koleje, most) je nejdůležitější architektonické rozhodnutí.
   - Většina současných chyb vzniká tím, že každá vrstva si počítá výšku sama.
   - Viz kap. 5.1.
4. **Výkonový rozpočet je ≥ 60 FPS.**
   - Současné celé město dělá asi 44 FPS: herní smyčka trvá 22,8 ms, ve vrcholu je ~6 M trojúhelníků a ~6 200 draw callů.
   - Bez skinned řidičů a s rozumným LOD je to v pohodě. Viz kap. 4.

---

## 1. Co stavíme (vize)

- **Open-world hra v Praze v měřítku 1 : 1.** Výřez sahá od Hradčan po Žižkov. Žánr je „městský ekosystém“: město žije samo a hráč je jen malá část.
- **NPC:** chodci, auta, tramvaje, metro, vlaky, lodě, holubi. Řídí se denní dobou (hodinové profily podle TSK/IPR).
- **Zakázky:** taxi a kurýr. NPC je umějí přebrat.
- **Policie a svědci, reputace podle čtvrtí, vlastnictví nemovitostí.**
- **Vizuální cíl:** realismus, „nesmí to vypadat levně“. Vše má být podle reálných fotek Prahy, které slouží jako reference, ne jako asset.
  - Autorská práva respektuj.
  - Modely dělej sám (Blender je k dispozici) nebo používej CC0 (Kenney, Quaternius, Poly Haven, ambientCG).
- **Uživatelova přání, která zatím nejsou splněná:**
  - Do podniků jde vejít a vypadají podle svého účelu (hospoda, kavárna, trafika, potraviny…).
  - Do významných budov jde vejít, interiér má být podle fotek. Jde o Tančící dům, Národní technické muzeum, Národní divadlo, Rudolfinum, Obecní dům, Národní muzeum…
  - Významné budovy mají být rozpoznatelné, ne generické domy.
  - Chodci chodí po chodnících a přes přechody. Kam dojede auto, tam dojde i chodec.
  - Chodci nastupují do tramvají, metra a lodí.
  - Lepší model hráče (teď je to kreslená postavička, viz snímek `Screenshots/car1.png`).
  - Realistické zvuky (CC0).
  - Techniky z GTA V (`Docs/GTA5_research.md`): timecycle, vertex AO, color grading, decaly špíny, světelné karty, LOD/SLOD, mokré silnice.

---

## 2. Technický stack a pracovní postup

| Věc | Hodnota |
|---|---|
| Engine | Unity **6000.6.9f1**, URP |
| Domain reload | **vypnutý** → statické cache resetuj přes `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]` |
| Ovládání editoru | Unity Pipeline CLI: `unity.exe` (script eval, screenshot). Pomocné skripty byly v `$CLAUDE_JOB_DIR/tmp/w.sh` (funkce `ev` = C# eval, `cap NAME` = snímek game view do `Screenshots/NAME.png`). V nové konverzaci si je napiš znovu. |
| Python | `~/.venvs/vscht/bin/python` (Pillow, numpy) |
| Blender | 4.2.3 portable: `/mnt/c/Users/dominik/documents/Hry/tools/blender-4.2.3-windows-x64/blender.exe -b --factory-startup --python <WIN cesta ke skriptu>`. **Proměnné prostředí z WSL se nepředají**, pokud nejsou v `WSLENV` (např. `PREVIEW=1 ONLY=Estate WSLENV=PREVIEW:ONLY blender.exe …`). |
| Git | Pracuje se ve worktree `.claude/worktrees/<jméno>`, větev `worktree-<jméno>`. **Nikdy nepushuj na main** (merguje uživatel). Push jde přes Windows git: `"/mnt/c/Program Files/Git/cmd/git.exe" --git-dir='C:\Users\dominik\documents\Hry\PrahaOpenWorld\.git' push origin <větev>` (WSL git nemá přihlašovací údaje). |
| Commity | Patička `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>` |
| Externí API | User-Agent `PrahaOpenWorld-game/0.1`; žádné osobní údaje uživatele do requestů. |

### Tipy k editoru (stály hodiny)

- **Kompilace:**
  - Plný překlad trvá až **~100 s**. Před vstupem do play mode počkej na `EditorApplication.isCompiling == false`, jinak běží stará verze.
  - Chyby kompilace: `EditorUtility.scriptCompilationFailed`. `Logs/Editor.log` obsahuje i **staré** chybové řádky, takže samotný grep klame.
- **Editor zmizel** (hláška „No Pipeline instance“): `unity.exe projects open '<win cesta>' --until-ready`.
- **Snímky:**
  - Kamera na snímky (klon hlavní kamery) **zdvojuje cenu renderu**. Před měřením FPS ji znič.
  - Kameru dávej na výšku terénu +1,7 m, jinak skončí pod terénem.
- **Měření FPS:** každé volání `ev` hru na chvíli zastaví, takže `1/smoothDeltaTime` lže.
  - Měř přes `ProfilerRecorder` („PlayerLoop“).
  - Případně přes `Time.frameCount` za ≥ 12 s.
- **Detaily C# a Unity 6:**
  - TMP: `textWrappingMode = TextWrappingModes.NoWrap` (`enableWordWrapping` je chyba).
  - Unity 6.6: `GetInstanceID()` je chyba, použij `GetHashCode()`.
  - Vnořená třída `MapBuilder.Metro` (data) koliduje s globální třídou `Metro`, proto uvnitř `MapBuilder` piš `global::Metro`.
- **Paralelní Unity editory nad jedním projektem nejdou.** Worktree je vhodný pro Python, dokumentaci a nezávislé skripty. Hraní testuj v jediném editoru.

---

## 3. Architektura současného kódu (Assets/Scripts, ~8 900 řádků)

### 3.1 Datová pipeline (Tools/ → Assets/Resources/)

| Skript | Výstup | Zdroj |
|---|---|---|
| `fetch_map.py` | `PrahaMap.json` (3,2 MB): ulice (polyline, šířka, foot), budovy OSM (footprint, holes, výška), terén (DEM mřížka), řeka, tramvaje, metro, semafory, POI | OSM Overpass + IPR vrstevnice + Metropolitní plán (mp.iprpraha.cz ArcGIS) |
| `fetch_ipr3d.py` | `IprBuildings.bytes` (16 MB): LOD2 budovy | IPR 3D (I3S + atributy), CC BY |
| `fetch_ipr_mesh.py`, `sample_colors.py`, `color_tones.py` | `IprColors.bytes`, `IprTones.bytes`, `Tex/Facade0..23.jpg` | IPR fotorealistický mesh → barva fasády a střechy pro každou budovu (k-means na 24 + 12 tónů) |
| (IPR) | `IprBridges.bytes`, `IprTrees.bytes`, `IprFootprints.bytes` | IPR Mosty3D, stromy, půdorysy |
| `fetch_steps.py` | `PrahaSteps.json` | OSM highway=steps |
| `fetch_rail.py` | `PrahaRail.json` | OSM railway=rail + stanice |
| `fetch_signs.py` | `PrahaSigns.json` (3 405 záznamů: 1 833 jednosměrek, 1 469 přechodů, 84 P4, 18 P6…) | OSM highway=crossing/give_way/stop, traffic_sign, oneway |
| `make_textures.py` | `Tex/*` (procedurální PBR: asfalt, kostky, fasády, tašky…) | – |
| `make_people.py` (Blender) | `People/*.fbx` + `manifest` | MakeHuman CC0, vlastní procedurální chůze (`HumanMotion`) |
| `make_vehicles.py` (Blender) | `Vehicles/{Hatch,Estate,Suv,Van,Taxi}.fbx` | vlastní procedurální modely aut (karoserie, sklo, interiér, kola, `Low` LOD mesh) |
| `add_courtyards.py` | úpravy dvorů | – |

Licence dat: OSM je ODbL a vyžaduje uvedení. IPR je CC BY. Viz paměť `ipr-3d-data`.

### 3.2 Runtime skripty

| Skript | Co dělá | Stav |
|---|---|---|
| `MapBuilder.cs` (1 117 ř.) | Vše statické: terén (chunky `Teren_i_j`, kolider), řeka `Vltava`, ulice `Ulice` (draped ribbon, kolider), mosty IPR, budovy IPR (vizuál) + OSM footprinty (kolize), stromy, schody, koleje `Koleje`, obrubníky a rohy `Obrubniky`, parkovací místa, vstupy do metra, `Height(x,z)`, `RoadY(x,z)` | Monolit. Většina vizuálních chyb je tady. |
| `MeshBuilder.cs` | `Box`, `Poly`, `Wall`, `Cylinder`, `Build(name,parent,collider,castShadows)`. Merguje podle materiálu. `Holes` + `Cut` ořezávají povrch kolem schodišť do metra (Sutherland–Hodgman). | OK |
| `Gfx.cs` | `Mat(color, smooth, tex, tag)`, `Pbr(tex, smooth, tile, tint)`, `Emissive`, cache materiálů | Průhledné sklo je v `Vehicles.Glass` |
| `Traffic.cs` | `Net` (graf ulic: `Add`, `Finish`, `Next`, `Route` A*, `Nearest`, `RandomVertex`), pool aut, `Driver` AI (sleduje lane, BoxCast na překážky, semafory, přednost zprava, „tykadla“ proti zdem, couvání při zaseknutí), hodinový profil provozu `carHours` | Funguje; bourá zhruba 4× méně než dřív |
| `CarController.cs` | Arcade fyzika: raycast odpružení 4 kol, bicycle model, drift; `Build(pos,yaw,paint,style,withDriver)`: styly 0 hatch, 1 kombi, 2 dodávka, 3 SUV, 4 taxi, 5 policie | OK |
| `Vehicles.cs` | Načte FBX z Blenderu, otočí model podle značky `Front`, přemapuje materiály podle jména (Paint/Glass/…), kola přesadí na pivoty, `Driver()` posadí NPC, `NearOnly` (řidič jen do 45 m), `Saloon()` interiér MHD, `GlassBand`/`Shell` | Nové |
| `ParkedCars.cs` | Tisíce aut podél krajnic: dlaždice 200 m, `Low` mesh sloučený přes `CombineMeshes` podle materiálu, BoxCollider na auto, zobrazení do 260 m; `Take()` (vloupání), `IsLocked()` | 6 638 aut, obsazenost podle centra, obchodů a památek |
| `Pedestrians.cs`, `People.cs` | Chodci: spawn podle hodiny + POI + hotspotů (Karlův most…), `HumanMotion` (procedurální kostra, sed `seated`), kočárky, skupiny, osobní prostor | Chodí kdekoli, ne po chodnících (bug) |
| `Transit.cs` | Tramvaje 15T a T3 (trail-following po `D.trams`), 118 zastávek s přístřešky, nástup E, menu metra | Tramvaje mají sklo, interiér a řidiče |
| `Metro.cs` (592 ř.) | Sestupné metro: haly per stanice/linka, eskalátory, vestibuly, vstupy (díry v povrchu), vlaky 81-71M/M1, přestupy, portály přes fade | Dav na nástupišti jsou nehybné modely bez kolizí (bug) |
| `Rail.cs` | Hlavní železnice: štěrk, pražce, kolejnice, mosty, tunely, vlaky CityElefant a RegioPanter | OK, 52 FPS při zavedení |
| `Boats.cs` | Výletní lodě, šlapadla, kotvící lodě; Rigidbody, řízení E | Lodě mají prosklený salon a kapitána |
| `Signs.cs` | Značky z OSM: plechy kreslené procedurálně (IP6, P4, P6, P2, B2, B20a, B28, IP11, IP4b), zebry | Přesun na pravý okraj je jen lokálně, necommitnutý |
| `Law.cs`, `Jobs.cs`, `Inspect.cs`, `Property.cs` | Policie, svědci, zakázky, Q karta, nemovitosti | Hratelnost |
| `GameClock.cs`, `Hud.cs`, `BigMap.cs`, `CameraRig.cs`, `PlayerController.cs`, `GameAudio.cs`, `AudioSynth.cs`, `Pigeons.cs`, `Vfx.cs`, `CityBuilder.cs`, `Places.cs` | Čas (1 herní min/s), HUD, mapa M, kamera, hráč, syntetické zvuky, holubi, otevírací doby podniků | – |

### 3.3 Souřadnice a důležitá data

- **Souřadnice:** metry, x na východ, z na sever, počátek přibližně u Staroměstského náměstí. Orloj a náměstí jsou kolem `(694, 114)`.
- **Hotspoty** (`Pedestrians.Hotspots`):
  - Karlův most `(150,-25)–(-330,75)`;
  - Staroměstské náměstí `(694,114)` r70;
  - Karlova;
  - Václavské náměstí `(1151,-582)–(1366,-782)`;
  - Na Příkopě;
  - Hrad `(-991,357)–(-777,445)`;
  - Malostranské náměstí.
- **Výšky povrchů:**
  - `Height(x,z)` odpovídá trojúhelníkům terénního meshe.
  - Ulice leží o `lift` výš: dřív 0,12 m všude. Lokálně (necommitnuté) je to teď 0,105 pro pěší a 0,118 + 0,0006·šířka + 0,0005·(index mod 5) pro auta.
  - Koleje jsou `RoadY + 0,012` (lokálně).
- **Vrstvy:** `Traffic.CarLayer = 8`, `BodyLayer = 9`. Auta mají tag `"Player"`, aby je deoccluder kamery ignoroval.

---

## 4. Bottlenecky (naměřeno 16.–17. 11. 2026)

| Měření | Hodnota | Poznámka |
|---|---|---|
| PlayerLoop | **22,8 ms (~44 FPS)** | celé město, střed Starého Města |
| Trojúhelníky / draw cally | **~6,2 M / ~6 200**, setpass ~840 | ve snímku včetně stínových kaskád |
| Skinned řidiči ve všech autech | 58 → ~23 FPS | **vyřešeno**: řidič je vidět jen do 45 m (`NearOnly`) |
| Chodci (`Chodec`) | ~2 200 draw callů, 275 k trojúhelníků | největší zbývající položka draw callů; potřebují GPU instancing, sdílené materiály nebo impostory |
| Zaparkovaná auta (`Low` mesh 1 100 trojúhelníků) | ~570 k trojúhelníků viditelných, 231 meshí; se stíny výrazně dražší | LOD 1 100 je moc; cíl je ~400. Stíny jen pro nejbližší dlaždice. |
| Obrubníky | 354 k → 145 k trojúhelníků | po sloučení do kusů ≤ 12 m |
| Kamera na snímky (TmpCam) | renderuje 2× | před měřením ji ničit |
| Kompilace | ~100 s | jeden obří assembly; zvaž asmdef |
| Start scény | desítky sekund | `MapBuilder` vše staví za běhu (RaycastAll pro výšky, CombineMeshes, 16 MB budov) |

**Poučení:**
- Nic těžkého negeneruj za běhu bez LOD.
- Skinned postavy jen blízko kamery.
- Materiály sdílej (každá barva auta je nový `Material`).
- Statické věci peč do assetů v editoru, ne při každém spuštění.

---

## 5. Známé chyby a jejich příčiny

### 5.1 Povrchy a výšky (kořenová příčina většiny vizuálních chyb)

- **Povrchy se prolínají a zapadají pod sebe** (silnice, cesty, náměstí, terén):
  - Každá vrstva (ulice, pěší cesta, náměstí v terénu, koleje, obrubník, zebra, mostovka) si počítá výšku sama: `Height + lift`, draping po 4 m nebo ramp.
  - Překryvy na křižovatkách mají stejný lift, takže vzniká z-fighting.
  - Ribbony různých šířek se protínají.
  - Lokální fix (necommitnutý) rozkládá lifty podle typu a šířky. Je to jen náplast.
- **Koleje pod povrchem nebo chybějící na mostech:** tramvajový ribbon se drapoval na terén, ne na nejvyšší povrch, a na mostovkách IPR se nekreslil vůbec. Lokální fix je `TrackStrip` + `RoadY` (nejvyšší ze `Ulice`/`IprMosty*`/`Teren*`). Je necommitnutý a neověřený.
- **Schod při nájezdu na most:**
  - Ulice nad vodou se nekreslí a končí na `deckY - lift`.
  - Mostovka je cizí mesh IPR s vlastní hranou nebo obrubou.
  - Ramp 12 % se počítá jen z bodů ulice.
  - Výsledek je skok mezi kolidery ulice a mostu. Zatím neřešeno.
- **Správné řešení pro nový výřez:** jeden **výškový a povrchový model**:
  - Heightfield nebo mesh „finished grade“, kde vozovka, chodník, obrubník, mostovka i nájezd jsou jedna souvislá plocha.
  - Vrstvy jsou jen materiály nebo decaly na ní, ne samostatné ribbony nad terénem.
  - Obrubník má výšku 10–15 cm jako skutečný stupeň.
  - Koleje jsou decal + nízký mesh na vozovce.
  - Mostní nájezd se modeluje jako součást silnice.

### 5.2 Budovy

- **Okna nesedí ke střeše:**
  - Fasádní textura je tileovaná po patrech (`Wall(cellW 3.2, cellH 3.4)`), takže u římsy uřízne řadu oken.
  - IPR stěny navíc „visí od okapu“.
  - Řešení: počet pater = round(výška/3,4), výšku buňky dopočítat přesně a nahoře vynechat neúplnou řadu. Lepší jsou modulární fasády.
- **Budovy zapadlé do země:** IPR budovy mají vlastní výšky a OSM kolizní footprinty jiné. Terén je vyhlazený 8 m mřížkou, takže na svahu se dům zaboří nebo visí. Chybí sokl, který by dorovnal výšku.
- **Málokdy jsou vidět dveře:** fasády jsou textury bez dveří. Podniky jsou body z OSM bez geometrie vstupu.
- **Významné budovy jsou generické domy** (Tančící dům, NTM, Národní divadlo…). Ruční modely landmarků byly kdysi umístěné jako jeden posunutý blok a pak se vypnuly ve prospěch IPR. Každý kus musí sedět na OSM a GPS.
- **Do podniků ani budov nejde vejít:** neexistuje systém interiérů.

### 5.3 Doprava a vozidla

- **Auta:**
  - Bourala do rohů domů (log: kolize vždy předním rohem o `DomyIpr`/`MetroVstupy` při řezání zatáčky).
  - Opraveno „tykadly“, přednost zprava existuje. Zbytek je asi 0,6 z 27 aut v šoku.
  - Lane offset na úzkých ulicích je 1,3 m od osy. AI nezná skutečné jízdní pruhy.
- **Modely aut (Blender):**
  - Sklo, interiér a sedící řidič fungují.
  - Tvar je ale pořád hranatý: prizmatický profil + bevel. Podle fotek chybí zaoblená kapota, světla s tvarem, prolisy a reálné proporce (Škoda Octavia/Fabia).
  - Kola mají 5 paprsků, OK.
- **Tramvaje, vlaky, lodě:** stále jsou to kvádry z `MeshBuilder.Box`, jen dostaly sklo, interiér a řidiče. Reálný tvar podle fotek (15T ForCity, T3, 81-71M, CityElefant, RegioPanter) chybí.
- **Značky:** stály na levé straně (bug: levá normála). Lokálně opraveno na pravý okraj, necommitnuté. Rozmístění z OSM uzlů je řídké a nepřesné: chybí stopčáry, vodorovné značení pruhů a přechody pro chodce u semaforů.

### 5.4 Chodci a MHD

- **Chodci nechodí po chodnících:** chodníky jako samostatná vrstva neexistují. Pěší síť je jen `foot` ulice, nepřechází po zebrách a nemá stejnou průchodnost jako síť aut.
- **Chodci nenastupují do tramvají, metra ani lodí.**
- **V metru stojí lidé jako duchové:** `People.Spawn` + `Tick(zero)` bez pohybu a bez kolideru.

### 5.5 Ostatní

- **Hráč** je kreslená postavička, nesedí k realistickým NPC.
- **Zvuky** jsou syntetické (`AudioSynth`). Chybí CC0 nahrávky.

### 5.6 Necommitnutý stav ve worktree `phase7-property` (17. 11.)

- **`MapBuilder.cs`:** lifty podle typu ulice, `TrackStrip` (koleje na nejvyšším povrchu i na mostech), `RoadY` zahrnuje `IprMosty`.
- **`Signs.cs`:** značky na pravém okraji.
- **Stav:** kompiluje se (`scriptCompilationFailed == false`), ale **není ověřené na snímku** a není commitnuté. Uživatel práci zastavil.

---

## 6. Doporučený menší výřez

**Výřez: „Staroměstská – Rudolfinum – Karlův most – Smetanovo nábřeží – Národní divadlo“.** Je to přibližně pás 1,2 × 0,6 km podél pravého břehu Vltavy, přibližně x ∈ ⟨−50; 750⟩, z ∈ ⟨−750; 350⟩ (odhad z polohy hotspotů; přesné hranice ověř na OSM a GPS). Obsahuje **všechny prvky města**:

| Prvek | Kde ve výřezu |
|---|---|
| Řeka, nábřeží, lodě, přístaviště | Smetanovo a Masarykovo nábřeží, Slovanský ostrov (šlapadla) |
| Most s nájezdem (auta, tramvaje, chodci) | Most Legií (tramvaj + auta), Mánesův most; Karlův most (jen pěší) |
| Tramvaje + zastávky | Národní divadlo, Staroměstská, Karlovy lázně |
| Metro (vstup → vestibul → nástupiště) | Staroměstská (A), případně Národní třída (B) na okraji |
| Významné budovy s interiérem | Národní divadlo, Rudolfinum, Klementinum; Tančící dům je kousek na jih (rozšíření výřezu nebo samostatný úkol) |
| Podniky s interiérem | Karlova, Křižovnická, Národní: kavárna (Slavia), hospody, trafika, potraviny, suvenýry |
| Křižovatky se semafory, přechody, značky | Křižovnická × Platnéřská, Národní × Smetanovo nábřeží |
| Úzké uličky s kostkami a pěší zóny | Karlova, Liliová, Anenské náměstí |
| Svah a schody | jen mírně; schody k náplavce |
| Chodci, turisté (hotspot) | Karlův most, Karlova |
| Parkování podél krajnic | Smetanovo nábřeží, Platnéřská |

Malý výřez dovolí:
- dělat povrchy, budovy a vozidla ručně řízeným způsobem;
- **péct geometrii do assetů v editoru**;
- držet 60+ FPS s rezervou.

---

## 7. Definition of done pro každý prvek výřezu

Ke každému prvku: **reference** = fotky z internetu (jen jako předloha), **ověření** = snímek ve hře + numerický test.

1. **Terén a povrchy**
   - Jedna souvislá plocha.
   - Obrubník je skutečný stupeň 12 cm.
   - Chodník je o úroveň výš než vozovka; vjezdy a přechody mají snížený obrubník.
   - Žádný z-fighting z žádného úhlu.
   - Auto přejede křižovatku, koleje i nájezd na most bez skoku. Test: vertikální zrychlení auta < prahu na trase přes most.
2. **Vozovka**
   - Šířky a počty pruhů podle OSM `lanes`.
   - Vodorovné značení: osa, pruhy, stopčáry, zebry, šipky.
   - Asfalt na hlavních ulicích, kostky v pěší zóně.
   - Kanálové vpusti jako decaly.
3. **Koleje**
   - Žlábkové kolejnice v úrovni vozovky, vždy navrchu.
   - Výhybky a oblouky plynulé.
   - Trolejové vedení (sloupy a dráty) podle fotek.
4. **Značky a semafory**
   - Na pravém okraji ve směru jízdy, čelem k řidiči.
   - Správné typy podle vyhlášky 294/2015 Sb.
   - Semafory pro auta, chodce i tramvaje (tramvajová návěstidla).
5. **Budovy**
   - Patra a okna sedí ke střeše a římse.
   - Sokl dorovnává terén, žádné zaboření.
   - Dveře a výlohy v parteru tam, kde jsou podniky nebo vchody.
   - Barvy z IPR (už existují).
6. **Významné budovy**
   - Vlastní modely podle fotek, umístěné přesně podle OSM/GPS.
   - Vstupní hala a hlavní sál jsou přístupné (Národní divadlo: foyer + hlediště).
7. **Podniky**
   - Vchod v parteru, výloha a cedule podle typu.
   - Interiér podle účelu (bar, pípa a stoly / pult a regály…).
   - Otevírací doba (`Places`) určuje, jestli jde vejít.
8. **Chodci**
   - Pěší navigační síť: chodníky, přechody, pěší zóny, schody.
   - Přecházejí jen na přechodech a na zelenou.
   - Nastupují na zastávkách do tramvají, metra a lodí.
   - Mají kolize; dav v metru chodí.
9. **Auta**
   - Modely podle fotek (Octavia, Fabia, SUV, Transit/Transporter, taxi, Policie ČR).
   - Průhledné sklo, interiér, řidič.
   - LOD0, LOD1 (~400 trojúhelníků) a impostor.
   - AI v pruzích, přednost, semafory, parkovací manévry.
10. **Tramvaje, metro, vlaky, lodě**
    - Modely podle fotek (15T, T3, 81-71M, lodě Pražské paroplavební společnosti).
    - Sklo, interiér, řidič, cestující, kteří nastupují a vystupují.
11. **Hráč**
    - Realistický model ve stejném stylu jako NPC.
    - Sedí ve vozidle.
12. **Zvuky** (CC0)
    - Tramvaj (pískání v oblouku, zvonek), město, kroky na kostkách, řeka.
13. **Výkon**
    - ≥ 60 FPS v nejhustším místě (Karlův most v poledne).
    - Statická geometrie zapečená, GPU instancing, LOD, occlusion.

---

## 8. Co ze současného kódu převzít a co ne

**Převzít:**
- datové skripty (`Tools/fetch_*`, `color_tones.py`);
- Blender pipeline lidí a aut (`make_people.py`, `make_vehicles.py`);
- `HumanMotion` (procedurální chůze a sed);
- `Net` (graf + A*);
- `Driver` AI (tykadla, přednost zprava);
- `GameClock` + hodinové profily;
- `Places` (otevírací doby);
- `Vehicles.Glass` (URP transparent preset).

**Nepřebírat jako základ:**
- `MapBuilder.Ribbon` / lifty a obecně generování povrchů za běhu;
- tramvaje, vlaky a lodě z boxů;
- `Signs` umístění z OSM uzlů bez znalosti pruhů;
- metro portály přes fade, pokud má být chůze plynulá.

---

## 9. Pracovní pravidla od uživatele (z paměti)

- Česky. Fázově: hratelný kus, commit, další fáze až po „pokračuj“.
- Šetřit tokeny: ověřovat numericky přes script eval, snímky jen 1–2 na krok.
- Vše podle reálných fotek a dat. Uživatel si všímá i detailů (strany značek, schody, švy).
- Push jen do vlastní větve, nikdy na main.
