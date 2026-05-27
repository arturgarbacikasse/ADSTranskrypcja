# WhisperBenchmark – POC

POC aplikacji benchmarkującej wydajność transkrypcji Whisper.net / whisper.cpp na pojedynczej karcie GPU (np. NVIDIA L40 48 GB).

Aplikacja **NIE** zawiera RabbitMQ, OpenSSL, ffmpeg, uploadu, deszyfracji, diarizacji ani obsługi OGG. Skupiamy się wyłącznie na pomiarze przepustowości GPU: ile audio i ile calli jest w stanie przetworzyć jedna karta na godzinę.

---

## 1. Cel benchmarku

Odpowiedzieć na pytania:

- Ile godzin audio jest w stanie przerobić jedna karta GPU w ciągu jednej godziny zegara ściennego? (`audioHoursPerHour` ≈ `rtf`)
- Ile plików / "legów rozmów" jest w stanie przerobić na godzinę? (`filesPerHour`)
- Ile pełnych interakcji (`interactionId`) jest w stanie domknąć? (`interactionsPerHour` / `callsPerHour`)
- **Ile czasu zajmie przetworzenie całego realnego datasetu?** (tryb `dataset` – rekomendowany)
- Jak wartość `GpuConcurrency` wpływa na powyższe metryki (tryb `sweep`)?

---

## 2. Wymagania

### Sprzęt / OS

- **Ubuntu Server 22.04** (lub równoważny) – aplikacja jest cross-platform, ale POC jest dedykowany pod Linuxa z L40.
- NVIDIA GPU obsługujące CUDA (np. L40 48 GB).
- Działający sterownik NVIDIA i poprawnie odpowiadające `nvidia-smi`:
  ```
  nvidia-smi --query-gpu=name,driver_version --format=csv
  ```

### Oprogramowanie

- **.NET 10 SDK** (lub .NET 10 runtime + samodzielnie zbudowany artefakt). Instalacja krok po kroku: `PreKonfiguracja-Ubuntu-24.04-LTS.md`.
- Pakiety Whisper.net w wersji `1.9.1-preview1`:
  - `Whisper.net`
  - `Whisper.net.Runtime` – fallback CPU
  - `Whisper.net.Runtime.Cuda` – CUDA 13 toolchain (preferowane na nowych sterownikach)
  - `Whisper.net.Runtime.Cuda12` – CUDA 12 toolchain (fallback dla starszych driverów)
- Model GGML w katalogu wskazanym przez `Transcription.ModelsDirectory`:
  - domyślnie `./Models/ggml-large-v3-turbo.bin`
  - inne dostępne: `ggml-large-v3.bin`, `ggml-medium.bin`, `ggml-base.bin`.

> Model ładowany jest **raz** przy starcie aplikacji i używany przez cały test. Nie odpalamy modelu per plik.

---

## 3. Format plików wejściowych

Aplikacja oczekuje, że folder `InputDirectory` zawiera **podkatalogi** (po jednym na interakcję/rozmowę). W każdym podkatalogu leżą gotowe pliki WAV:

- **mono**
- **16 kHz**
- **PCM 16-bit**

Struktura katalogów:

```
InputDirectory/
  {interactionId}/
    {interactionId}_{participantId}.wav
```

Przykład datasetu:

```
/data/input/
  100/
    100_1.wav
    100_2.wav
  101/
    101_1.wav
  102/
    102_1.wav
    102_2.wav
```

Interpretacja:

- `interactionId` = nazwa podkatalogu oraz prefiks w nazwie pliku (część przed **ostatnim** podkreśleniem)
- `participantId` = część po ostatnim podkreśleniu, przed `.wav`
- Prefiks w nazwie pliku musi być **zgodny** z nazwą katalogu (np. w `100/` tylko `100_*.wav`)
- Pliki WAV w katalogu głównym `InputDirectory` (bez podfolderu) są odrzucane
- Jeden plik WAV = jeden job GPU
- Agregat per `interactionId` trafia do `benchmark-calls.csv` (kolumny `interactionId` i `callId` – ta sama wartość)

