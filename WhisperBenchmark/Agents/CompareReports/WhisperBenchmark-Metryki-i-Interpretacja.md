# WhisperBenchmark – metryki, interpretacja wyników i porównywanie benchmarków

Ten dokument służy jako stała ściąga do interpretacji wyników `benchmark-summary.json`, `benchmark-files.csv`, `benchmark-calls.csv` oraz wyników trybu `dataset` w projekcie **WhisperBenchmark**.

Celem benchmarku jest odpowiedzieć na pytanie:

> Ile realnego audio jest w stanie przetworzyć dana maszyna/GPU w określonym czasie oraz jak przewidzieć, ile zajmie przetworzenie większego datasetu nagrań?

Najważniejsza metryka to **RTF** / **audioHoursPerHour**.

---

## 1. Kontekst trybu `dataset`

Rekomendowany tryb dla realnej symulacji produkcyjnej to:

```bash
dotnet run -c Release --project WhisperBenchmark -- \
  dataset \
  --input /data/input \
  --output /data/output \
  --gpu-concurrency 4
```

Tryb `dataset`:

- skanuje cały `InputDirectory`,
- przechodzi po wszystkich podfolderach `interactionId`,
- przetwarza wszystkie poprawne pliki WAV dokładnie raz,
- nie używa `DurationMinutes`,
- nie zapętla datasetu,
- kończy dopiero po przetworzeniu wszystkich plików,
- generuje metryki do predykcji wydajności GPU/maszyny.

Struktura wejściowa:

```text
InputDirectory/
  {interactionId}/
    {interactionId}_{participantId}.wav
```

Przykład:

```text
/data/input/
  100/
    100_1.wav
    100_2.wav
  101/
    101_1.wav
  102/
    102_1.wav
    102_2.wav
    102_3.wav
```

Założenie benchmarku:

```text
1 plik WAV = 1 leg rozmowy = 1 job GPU
1 folder interactionId = 1 rozmowa / interaction / call
```

Jeżeli rozmowa ma dwa legi:

```text
100_1.wav = klient, 120 s
100_2.wav = agent, 110 s
```

to sumaryczna długość audio dla interaction `100` wynosi:

```text
120 s + 110 s = 230 s audio
```

---

## 2. Najważniejsze pliki wynikowe

Typowe pliki w `OutputDirectory`:

```text
benchmark-summary.json      # główne podsumowanie runa
benchmark-summary.csv       # summary w formie key/value
benchmark-files.csv         # metryki per plik / leg
benchmark-calls.csv         # metryki per interactionId / call
benchmark-sweep.csv         # porównanie sweep, jeśli używany
errors.json                 # błędy walidacji/transkrypcji
 gpu-metrics.csv             # metryki GPU z nvidia-smi, jeśli włączone
```

Najważniejszy plik do interpretacji to:

```text
benchmark-summary.json
```

---

## 3. Najważniejsze pojęcia

### 3.1. Audio duration

To długość nagrania audio.

Przykład:

```json
"processedAudioSeconds": 125.68
```

Oznacza, że przetworzono plik albo zbiór plików o łącznej długości:

```text
125.68 s = 2 min 5.68 s audio
```

---

### 3.2. Wall-clock time

To realny czas zegarowy, jaki zajęło przetwarzanie.

Przykład:

```json
"wallClockSeconds": 1322.0
```

Oznacza:

```text
1322 s = 22.03 min realnego czasu pracy aplikacji
```

---

### 3.3. RTF – Real-Time Factor

Najważniejsza metryka wydajności.

Wzór:

```text
RTF = audioSecondsProcessed / wallClockSeconds
```

Przykład:

```text
28800 s audio / 1322 s przetwarzania = 21.79x
```

Interpretacja:

```text
RTF = 21.79x
```

oznacza:

```text
Maszyna przetwarza 21.79 godzin audio w 1 godzinę pracy.
```

Czyli działa **21.79 razy szybciej niż real time**.

---

### 3.4. audioHoursPerHour

To biznesowa nazwa dla RTF.

```json
"audioHoursPerHour": 21.79
```

Oznacza:

```text
1 godzina pracy tej maszyny przetwarza około 21.79 godzin audio.
```

