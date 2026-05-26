# WhisperBenchmark – POC

POC aplikacji benchmarkującej wydajność transkrypcji Whisper.net / whisper.cpp na pojedynczej karcie GPU (np. NVIDIA L40 48 GB).

Aplikacja **NIE** zawiera RabbitMQ, OpenSSL, ffmpeg, uploadu, deszyfracji, diarizacji ani obsługi OGG. Skupiamy się wyłącznie na pomiarze przepustowości GPU: ile audio i ile calli jest w stanie przetworzyć jedna karta na godzinę.

---

## 1. Cel benchmarku

Odpowiedzieć na pytania:

- Ile godzin audio jest w stanie przerobić jedna karta GPU w ciągu jednej godziny zegara ściennego? (`audioHoursPerHour` ≈ `rtf`)
- Ile plików / "legów rozmów" jest w stanie przerobić na godzinę? (`filesPerHour`)
- Ile pełnych callId-ów jest w stanie domknąć? (`callsPerHour`)
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

- **.NET 10 SDK** (lub .NET 10 runtime + samodzielnie zbudowany artefakt).
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

Aplikacja oczekuje, że folder `InputDirectory` zawiera już gotowe pliki WAV:

- **mono**
- **16 kHz**
- **PCM 16-bit**

Nazewnictwo:

```
{callId}_{participantId}.wav
```

Przykład datasetu:

```
/data/input/call001_1.wav
/data/input/call001_2.wav
/data/input/call002_1.wav
/data/input/call002_2.wav
/data/input/call003_1.wav
```

Interpretacja:

- `callId` = część przed **ostatnim** podkreśleniem
- `participantId` = część po ostatnim podkreśleniu, przed `.wav`
- Jeden plik WAV = jeden job GPU.
- Agregat per `callId` jest budowany na końcu (`benchmark-calls.csv`).

Plik niezgodny z formatem (np. stereo, 48 kHz, mp3) jest odrzucany w fazie walidacji i ląduje w `errors.json`. Benchmark działa dalej, dopóki jest jakikolwiek poprawny plik.

---

## 4. Przykładowy `appsettings.json`

```json
{
  "Benchmark": {
    "InputDirectory": "/data/input",
    "OutputDirectory": "/data/output",
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
dotnet run -c Release --project WhisperBenchmark -- \
    single --file /data/input/call001_1.wav
```

### 5.2 `soak` – test obciążeniowy

Główny tryb POC. Działa zadany czas, ładuje GPU do `GpuConcurrency` równoległych transkrypcji, opcjonalnie zapętla dataset.

```bash
dotnet run -c Release --project WhisperBenchmark -- \
    soak \
    --input /data/input \
    --output /data/output \
    --duration-minutes 60 \
    --gpu-concurrency 4
```

### 5.3 `sweep` – porównanie wielu wartości concurrency

```bash
dotnet run -c Release --project WhisperBenchmark -- \
    sweep \
    --input /data/input \
    --output /data/output \
    --duration-minutes 10 \
    --concurrency 1,2,4,8,16
```

Każdy krok sweepu trafia do podkatalogu `sweep-c<N>/`, a porównanie zbiorcze do `benchmark-sweep.csv`.

### 5.4 Pełna lista opcji CLI

| Opcja | Opis |
|------|------|
| `--file <path>` | Plik do transkrypcji (tryb `single`). |
| `--input <dir>` | Katalog wejściowy – override `Benchmark.InputDirectory`. |
| `--output <dir>` | Katalog wyjściowy – override `Benchmark.OutputDirectory`. |
| `--duration-minutes <int>` | Czas fazy pomiarowej. |
| `--gpu-concurrency <int>` | Maksymalna liczba równoległych transkrypcji. |
| `--concurrency 1,2,4,8` | Lista wartości GpuConcurrency dla trybu `sweep`. |
| `--max-files <int>` | Twardy limit liczby plików. |
| `--write-transcription-json true` | Zapisuje pełną transkrypcję per plik (kosztem narzutu I/O). |
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

Po zakończeniu testu znajdź `benchmark-summary.json`. Najważniejsze pola:

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
- **`callsPerHour`** – pełne callId-y domknięte w ciągu godziny.
- **`p95FileProcessingSeconds`** – ogon czasów; ważne przy planowaniu kolejkowania / SLA.
- **Szacunek calls/h niezależny od czasu testu**:
  ```
  estimatedCallsPerHourByAverageCallDuration =
      (rtf * 3600) / averageCallAudioSeconds
  ```
  Wzór policz ręcznie z `benchmark-calls.csv` (kolumna `totalAudioSeconds` zsumowana / liczba calli = `averageCallAudioSeconds`).

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

Kolumny: `file, fullPath, callId, participantId, audioSeconds, processingSeconds, queueWaitSeconds, rtf, startedAt, finishedAt, segmentCount, success, errorMessage`.

### `benchmark-calls.csv`

Kolumny: `callId, participants, files, totalAudioSeconds, completedFiles, failedFiles, totalProcessingSeconds, maxFileProcessingSeconds, completed`. Pola `participants` i `files` są listami sklejonymi znakiem `|`.

### `benchmark-sweep.csv`

Kolumny: `gpuConcurrency, durationSeconds, processedFiles, processedCalls, processedAudioSeconds, audioHoursProcessed, rtf, filesPerHour, callsPerHour, avgFileProcessingSeconds, p95FileProcessingSeconds, errors`.