Plik niezgodny z formatem (np. stereo, 48 kHz, mp3, zły katalog) jest odrzucany w fazie walidacji i ląduje w `errors.json`. Benchmark działa dalej, dopóki jest jakikolwiek poprawny plik.

---

## 4. Przykładowy `appsettings.json`

```json
{
  "Benchmark": {
    "DefaultMode": "dataset",
    "InputDirectory": "./Data/Input",
    "OutputDirectory": "/data/output",
    "SingleSampleFile": "/data/input/100/100_1.wav",
    "Pattern": "*.wav",
    "FileNameRegex": "^(?<callId>.+)_(?<participantId>\\d+)\\.wav$",

    "DurationMinutes": 60,
    "WarmupFiles": 5,

    "GpuConcurrency": 1,
    "MaxFiles": null,
    "ShuffleInput": true,
    "RepeatInputUntilDurationEnds": true,

    "WriteTranscriptionJson": false,
    "WritePerFileJson": false,
    "WriteMergedCallJson": false,

    "MetricsIntervalSeconds": 10,

    "CollectGpuMetrics": true,
    "GpuMetricsIntervalSeconds": 10
  },

  "Transcription": {
    "UseGpu": true,
    "GpuDevice": 0,
    "ModelFileName": "ggml-large-v3-turbo.bin",
    "ModelsDirectory": "/data/models",
    "Language": "pl",
    "Threads": 4,
    "AutoDownloadModel": true,

    "Whisper": {
      "InitialPrompt": "Rozmowa telefoniczna konsultanta z klientem.",
      "SamplingStrategy": "BeamSearch",
      "BeamSize": 5,
      "Temperature": 0.0,
      "NoSpeechThreshold": 0.6,
      "Translate": false
    }
  }
}
```

> Wszystkie kluczowe parametry można nadpisać z linii poleceń – patrz niżej.

---

## 5. Komendy uruchomieniowe

Wbudowane tryby:

### 5.1 `single` – jeden plik

Szybki sanity check: transkrybuje jeden konkretny plik, drukuje wynik, zapisuje minimalny raport.

```bash
dotnet run -c Release --no-launch-profile --project WhisperBenchmark -- \
    single --file /data/input/100/100_1.wav
```

> Użyj `--no-launch-profile`, jeśli podajesz własne argumenty z terminala – inaczej `dotnet run` może dołączyć argumenty z `launchSettings.json` (komunikat „Używanie ustawień uruchamiania z profilu…”).

### 5.2 `dataset` – przetwarzanie całego realnego datasetu (rekomendowany)

Symuluje dzień pracy: bierze cały `InputDirectory`, przetwarza każdy plik WAV **dokładnie raz** i kończy po domknięciu datasetu.

- **Nie** używa `DurationMinutes` ani zapętlania (`RepeatInputUntilDurationEnds` jest ignorowane).
- **Nie** robi warmupu (wynik jest prostszy do interpretacji).
- Model ładowany przy starcie: **jedna instancja GGML na każdy slot** `GpuConcurrency` (bezpieczna równoległość CUDA; na laptopie 4 GB zwykle `GpuConcurrency=1`).
- Raport `benchmark-summary.json` zawiera m.in. `datasetAudioHours`, `wallClockMinutes`, `rtf`, `audioHoursPerHour`, `capacityPrediction`.

```bash
cd WhisperBenchmark
dotnet run -c Release --no-launch-profile -- \
  dataset \
  --input ./Data/Input \
  --output ./Data/Output \
  --gpu-concurrency 1
```

> Na laptopie z **4 GB VRAM** (np. RTX 3050) używaj `--gpu-concurrency 1`. Wartość `4` jest sensowna na serwerze z L40 48 GB.

Opcje dodatkowe: `--max-files`, `--write-transcription-json`, `--collect-gpu-metrics`, `--shuffle`.

Po sukcesie w konsoli pojawi się **`DATASET BENCHMARK FINISHED`** i **`Raport zapisany w ./Data/Output`**. W `benchmark-summary.json` sprawdź `"mode": "dataset"`.

Log na żywo (co `MetricsIntervalSeconds`):