W praktyce:

```text
audioHoursPerHour = rtf
```

To pole jest czytelniejsze biznesowo niż `rtf`.

---

### 3.5. processingTimePercentOfAudioDuration

Pokazuje, jaki procent długości audio zajęło przetwarzanie.

Wzór:

```text
processingTimePercentOfAudioDuration = (wallClockSeconds / datasetAudioSeconds) * 100
```

Przykład:

```text
1322 / 28800 * 100 = 4.59%
```

Interpretacja:

```text
Przetworzenie datasetu zajęło 4.59% jego długości audio.
```

Czyli jeżeli dataset miał 8 godzin audio, to został przetworzony w czasie równym około 4.59% z 8 godzin.

---

## 4. Pola `benchmark-summary.json` – opis linia po linii

### Informacje techniczne runa

#### `mode`

```json
"mode": "dataset"
```

Tryb pracy benchmarku.

Najważniejsze tryby:

```text
single  - pojedynczy plik
soak    - test przez zadany czas
sweep   - test wielu wartości GpuConcurrency
dataset - przetworzenie całego datasetu raz
```

Dla realnej symulacji dnia pracy najważniejszy jest `dataset`.

---

#### `startedAt`

```json
"startedAt": "2026-05-26T15:33:59Z"
```

Czas startu benchmarku.

---

#### `finishedAt`

```json
"finishedAt": "2026-05-26T15:56:01Z"
```

Czas zakończenia benchmarku.

---

#### `model`

```json
"model": "ggml-large-v3-turbo.bin"
```

Model Whisper użyty w teście.

Wyników z różnych modeli nie należy porównywać bezpośrednio jako tej samej konfiguracji, bo model wpływa bardzo mocno na wydajność i jakość.

---

#### `language`

```json
"language": "pl"
```

Język ustawiony dla transkrypcji.

---

#### `useGpu`

```json
"useGpu": true
```

Czy benchmark miał używać GPU.

Uwaga: warto dodatkowo sprawdzić logi aplikacji, czy faktycznie runtime był CUDA, a nie fallback CPU.

---

#### `gpuDevice`

```json
"gpuDevice": 0
```

Numer użytej karty GPU.

---

#### `gpuConcurrency`

```json
"gpuConcurrency": 4
```

Maksymalna liczba równoległych transkrypcji uruchamianych na GPU.

To jest jeden z najważniejszych parametrów testu.

Typowo testuje się wartości:

```text
1, 2, 4, 8, 12, 16
```

Największa wartość nie zawsze oznacza najlepszy wynik. Zbyt wysokie `GpuConcurrency` może powodować spadek throughputu albo błędy pamięci.

---

#### `inputDirectory`

```json
"inputDirectory": "/data/input"
```

Folder wejściowy datasetu.

---

## 5. Metryki liczby plików i interakcji

### `interactionsDiscovered`

```json
"interactionsDiscovered": 500
```

Liczba katalogów `interactionId` znalezionych w `InputDirectory`.

---

### `filesDiscovered`

```json
"filesDiscovered": 1000
```

Liczba plików WAV znalezionych przed walidacją / przetwarzaniem.

---

### `processedInteractions`

```json
"processedInteractions": 500
```

Liczba interakcji przetworzonych poprawnie.

Interakcja jest poprawnie przetworzona, jeżeli wszystkie jej wymagane pliki/legi zakończyły się sukcesem.

---

### `processedFiles`

```json
"processedFiles": 1000
```

Liczba plików WAV / legów przetworzonych poprawnie.

---

### `failedInteractions`

```json
"failedInteractions": 0
```

Liczba interakcji, w których wystąpił co najmniej jeden błąd pliku.

---

### `failedFiles`

```json
"failedFiles": 0
```

Liczba plików, które nie przeszły walidacji albo transkrypcji.

---

### `errors`

```json
"errors": 0
```

Łączna liczba błędów zapisanych do `errors.json`.

Jeżeli `errors > 0`, trzeba sprawdzić `errors.json`.

---

## 6. Metryki długości audio

### `datasetAudioSeconds`

```json
"datasetAudioSeconds": 28800.0
```

Łączna długość wszystkich poprawnie przetworzonych plików audio, w sekundach.