### `errors.json`

Lista obiektów z polami `file, callId, participantId, stage, message, exception, timestamp`. `stage` to jedno z: `Scan`, `Validation`, `Warmup`, `Transcription`.

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
- Tryb **`soak`** / **`sweep`** – tylko gdy `WriteTranscriptionJson: true` (domyślnie `false`, żeby nie mierzyć narzutu I/O).
- **`WriteMergedCallJson: true`** – dodatkowo `transcriptions/call001.json` ze wszystkimi uczestnikami w polu `participants`.

---

## 10. Wskazówki praktyczne

- **Warmup** – pierwsze N plików (`Benchmark.WarmupFiles`) jest przetwarzane przed startem pomiaru i ich wyniki **nie** są wliczane do `benchmark-summary.json`. Domyślne 5 plików stabilizuje runtime CUDA i cache modelu.
- **Zapętlanie datasetu** – `RepeatInputUntilDurationEnds: true` pozwala mierzyć przez godzinę nawet jeśli plików jest mniej (np. 50 plików × kilka pętli).
- **Wyłącz I/O w pomiarze przepustowości** – pozostaw `WriteTranscriptionJson: false` (domyślnie). Zapis JSON-ów per plik włączaj tylko, gdy chcesz porównać jakość transkrypcji.
- **GpuConcurrency** – na L40 48 GB z `large-v3-turbo` zwykle przydatne wartości to 2–8. Sweep pokaże, gdzie się nasyca pamięć/SM i przestaje rosnąć throughput.
- **Anulowanie** – `Ctrl+C` zapisuje raport częściowy (z tego, co już zostało przetworzone).

---

## 11. Uruchamianie w IDE (Visual Studio / Cursor / Rider)

Plik `Properties/launchSettings.json` definiuje gotowe **profile uruchomieniowe**. Pojawiają się one obok zielonego "Play" w Visual Studio (oraz w panelu Run/Debug w Cursor / VS Code / Rider). Wybór profilu = wybór trybu i argumentów, bez przepisywania ich za każdym razem.

Predefiniowane profile:

| Profil | Co robi |
|---|---|
| `help` | `--help`, drukuje pomoc i kończy. |
| `single (sample WAV)` | `single --file ./Data/Input/call001_1.wav` – pojedynczy plik. |
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
       soak --input /data/input --output /data/output \
            --duration-minutes 60 --gpu-concurrency 4
   ```

### Working directory i ścieżki względne

W profilach `workingDirectory = $(ProjectDir)`, czyli VS startuje proces z katalogu `WhisperBenchmark/`. Dlatego ścieżki w profilach to `./Data/Input` (relatywne do projektu). Jeśli wolisz absolutne, podmień je w `Properties/launchSettings.json` na np. `D:/datasets/calls`.

### Lokalny dataset / model do testów

Aplikacja oczekuje:

```
WhisperBenchmark/
  Data/
    Input/                       # tutaj wgraj WAV-y mono/16kHz/PCM-16
      call001_1.wav
      call001_2.wav
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
| `Plik wejściowy nie istnieje: ./Data/Input/call001_1.wav` (tryb `single`) | Brak konkretnego pliku w `Data/Input/` | Wgraj plik, zmień ścieżkę w profilu albo użyj profilu `soak (2 min smoke test)`. |
| `Brak poprawnych plików WAV w ./Data/Input` (tryb `soak`/`sweep`) | Folder jest pusty albo wszystkie pliki nie przeszły walidacji | Wgraj poprawne WAV-y (mono/16 kHz/PCM-16) z nazwą `{callId}_{participantId}.wav`. |
| `Nie znaleziono pliku modelu Whispera: ./Models/ggml-large-v3-turbo.bin …` | Brak modelu w `Models/` i `AutoDownloadModel=false` | Pobierz model ręcznie (instrukcja wyżej) albo włącz `Transcription.AutoDownloadModel=true`. |
| `Nie udało się rozpoznać typu modelu z nazwy pliku '…'` | Auto-downloader nie zmapował nazwy na `GgmlType` | Użyj nazwy zgodnej ze schematem `ggml-{model}[-{quant}].bin` albo wgraj plik ręcznie. |
| `Nie udało się uruchomić nvidia-smi …` (warning) | Brak `nvidia-smi` w PATH (np. development na laptopie bez NVIDII) | Ignoruj – benchmark biegnie dalej, tylko `gpu-metrics.csv` jest pusty. Albo wyłącz `Benchmark.CollectGpuMetrics`. |
| `Skonfigurowano UseGpu=true, ale Whisper.net załadował runtime CPU` | CUDA się nie wpięła (driver, wersja toolchaina, brak pakietu CUDA runtime) | Zainstaluj NVIDIA driver + CUDA Toolkit, sprawdź `nvidia-smi`, zweryfikuj że pakiet `Whisper.net.Runtime.Cuda` / `Whisper.net.Runtime.Cuda12` znalazł odpowiednią natywkę pod `bin/.../runtimes/linux-x64/native`. |

---

## 12. Limity POC

- Brak diarizacji.
- Brak konwersji formatów (ffmpeg) – wymagane WAV mono 16 kHz PCM-16 na wejściu.
- Brak RabbitMQ, OpenSSL, uploadów, scalania finalnego JSON-a.
- Jeden proces, jeden model w pamięci, N workerów / processorów (każdy z osobnym `WhisperProcessor`, ale współdzielonym `WhisperFactory`).