```text
[00:10:00] mode=dataset files=420/1000 interactions=180/500 audio=3.42h elapsed=10m rtf=20.5x active=4 queue=580 errors=0
```

### 5.3 `soak` – test obciążeniowy przez zadany czas

Działa zadany czas, ładuje GPU do `GpuConcurrency` równoległych transkrypcji, opcjonalnie zapętla dataset.

```bash
dotnet run -c Release --project WhisperBenchmark -- \
    soak \
    --input /home/aubuntu/ADSTranskrypcja/WhisperBenchmark/Data/Input \
    --output /home/aubuntu/ADSTranskrypcja/WhisperBenchmark/Data/Output \
    --duration-minutes 10 \
    --gpu-concurrency 4
```

### 5.4 `sweep` – porównanie wielu wartości concurrency

```bash
dotnet run -c Release --project WhisperBenchmark -- \
    sweep \
    --input /home/aubuntu/ADSTranskrypcja/WhisperBenchmark/Data/Input \
    --output /home/aubuntu/ADSTranskrypcja/WhisperBenchmark/Data/Output \
    --duration-minutes 10 \
    --concurrency 1,2,4,8,16
```

Każdy krok sweepu trafia do podkatalogu `sweep-c<N>/`, a porównanie zbiorcze do `benchmark-sweep.csv`.

### 5.5 Pełna lista opcji CLI

| Opcja | Opis |
|------|------|
| `--file <path>` | Plik do transkrypcji (tryb `single`). |
| `--input <dir>` | Katalog wejściowy – override `Benchmark.InputDirectory`. |
| `--output <dir>` | Katalog wyjściowy – override `Benchmark.OutputDirectory`. |
| `--duration-minutes <int>` | Czas fazy pomiarowej (`soak`/`sweep`; ignorowane w `dataset`). |
| `--gpu-concurrency <int>` | Maksymalna liczba równoległych transkrypcji. |
| `--concurrency 1,2,4,8` | Lista wartości GpuConcurrency dla trybu `sweep`. |
| `--max-files <int>` | Twardy limit liczby plików. |
| `--write-transcription-json true/false` | Zapis pełnej transkrypcji per plik. |
| `--collect-gpu-metrics true/false` | Zbieranie `nvidia-smi` → `gpu-metrics.csv`. |
| `--shuffle true/false` | Losowa kolejność plików (`dataset`/`soak`). |
| `--help` | Wypisuje pomoc. |

---

## 6. RTF – Real-Time Factor

`RTF = audioSecondsProcessed / wallClockSecondsProcessed`

- `RTF = 1x` → karta przerabia 1 godzinę audio w 1 godzinę zegara ściennego.
- `RTF = 25x` → karta przerabia **25 godzin audio na każdą godzinę pracy GPU**.

W aplikacji liczymy dwa "smaki" RTF:

- **`Summary.Rtf`** – zagregowany; suma audio podzielona przez wall-clock czas fazy pomiarowej.
- **`FileBenchmarkResult.Rtf`** / `Summary.AvgFileRtf` – per pojedynczy plik (czas trwania pliku ÷ czas transkrypcji pliku).

Pojedynczy plik na L40 z `large-v3-turbo` zwykle osiąga RTF ~10–30x; przy `GpuConcurrency > 1` całościowy `Summary.Rtf` rośnie aż do nasycenia karty.

---

## 7. Jak interpretować wynik

### Tryb `dataset` (rekomendowany)

Po zakończeniu znajdź `benchmark-summary.json` (`"mode": "dataset"`):

```json
{
  "mode": "dataset",
  "datasetAudioHours": 8.0,
  "wallClockMinutes": 22.03,
  "rtf": 21.79,
  "audioHoursPerHour": 21.79,
  "processingTimePercentOfAudioDuration": 4.59,
  "interactionsPerHour": 1361.5,
  "capacityPrediction": {
    "processingTimeFor8AudioHoursMinutes": 22.03,
    "processingTimeFor100AudioHoursMinutes": 275.36
  }
}
```

**Interpretacja:**