Przykład:

```text
28800 s = 8 h audio
```

---

### `datasetAudioHours`

```json
"datasetAudioHours": 8.0
```

Łączna długość audio w godzinach.

Wzór:

```text
datasetAudioHours = datasetAudioSeconds / 3600
```

To jedna z najważniejszych wartości biznesowych.

---

### `averageFileAudioSeconds`

```json
"averageFileAudioSeconds": 28.8
```

Średnia długość jednego pliku / lega.

Wzór:

```text
averageFileAudioSeconds = datasetAudioSeconds / processedFiles
```

---

### `averageInteractionAudioSeconds`

```json
"averageInteractionAudioSeconds": 57.6
```

Średnia łączna długość audio jednej interakcji.

Wzór:

```text
averageInteractionAudioSeconds = datasetAudioSeconds / processedInteractions
```

Przykład:

```text
interaction 100:
  100_1.wav = 120 s
  100_2.wav = 110 s

averageInteractionAudioSeconds dla tej jednej interakcji = 230 s
```

To pole jest bardzo ważne do predykcji liczby interakcji na godzinę.

---

## 7. Metryki czasu przetwarzania

### `wallClockSeconds`

```json
"wallClockSeconds": 1322.0
```

Realny czas przetwarzania datasetu w sekundach.

---

### `wallClockMinutes`

```json
"wallClockMinutes": 22.03
```

Realny czas przetwarzania datasetu w minutach.

Wzór:

```text
wallClockMinutes = wallClockSeconds / 60
```

---

### `avgFileProcessingSeconds`

```json
"avgFileProcessingSeconds": 2.64
```

Średni czas przetwarzania jednego pliku.

---

### `p50FileProcessingSeconds`

```json
"p50FileProcessingSeconds": 2.31
```

Mediana czasu przetwarzania pliku.

Interpretacja:

```text
50% plików przetworzyło się w 2.31 s albo szybciej.
```

---

### `p95FileProcessingSeconds`

```json
"p95FileProcessingSeconds": 6.42
```

Percentyl 95 czasu przetwarzania pliku.

Interpretacja:

```text
95% plików przetworzyło się w 6.42 s albo szybciej.
```

To jest ważne do oceny ogona czasów.

---

### `p99FileProcessingSeconds`

```json
"p99FileProcessingSeconds": 9.87
```

Percentyl 99 czasu przetwarzania pliku.

Interpretacja:

```text
99% plików przetworzyło się w 9.87 s albo szybciej.
```

Jeżeli p99 jest dużo większe niż p50, oznacza to, że część plików trwa wyraźnie dłużej.

---

### `avgInteractionProcessingSeconds`

```json
"avgInteractionProcessingSeconds": 5.11
```

Średni czas domknięcia jednej interakcji.

Dla interakcji z wieloma participantami liczymy:

```text
interactionProcessingSeconds = lastFileFinishedAt - firstFileStartedAt
```

Jeżeli legi były przetwarzane równolegle, czas interakcji może być bliższy najdłuższemu legowi niż sumie czasów wszystkich legów.

---

### `p95InteractionProcessingSeconds`

```json
"p95InteractionProcessingSeconds": 13.8
```

Percentyl 95 czasu domknięcia całej interakcji.

To jest ważniejsze dla SLA niż `p95FileProcessingSeconds`, bo produkcyjnie interesuje nas najczęściej, kiedy gotowa jest cała rozmowa, a nie pojedynczy leg.

---

## 8. Metryki throughputu

### `rtf`

```json
"rtf": 21.79
```

Najważniejsza metryka wydajności.

Wzór:

```text
rtf = datasetAudioSeconds / wallClockSeconds
```

Interpretacja:

```text
RTF 21.79x = 21.79 h audio / 1 h pracy maszyny
```

---

### `audioHoursPerHour`

```json
"audioHoursPerHour": 21.79
```

Biznesowa nazwa dla RTF.

Interpretacja:

```text
Jedna godzina pracy maszyny przetwarza 21.79 godzin audio.
```

---

### `filesPerHour`

```json
"filesPerHour": 2723.1
```

Liczba plików / legów przetwarzanych na godzinę.

Wzór:

