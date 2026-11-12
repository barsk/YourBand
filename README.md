# YourBand

Planen för spelet (övningsverktyg och jampartner, desktop och VR) finns i [docs/plan.md](docs/plan.md).
Källor och licenser för allt tredjepartsinnehåll står i [CREDITS.md](CREDITS.md).

En AI-styrd gitarrlärare som avatar. Första steget är att testa hur väl handrörelser
kan extraheras ur vanlig video, som ett billigare alternativ till mocap.

## Steg 1: filma

**Kamera:** mobil på stativ, 1–1,5 m bort, i höjd med gitarrhalsen. Filma framifrån
och något från halssidan, så att greppbrädan och greppande hand syns tydligt.

- **60 fps** räcker för ackord. Använd 120 eller 240 fps om du filmar solon.
- 1080p, jämnt och starkt ljus utan motljus och en lugn bakgrund.
- Kavla upp ärmarna och ta av ringar.
- Börja lugnt. Håll varje ackord i ungefär 3 sekunder och anslå det några gånger.

**Förslag på testklipp:**

1. `oppna_ackord.mp4`: C, G, D, Am, Em och E, i den ordningen.
2. `barre.mp4`: F (E-form), Bm (A-form) och sedan ett byte F → C → G.
3. *(valfritt)* en kort pentatonisk skala eller ett riff, för att se var gränsen går.

Skriv ner vilka ackord som spelas i vilken ordning. Längre fram används det för att
kalibrera händernas läge mot greppbrädan.

Har du två mobiler kan du filma samtidigt från ytterligare en vinkel, till exempel
snett ovanifrån halsen som eleven ser den. Varje vinkel bearbetas separat tills vidare.

**Markörer (valfritt men rekommenderat):** skriv ut `markers/aruco_markers.pdf` i
100 %. Tejpa id 0 på huvudets framsida och id 1 på kroppen nära halsfästet. De behövs
inte för första testet, men då kan samma filmer användas senare för att räkna ut
greppbrädans exakta läge utan att du behöver filma om.

Lägg filmerna i `videos/`.

## Steg 2: extrahera händerna

```powershell
.\.venv\Scripts\python.exe capture\extract_hands.py videos\oppna_ackord.mp4
```

Skriptet skapar två filer i `output/`:

- `*_overlay.mp4`, videon med spårningen inritad. Titta på den först. Orange är
  greppande hand och blått är anslagshanden.
- `*_hands.json`, alla 21 handleder per bildruta i pixlar, i handens eget koordinatsystem
  och i kamerans 3D-rum (meter).

Användbara flaggor:

| Flagga | När |
|---|---|
| `--lefty` | Vänsterhänt gitarrist |
| `--mirrored` | Videon är spegelvänd (vissa selfie-lägen) |
| `--fov 72` | Kamerans synfält längs långsidan, standard 67° |
| `--rotate 90` | Om bilden ligger fel |
| `--no-smooth` | Se rådata utan utjämning |

Om händerna har bytt färg i overlay-videon (orange på anslagshanden) prövar du `--mirrored`.

## Steg 3: titta i Blender

```powershell
& "C:\Program Files\Blender Foundation\Blender 3.5\blender.exe" --python capture\blender_view.py -- output\oppna_ackord_hands.json
```

Blender öppnas och visar scenen genom en kamera som matchar mobilen, med
overlay-videon i bakgrunden och de animerade 3D-händerna ovanpå. Tryck mellanslag
för att spela upp. Rotera vyn för att se hur djupet ser ut från sidan.

## Installation (redan gjort på den här datorn)

```powershell
py -3.13 -m venv .venv
.\.venv\Scripts\python.exe -m pip install -r capture\requirements.txt
```

Handmodellen (`models/hand_landmarker.task`) laddas ner automatiskt vid första körningen.

## Kända begränsningar i steg 1

- Djupet är grovt. Det skattas från en enda kamera med en uppskattad brännvidd.
- Händerna ligger i kamerans rum och inte relativt greppbrädan ännu. Det är nästa steg,
  via markörerna eller kalibrering mot kända ackordgrepp.
- MediaPipe är snabbt men grovt på fingerdetaljer. Om det inte räcker byter vi till
  HaMeR eller WiLoR, som går att köra på grafikkortet.