- Do aplikacji wrzucono **8 h audio** (`datasetAudioHours`).
- GPU przetworzyło to w **22,03 min** (`wallClockMinutes`).
- Maszyna działa **21,79×** szybciej niż real time (`rtf` / `audioHoursPerHour`).
- Jedna godzina pracy tej maszyny przetwarza ok. **21,79 h audio**.

**Predykcja czasu przetwarzania:**

```
czasPrzetwarzaniaMinuty = (audioHours / audioHoursPerHour) * 60
```

Przykłady przy `audioHoursPerHour = 21.79`:

| Audio wejściowe | Czas przetwarzania |
|----------------|-------------------|
| 2 h | 5,51 min |
| 8 h | 22,03 min |
| 24 h | 66,09 min |
| 100 h | 275,36 min |

Pola `capacityPrediction.*` w JSON zawierają te same wartości gotowe do odczytu.

### Tryb `soak` / `sweep`

```json
{
  "audioHoursProcessed": 26.4,
  "rtf": 26.4,
  "filesPerHour": 740,
  "callsPerHour": 365,
  "avgFileProcessingSeconds": 18.4,
  "p95FileProcessingSeconds": 31.8,
  "errors": 0
}
```

- **`audioHoursPerHour` = `rtf`** – tyle godzin audio karta jest w stanie przerobić w ciągu zegarowej godziny.
- **`callsPerHour`** – pełne interakcje domknięte w ciągu godziny.
- **`p95FileProcessingSeconds`** – ogon czasów; ważne przy planowaniu kolejkowania / SLA.

---

## 8. Monitoring GPU obok benchmarku

`nvidia-smi dmon` można odpalić obok (drugi terminal) dla podglądu na żywo:

```bash
nvidia-smi dmon
```

Aplikacja sama też zbiera próbki (jeżeli `Benchmark.CollectGpuMetrics: true`):

```
nvidia-smi --query-gpu=timestamp,index,name,utilization.gpu,memory.used,memory.total,power.draw,temperature.gpu --format=csv,noheader,nounits
```

i zapisuje do `gpu-metrics.csv`. Brak `nvidia-smi` w PATH skutkuje wyłącznie ostrzeżeniem – benchmark biegnie dalej.

---

## 9. Pliki wynikowe

```
<OutputDirectory>/
  benchmark-summary.json      # główne podsumowanie pojedynczego runa
  benchmark-summary.csv       # to samo w postaci klucz,wartość
  benchmark-files.csv         # metryki per plik (audio/proc/rtf/queue wait)
  benchmark-calls.csv         # agregat per callId
  benchmark-sweep.csv         # porównanie kroków sweepu (tylko tryb sweep)
  gpu-metrics.csv             # surowe próbki z nvidia-smi
  errors.json                 # błędy walidacji / transkrypcji
  files/                      # (opcjonalnie) per-file JSON (WritePerFileJson=true)
    call001_1.benchmark.json
  transcriptions/             # transkrypcje (tryb single zawsze; soak gdy WriteTranscriptionJson=true)
    call001_1.json            # per plik: model, language, segments[{start,end,text}]
    call001.json              # (opcjonalnie WriteMergedCallJson=true) obie nogi rozmowy
  sweep-c1/, sweep-c2/, ...   # (tryb sweep) raporty per krok
```

### `benchmark-files.csv`

Kolumny: `file, fullPath, interactionId, callId, participantId, audioSeconds, processingSeconds, queueWaitSeconds, rtf, startedAt, finishedAt, segmentCount, success, errorMessage`. (`interactionId` = `callId`).

### `benchmark-calls.csv`

Kolumny: `interactionId, callId, participants, files, totalAudioSeconds, completedFiles, failedFiles, interactionProcessingSeconds, firstFileStartedAt, lastFileFinishedAt, totalProcessingSeconds, maxFileProcessingSeconds, completed`.

`interactionProcessingSeconds` = `lastFileFinishedAt − firstFileStartedAt` (uwzględnia równoległość nóg rozmowy).

### `benchmark-sweep.csv`