```text
filesPerHour = processedFiles / (wallClockSeconds / 3600)
```

To metryka techniczna, bo zależy od długości plików.

---

### `interactionsPerHour`

```json
"interactionsPerHour": 1361.5
```

Liczba pełnych interakcji przetwarzanych na godzinę.

Wzór:

```text
interactionsPerHour = processedInteractions / (wallClockSeconds / 3600)
```

To dobra metryka biznesowa, ale zależy od średniej długości interaction.

---

### `processingTimePercentOfAudioDuration`

```json
"processingTimePercentOfAudioDuration": 4.59
```

Pokazuje, jak długo trwało przetwarzanie w stosunku do długości audio.

Interpretacja:

```text
Przetwarzanie zajęło 4.59% długości audio.
```

Czyli:

```text
100 h audio zostanie przetworzone w około 4.59 h
```

---

## 9. Metryki RTF per plik

### `avgFileRtf`

```json
"avgFileRtf": 12.4
```

Średni RTF liczony osobno dla plików.

Uwaga: `avgFileRtf` może różnić się od globalnego `rtf`, bo globalny `rtf` uwzględnia równoległość i cały wall-clock benchmarku.

Najważniejszy dla przepustowości całej maszyny jest globalny:

```text
rtf / audioHoursPerHour
```

---

### `p50FileRtf`

```json
"p50FileRtf": 13.1
```

Mediana RTF per plik.

---

### `p95FileRtf`

```json
"p95FileRtf": 18.2
```

Percentyl 95 RTF per plik.

Uwaga: w przypadku RTF wyższa wartość oznacza szybciej. Dlatego p95FileRtf oznacza, że 95% plików ma RTF nie większy niż ta wartość, zależnie od sposobu liczenia percentyli.

Do porównywania wydajności najczęściej używaj globalnego `rtf`.

---

## 10. Sekcja `capacityPrediction`

Przykład:

```json
"capacityPrediction": {
  "audioHoursPerHour": 21.79,
  "estimatedAudioHoursPer8HourShift": 174.32,
  "estimatedInteractionsPerHourByAverageDuration": 1361.5,
  "estimatedInteractionsPer8HourShiftByAverageDuration": 10892,
  "processingTimeFor2AudioHoursMinutes": 5.51,
  "processingTimeFor8AudioHoursMinutes": 22.03,
  "processingTimeFor24AudioHoursMinutes": 66.09,
  "processingTimeFor100AudioHoursMinutes": 275.36
}
```

### `estimatedAudioHoursPer8HourShift`

Wzór:

```text
estimatedAudioHoursPer8HourShift = audioHoursPerHour * 8
```

Przykład:

```text
21.79 * 8 = 174.32 h audio
```

Interpretacja:

```text
Jedna maszyna w 8 godzin pracy może przetworzyć około 174.32 h audio.
```

---

### `estimatedInteractionsPerHourByAverageDuration`

Wzór:

```text
estimatedInteractionsPerHourByAverageDuration =
  (rtf * 3600) / averageInteractionAudioSeconds
```

Przykład:

```text
RTF = 21.79
averageInteractionAudioSeconds = 57.6

21.79 * 3600 / 57.6 = 1361.5 interaction/h
```

Interpretacja:

```text
Przy średniej długości interaction 57.6 s maszyna powinna robić około 1361.5 interaction/h.
```

---

### `processingTimeForXAudioHoursMinutes`

Wzór:

```text
processingTimeForXAudioHoursMinutes = (X / audioHoursPerHour) * 60
```

Przykład dla 100 godzin audio:

```text
100 / 21.79 * 60 = 275.36 min
```

Czyli:

```text
100 h audio zostanie przetworzone w około 4 h 35 min.
```

---

## 11. Jak interpretować przykładowy wynik

Przykład:

```json
{
  "datasetAudioHours": 8.0,
  "wallClockMinutes": 22.03,
  "rtf": 21.79,
  "audioHoursPerHour": 21.79,
  "processingTimePercentOfAudioDuration": 4.59
}
```

Interpretacja:

```text
Do aplikacji wrzucono 8 godzin audio.
GPU przetworzyło to w 22.03 minuty.
Maszyna działa 21.79x szybciej niż real time.
Jedna godzina pracy tej maszyny przetwarza około 21.79 godzin audio.
Przetwarzanie zajmuje około 4.59% długości audio.
```