Kolumny: `gpuConcurrency, durationSeconds, processedFiles, processedCalls, processedAudioSeconds, audioHoursProcessed, rtf, filesPerHour, callsPerHour, avgFileProcessingSeconds, p95FileProcessingSeconds, errors`.

### `errors.json`

Lista obiektów z polami `file, fullPath, interactionId, participantId, stage, message, exception, timestamp`. `stage`: `Scan`, `Validation`, `Warmup`, `Transcription`, `Report`.

### `transcriptions/{callId}_{participantId}.json`

Zapis wyniku transkrypcji (segmenty w sekundach od początku nagrania):

```json
{
  "callId": "call001",
  "participantId": "1",
  "file": "call001_1.wav",
  "model": "ggml-large-v3-turbo.bin",
  "language": "pl",
  "audioSeconds": 125.68,
  "processingSeconds": 10.24,
  "rtf": 12.27,
  "segments": [
    { "start": 0.0, "end": 4.12, "text": "Dzień dobry, w czym mogę pomóc?" }
  ]
}
```

- Tryb **`single`** zapisuje ten plik **zawsze** (segmenty są zbierane niezależnie od `WriteTranscriptionJson`).
- Tryb **`dataset`** / **`soak`** / **`sweep`** – tylko gdy `WriteTranscriptionJson: true` (domyślnie `false`, żeby nie mierzyć narzutu I/O).
- **`WriteMergedCallJson: true`** – dodatkowo `transcriptions/call001.json` ze wszystkimi uczestnikami w polu `participants`.

---

## 10. Wskazówki praktyczne

- **Tryb `dataset`** – bez warmupu i bez zapętlania; każdy poprawny plik przetwarzany dokładnie raz. Rekomendowany do planowania produkcji.
- **Warmup (`soak`)** – pierwsze N plików (`Benchmark.WarmupFiles`) przed pomiarem; wyniki warmupu **nie** wliczają się do summary.
- **Zapętlanie (`soak`)** – `RepeatInputUntilDurationEnds: true` pozwala mierzyć przez godzinę nawet przy małym datasetcie.
- **Wyłącz I/O w pomiarze przepustowości** – pozostaw `WriteTranscriptionJson: false` (domyślnie). Zapis JSON-ów per plik włączaj tylko, gdy chcesz porównać jakość transkrypcji.
- **GpuConcurrency** – na L40 48 GB z `large-v3-turbo` zwykle przydatne wartości to 2–8. Sweep pokaże, gdzie się nasyca pamięć/SM i przestaje rosnąć throughput.
- **Anulowanie** – `Ctrl+C` zapisuje raport częściowy (z tego, co już zostało przetworzone).

---

## 11. Uruchamianie w IDE (Visual Studio / Cursor / Rider)

Plik `Properties/launchSettings.json` definiuje gotowe **profile uruchomieniowe**. Pojawiają się one obok zielonego "Play" w Visual Studio (oraz w panelu Run/Debug w Cursor / VS Code / Rider). Wybór profilu = wybór trybu i argumentów, bez przepisywania ich za każdym razem.

Predefiniowane profile:

| Profil | Co robi |
|---|---|
| `dataset (full input, GPU=1)` | **Rekomendowany:** cały `./Data/Input` przy `GpuConcurrency=1` (bezpieczne na laptopie). |
| `help` | `--help`, drukuje pomoc i kończy. |
| `single (sample WAV)` | `single --file ./Data/Input/100/100_1.wav` – pojedynczy plik. |
| `soak (2 min smoke test)` | `soak ... --duration-minutes 2 --gpu-concurrency 1` – szybki sanity check. |
| `soak (60 min POC, GPU=4)` | Pełny POC: 60 minut przy `GpuConcurrency=4`. |
| `soak (CPU fallback debug)` | Krótki test 3 plików z `WriteTranscriptionJson=true` – do debugowania. |
| `sweep (10 min, 1-2-4-8-16)` | Sweep wartości `GpuConcurrency`. |

### Trzy warianty pracy