Predykcja:

```text
czasPrzetwarzania = audioHours / audioHoursPerHour
```

Przykłady:

```text
2 h audio   -> 2 / 21.79 * 60 = 5.51 min
8 h audio   -> 8 / 21.79 * 60 = 22.03 min
24 h audio  -> 24 / 21.79 * 60 = 66.09 min
100 h audio -> 100 / 21.79 * 60 = 275.36 min = 4 h 35 min
```

---

## 12. Jak interpretować bardzo krótki test z jednym plikiem

Przykład:

```json
{
  "durationSeconds": 6.6148698,
  "processedFiles": 1,
  "processedCalls": 1,
  "processedAudioSeconds": 125.68,
  "audioHoursProcessed": 0.034911111111111115,
  "rtf": 18.999618102838546,
  "filesPerHour": 544.2283988718871,
  "callsPerHour": 544.2283988718871,
  "avgFileProcessingSeconds": 6.6148698,
  "p50FileProcessingSeconds": 6.6148698,
  "p95FileProcessingSeconds": 6.6148698,
  "p99FileProcessingSeconds": 6.6148698,
  "avgFileRtf": 18.999618102838546
}
```

Interpretacja:

```text
Przetworzono 1 plik audio.
Długość pliku: 125.68 s.
Czas przetwarzania: 6.61 s.
RTF: około 19x.
```

Wzór:

```text
125.68 / 6.6148698 = 18.9996
```

Czyli:

```text
Maszyna przetwarza około 19 h audio / h pracy.
```

Ale taki test jest mało wiarygodny produkcyjnie, bo:

- dotyczy tylko jednego pliku,
- trwa kilka sekund,
- percentyle p50/p95/p99 są identyczne,
- `filesPerHour` i `callsPerHour` są estymacją z jednego pliku,
- nie pokazuje stabilności GPU przy dłuższym obciążeniu.

Wniosek:

```text
Dobry sanity check, ale nie wynik do decyzji produkcyjnej.
```

Do decyzji produkcyjnej używaj trybu `dataset` albo długiego `sweep`/`soak`.

---

## 13. Dlaczego p50/p95/p99 są takie same przy jednym pliku?

Jeżeli test ma tylko jeden plik:

```json
"processedFiles": 1
```

to wszystkie percentyle czasu pliku będą takie same:

```json
"p50FileProcessingSeconds": 6.61,
"p95FileProcessingSeconds": 6.61,
"p99FileProcessingSeconds": 6.61
```

Bo istnieje tylko jedna wartość.

Percentyle mają sens dopiero przy większej próbce, np.:

```text
100 plików
500 plików
1000 plików
```

---

## 14. Jak porównywać dwa wyniki benchmarku

Porównując dwa pliki `benchmark-summary.json`, sprawdź najpierw, czy konfiguracja jest porównywalna.

### 14.1. Najpierw sprawdź, czy porównanie jest uczciwe

Porównaj:

```text
model
language
gpuDevice
gpuConcurrency
inputDirectory / dataset
useGpu
WriteTranscriptionJson
CollectGpuMetrics
```

Wyniki są najlepiej porównywalne, gdy:

```text
ten sam model
ten sam język
ten sam dataset
ta sama maszyna/GPU
różni się tylko jeden parametr, np. gpuConcurrency
```

---

### 14.2. Najważniejsze pola do porównania

Porównuj przede wszystkim:

```text
rtf
audioHoursPerHour
wallClockMinutes
processingTimePercentOfAudioDuration
filesPerHour
interactionsPerHour
p95FileProcessingSeconds
p95InteractionProcessingSeconds
errors / failedFiles / failedInteractions
```

---

### 14.3. Jak interpretować różnice

#### Porównanie RTF

```text
rtf_A = 21.79
rtf_B = 28.50
```

Wzór na poprawę:

```text
improvementPercent = ((rtf_B - rtf_A) / rtf_A) * 100
```

Przykład:

```text
((28.50 - 21.79) / 21.79) * 100 = 30.8%
```

Interpretacja:

```text
Wynik B ma o około 30.8% większy throughput audio niż wynik A.
```

---

#### Porównanie wall-clock

```text
wallClock_A = 22.03 min
wallClock_B = 16.85 min
```

Wzór:

```text
speedup = wallClock_A / wallClock_B
```

Przykład:

```text
22.03 / 16.85 = 1.31x
```

Interpretacja:

```text
Wynik B zakończył przetwarzanie około 1.31x szybciej.
```

---

#### Porównanie p95

Jeżeli:

```text
p95InteractionProcessingSeconds_A = 30 s
p95InteractionProcessingSeconds_B = 45 s
```

to wynik B może mieć lepszy throughput globalny, ale gorsze ogony czasów.

Interpretacja:

```text
B jest szybszy średnio, ale ma gorszą przewidywalność/SLA dla części interakcji.
```

---

## 15. Szablon interpretacji jednego wyniku

Po otrzymaniu `benchmark-summary.json` można użyć tego schematu:

```text
Wynik benchmarku:
- Tryb: {mode}
- Model: {model}
- Język: {language}
- GPU concurrency: {gpuConcurrency}
- Liczba interakcji: {processedInteractions}
- Liczba plików/legów: {processedFiles}
- Dataset audio: {datasetAudioHours} h
- Czas przetwarzania: {wallClockMinutes} min
- RTF / audioHoursPerHour: {rtf}x

Interpretacja:
Maszyna przetwarza około {audioHoursPerHour} godzin audio w 1 godzinę pracy.
Przetworzenie tego datasetu zajęło {processingTimePercentOfAudioDuration}% jego długości audio.
Przy średniej długości interakcji {averageInteractionAudioSeconds} s daje to około {interactionsPerHour} interakcji/h.

Predykcja:
- 2 h audio: {processingTimeFor2AudioHoursMinutes} min
- 8 h audio: {processingTimeFor8AudioHoursMinutes} min
- 24 h audio: {processingTimeFor24AudioHoursMinutes} min
- 100 h audio: {processingTimeFor100AudioHoursMinutes} min

Ryzyka / uwagi:
- Błędy: {errors}
- Failed files: {failedFiles}
- Failed interactions: {failedInteractions}
- p95 interaction processing: {p95InteractionProcessingSeconds} s
```

---

## 16. Szablon porównania dwóch wyników

```text
Porównanie benchmarków A vs B:

Konfiguracja:
- A: model={modelA}, gpuConcurrency={gpuConcurrencyA}, dataset={inputDirectoryA}
- B: model={modelB}, gpuConcurrency={gpuConcurrencyB}, dataset={inputDirectoryB}

Czy porównanie jest uczciwe?
- Ten sam model: TAK/NIE
- Ten sam dataset: TAK/NIE
- Ta sama maszyna/GPU: TAK/NIE
- Różni się tylko gpuConcurrency: TAK/NIE

Throughput:
- A rtf/audioHoursPerHour: {rtfA}
- B rtf/audioHoursPerHour: {rtfB}
- Różnica: {improvementPercent}%

Czas całego datasetu:
- A wallClockMinutes: {wallClockMinutesA}
- B wallClockMinutes: {wallClockMinutesB}
- Speedup: {speedup}x

Interakcje:
- A interactionsPerHour: {interactionsPerHourA}
- B interactionsPerHour: {interactionsPerHourB}

Ogony czasów:
- A p95InteractionProcessingSeconds: {p95InteractionProcessingSecondsA}
- B p95InteractionProcessingSeconds: {p95InteractionProcessingSecondsB}

Błędy:
- A errors: {errorsA}
- B errors: {errorsB}

Wniosek:
Wynik B jest szybszy/wolniejszy od A o X%, ale należy zwrócić uwagę na p95/p99 oraz błędy.
```

---

## 17. Minimalny zestaw metryk do decyzji produkcyjnej

Do decyzji, ile GPU/maszyn potrzeba, najważniejsze są:

```text
model
gpuConcurrency
datasetAudioHours
wallClockMinutes
rtf
audioHoursPerHour
processingTimePercentOfAudioDuration
processedInteractions
processedFiles
averageInteractionAudioSeconds
interactionsPerHour
p95InteractionProcessingSeconds
errors
failedFiles
failedInteractions
```