1. **`launchSettings.json` + profile (rekomendowane do developmentu)**  
   F5 w IDE, wybierasz profil z dropdownu obok "Play". Plik commitujemy do gita – cały zespół ma te same przyciski. Działa też z terminala:
   ```bash
   dotnet run --project WhisperBenchmark -c Release --launch-profile "soak (2 min smoke test)"
   ```

2. **GUI Visual Studio → Project Properties → Debug → Launch Profiles**  
   To ten sam plik, edytowany myszką. Dobre na ad-hoc poprawki.

3. **Terminal (rekomendowane na L40 / Ubuntu / CI / `systemd`)**  
   Komenda jest jawna, kopiowalna, identyczna na Windowsie i Linuxie:
   ```bash
   dotnet run --project WhisperBenchmark -c Release -- \
       dataset --input /data/input --output /data/output --gpu-concurrency 4
   ```

### Working directory i ścieżki względne

W profilach `workingDirectory = $(ProjectDir)`, czyli VS startuje proces z katalogu `WhisperBenchmark/`. Dlatego ścieżki w profilach to `./Data/Input` (relatywne do projektu). Jeśli wolisz absolutne, podmień je w `Properties/launchSettings.json` na np. `D:/datasets/calls`.

### Lokalny dataset / model do testów

Aplikacja oczekuje:

```
WhisperBenchmark/
  Data/
    Input/                       # katalog główny datasetu
      100/                       # interactionId
        100_1.wav
        100_2.wav
      101/
        101_1.wav
      ...
    Output/                      # tutaj lecą raporty
  Models/
    ggml-large-v3-turbo.bin      # albo inny model GGML
```

Foldery `Data/Input/`, `Data/Output/` i `Models/` są w repo z plikami `.gitkeep`. Same WAV-y i `.bin` są wpisane do `.gitignore` i **nie trafiają do gita**.

### Skąd wziąć model GGML

**Domyślnie nie musisz robić nic** – jeżeli pliku nie ma w `ModelsDirectory`, aplikacja pobierze go z Hugging Face przy pierwszym uruchomieniu (`Transcription.AutoDownloadModel: true`). Postęp jest logowany co ~50 MB:

```
info: WhisperBenchmark.Services.WhisperModelDownloader[0]
  Brak modelu w ./Models/ggml-large-v3-turbo.bin. Pobieram LargeV3Turbo (kwantyzacja: NoQuantization) z Hugging Face...
info: WhisperBenchmark.Services.WhisperModelDownloader[0]
  Pobrano 50 MB modelu (12.4 MB/s, czas: 00:00:04).
...
info: WhisperBenchmark.Services.WhisperModelDownloader[0]
  Pobrano model ggml-large-v3-turbo.bin (1543 MB) w 00:01:58 – zapisano do ./Models/ggml-large-v3-turbo.bin.
```

Auto-downloader rozumie nazwy w schemacie `ggml-{model}[-{quant}].bin`. Obsługiwane modele: `tiny`, `tiny.en`, `base`, `base.en`, `small`, `small.en`, `medium`, `medium.en`, `large-v1`, `large-v2`, `large-v3`, `large-v3-turbo`. Obsługiwane kwantyzacje: `q4_0`, `q4_1`, `q5_0`, `q5_1`, `q8_0`. Przykłady poprawnych nazw:

- `ggml-large-v3-turbo.bin` – pełny large-v3-turbo (FP16, ~1.5 GB) – domyślny.
- `ggml-large-v3.bin` – pełny large-v3 (FP16, ~3.1 GB) – wyższa jakość, ~2× wolniej.
- `ggml-medium-q5_0.bin` – medium z kwantyzacją Q5_0 (~500 MB).

Jeśli chcesz wyłączyć pobieranie (np. na produkcyjnym L40 bez internetu), ustaw `Transcription.AutoDownloadModel: false` w `appsettings.json` i wgraj plik ręcznie. Linki:

```bash
# Linux
cd WhisperBenchmark/Models
curl -L -o ggml-large-v3-turbo.bin \
    https://huggingface.co/sandrohanea/whisper.net/resolve/v3/classic/ggml-large-v3-turbo.bin
```

```powershell
# Windows
Invoke-WebRequest `
  -Uri  https://huggingface.co/sandrohanea/whisper.net/resolve/v3/classic/ggml-large-v3-turbo.bin `
  -OutFile WhisperBenchmark\Models\ggml-large-v3-turbo.bin
```

### Typowe błędy "first run" i co znaczą

| Komunikat | Powód | Co zrobić |
|---|---|---|
| `Plik wejściowy nie istnieje: ./Data/Input/100/100_1.wav` (tryb `single`) | Brak konkretnego pliku | Utwórz podkatalog `interactionId` i wgraj plik `{interactionId}_{participantId}.wav`. |
| `Brak poprawnych plików WAV w ./Data/Input` (tryb `soak`/`sweep`) | Brak podkatalogów, puste foldery albo wszystkie pliki nie przeszły walidacji | Struktura: `Input/{interactionId}/{interactionId}_{participantId}.wav` (mono/16 kHz/PCM-16). |
| `Nie znaleziono pliku modelu Whispera: ./Models/ggml-large-v3-turbo.bin …` | Brak modelu w `Models/` i `AutoDownloadModel=false` | Pobierz model ręcznie (instrukcja wyżej) albo włącz `Transcription.AutoDownloadModel=true`. |
| `Nie udało się rozpoznać typu modelu z nazwy pliku '…'` | Auto-downloader nie zmapował nazwy na `GgmlType` | Użyj nazwy zgodnej ze schematem `ggml-{model}[-{quant}].bin` albo wgraj plik ręcznie. |
| `Nie udało się uruchomić nvidia-smi …` (warning) | Brak `nvidia-smi` w PATH (np. development na laptopie bez NVIDII) | Ignoruj – benchmark biegnie dalej, tylko `gpu-metrics.csv` jest pusty. Albo wyłącz `Benchmark.CollectGpuMetrics`. |
| `Skonfigurowano UseGpu=true, ale Whisper.net załadował runtime CPU` | CUDA się nie wpięła (driver, wersja toolchaina, brak pakietu CUDA runtime) | Zainstaluj NVIDIA driver + CUDA Toolkit, sprawdź `nvidia-smi`, zweryfikuj że pakiet `Whisper.net.Runtime.Cuda` / `Whisper.net.Runtime.Cuda12` znalazł odpowiednią natywkę pod `bin/.../runtimes/linux-x64/native`. |
| `ggml-cuda.cu:96: CUDA error` + brak nowego raportu | Wiszący proces po crashu zajmuje VRAM albo `GpuConcurrency` za duże na kartę | `pkill -f WhisperBenchmark`, sprawdź `nvidia-smi` (wolna pamięć), uruchom z `--gpu-concurrency 1`. |
| Stary `benchmark-summary.json` (brak `"mode": "dataset"`) | Ostatni run się wywalił przed `Raport zapisany` | Napraw CUDA / zwolnij GPU i uruchom `dataset` ponownie. |

### CUDA / pamięć GPU (ważne na laptopie)

Przed `dataset` / `soak`:

```bash
nvidia-smi
pkill -f WhisperBenchmark   # jeśli po Ctrl+C lub crashu coś zostało na GPU
```

Aplikacja przy starcie `dataset` loguje ostrzeżenie (`GpuStartupCheck`), gdy wykryje proces WhisperBenchmark już trzymający pamięć GPU.

**RTX 3050 4 GB:** model `large-v3-turbo` (~1,5 GB) + beam search — zostaw `GpuConcurrency=1`. Nie uruchamiaj kilku benchmarków równolegle.

Pełna instalacja Ubuntu (sterownik, CUDA, .NET, ffmpeg): patrz **`PreKonfiguracja-Ubuntu-24.04-LTS.md`**.

---

## 12. Limity POC

- Brak diarizacji.
- Brak konwersji formatów (ffmpeg) – wymagane WAV mono 16 kHz PCM-16 na wejściu.
- Brak RabbitMQ, OpenSSL, uploadów, scalania finalnego JSON-a.
- Jeden proces; `GpuConcurrency` niezależnych instancji modelu w VRAM; N workerów z osobnym `WhisperProcessor` na transkrypcję.