Jeżeli trzeba wybrać tylko 5 metryk, wybierz:

```text
1. datasetAudioHours
2. wallClockMinutes
3. rtf / audioHoursPerHour
4. averageInteractionAudioSeconds
5. p95InteractionProcessingSeconds
```

---

## 18. Najprostszy wzór do predykcji

Jeżeli znasz:

```text
audioHoursPerHour = 21.79
```

to dla dowolnej liczby godzin audio:

```text
czasPrzetwarzaniaGodziny = audioHours / audioHoursPerHour
czasPrzetwarzaniaMinuty = (audioHours / audioHoursPerHour) * 60
```

Przykład:

```text
80 h audio / 21.79 = 3.67 h = 3 h 40 min
```

Interpretacja:

```text
Jedna maszyna z takim wynikiem przetworzy 80 h audio w około 3 h 40 min.
```

---

## 19. Jak odpowiadać na pytanie: czy jedna L40 wystarczy?

Potrzebujesz znać:

```text
ile godzin audio powstaje dziennie
jaki jest wymagany czas zakończenia transkrypcji
jaki jest audioHoursPerHour z benchmarku
```

Wzór:

```text
requiredProcessingHours = dailyAudioHours / audioHoursPerHour
```

Przykład:

```text
dailyAudioHours = 100 h
audioHoursPerHour = 21.79

requiredProcessingHours = 100 / 21.79 = 4.59 h
```

Wniosek:

```text
Jeżeli akceptujesz, że 100 h audio zostanie przetworzone w około 4 h 35 min, jedna maszyna wystarczy.
Jeżeli SLA wymaga zakończenia w 1 godzinę, potrzeba około 5 takich maszyn/GPU.
```

Wzór na liczbę GPU:

```text
requiredGpuCount = ceil(requiredProcessingHours / allowedProcessingHours)
```

Przykład:

```text
requiredProcessingHours = 4.59
allowedProcessingHours = 1
requiredGpuCount = ceil(4.59 / 1) = 5
```

---

## 20. Uwaga o jakości porównania

Nie porównuj bezpośrednio wyników, jeśli zmienił się:

```text
model
język
format audio
dataset
liczba plików
średnia długość interakcji
gpuConcurrency
zapis transkrypcji JSON
wersja Whisper.net / runtime CUDA
maszyna/GPU
```

Najlepsza praktyka:

```text
Zmieniaj jeden parametr naraz.
```

Przykład dobrego testu:

```text
Ten sam dataset, ten sam model, ta sama maszyna:
- gpuConcurrency = 1
- gpuConcurrency = 2
- gpuConcurrency = 4
- gpuConcurrency = 8
```

To pozwala znaleźć najlepszy punkt pracy GPU.

---

## 21. Krótka ściąga interpretacji

```text
rtf = 1x
  -> real time, 1 h audio w 1 h pracy

rtf = 10x
  -> 10 h audio w 1 h pracy

rtf = 20x
  -> 20 h audio w 1 h pracy

rtf = 50x
  -> 50 h audio w 1 h pracy
```

```text
processingTimePercentOfAudioDuration = 5%
  -> 100 h audio przetwarza się w około 5 h
```

```text
p95InteractionProcessingSeconds = 60 s
  -> 95% interakcji domyka się w 60 s albo szybciej
```

```text
errors = 0
  -> brak błędów walidacji/transkrypcji
```

---

## 22. Rekomendacja praktyczna

Do realnego POC używaj przede wszystkim:

```text
dataset mode
```

Do szukania najlepszego `GpuConcurrency` używaj:

```text
sweep mode
```

Do szybkiego sprawdzenia, czy model/GPU działa:

```text
single mode
```

Do testu stabilności przez określony czas:

```text
soak mode
```

Najważniejszy wynik dla decyzji produkcyjnej:

```text
audioHoursPerHour
```

Najprostsza predykcja:

```text
czasPrzetwarzania = liczbaGodzinAudio / audioHoursPerHour
```

Przykład:

```text
80 h audio / 21.79 h/h = 3.67 h
```

Wniosek:

```text
Maszyna przetworzy 80 h audio w około 3 h 40 min.
```