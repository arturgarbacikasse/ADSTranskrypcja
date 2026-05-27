# Porównanie benchmarków: `gpu-concurrency 1` vs `gpu-concurrency 2`

**Data runów:** 2026-05-27
**Maszyna / GPU:** NVIDIA GeForce RTX 3050 Ti Laptop GPU (4 GB VRAM)
**Model:** `ggml-medium-q5_0.bin`
**Język:** `pl`
**Dataset:** 3 interakcje / 6 plików / 644.23 s audio (≈ 10 min 44 s)
**Źródła danych:**

- `Data/Output/Gpu1_medium-q5/benchmark-summary.json`
- `Data/Output/Gpu2_medium-q5/benchmark-summary.json`
- `gpu-metrics.csv` z obu folderów

---

## 1. Czy porównanie jest uczciwe?

| Warunek | Status |
|---|---|
| Ten sam model (`ggml-medium-q5_0.bin`) | TAK |
| Ten sam język (`pl`) | TAK |
| Ten sam dataset (3 interakcje, 6 plików, 644.23 s audio) | TAK |
| Ta sama maszyna/GPU (RTX 3050 Ti Laptop) | TAK |
| Różni się tylko `gpuConcurrency` (1 vs 2) | TAK |

Porównanie jest w pełni miarodajne — klasyczny *sweep* po `gpuConcurrency`.

---

## 2. Kluczowe metryki side-by-side

| Metryka | A: GPU=1 | B: GPU=2 | Różnica B vs A |
|---|---:|---:|---:|
| `wallClockSeconds` | 41.96 s | 32.00 s | **-23.7%** (szybciej) |
| `rtf` / `audioHoursPerHour` | **15.35x** | **20.13x** | **+31.1%** |
| `processingTimePercentOfAudioDuration` | 6.51% | 4.97% | -23.7% |
| `filesPerHour` | 514.7 | 674.9 | +31.1% |
| `interactionsPerHour` | 257.4 | 337.5 | +31.1% |
| `avgFileProcessingSeconds` | 6.99 s | 10.46 s | **+49.6%** (wolniej!) |
| `p95FileProcessingSeconds` | 9.01 s | 13.60 s | +50.9% |
| `avgInteractionProcessingSeconds` | 13.99 s | **10.87 s** | -22.3% (szybciej) |
| `p95InteractionProcessingSeconds` | 17.43 s | **13.37 s** | -23.3% |
| `p99InteractionProcessingSeconds` | 17.87 s | 13.67 s | -23.5% |
| `errors` / `failedFiles` / `failedInteractions` | 0 / 0 / 0 | 0 / 0 / 0 | bez zmian |

### Predykcja z `capacityPrediction`

| Czas potrzebny na… | GPU=1 | GPU=2 |
|---|---:|---:|
| 2 h audio | 7.82 min | 5.96 min |
| 8 h audio | 31.27 min | 23.85 min |
| 24 h audio | 93.80 min | **71.54 min** |
| 100 h audio | 390.83 min (≈ 6 h 31 min) | **298.07 min (≈ 4 h 58 min)** |
| `estimatedAudioHoursPer8HourShift` | 122.82 h | **161.04 h** |

---

## 3. Metryki GPU (`gpu-metrics.csv`)

| Wskaźnik | GPU=1 | GPU=2 |
|---|---|---|
| Utilization GPU | 77 – 79 % | **87 – 100 %** (saturacja) |
| Memory used | ~1484 MB | ~2852 MB (≈ 70 % z 4 GB) |
| Power draw | 70 – 74 W | 78 – 79 W |
| Temperatura | 63 – 72 °C | 66 – 74 °C |

Pełniejsze obciążenie GPU przy concurrency=2 to dokładnie to, czego oczekujemy — jeden strumień nie wystarczał, żeby wysycić kartę.

---

## 4. Co właściwie się dzieje — interpretacja

### 4.1. Throughput rośnie o ~31% (z 15.35x do 20.13x RTF)

Przy `gpuConcurrency=2` dwa pliki są transkrybowane jednocześnie. GPU było wcześniej niedociążone (~78 % util) i teraz dochodzi do realnej saturacji (87–100 %). Dzięki temu cały dataset 644 s audio kończy się w 32 s zamiast 42 s.

### 4.2. Pojedynczy plik (leg) jest **wolniejszy** o ~50%

To jest klucz do zrozumienia tego benchmarku:

- GPU=1: `avgFileProcessingSeconds = 6.99 s`, `avgFileRtf = 15.29x`
- GPU=2: `avgFileProcessingSeconds = 10.46 s`, `avgFileRtf = 10.23x`

Każdy pojedynczy plik trwa dłużej, bo dwa pliki współdzielą zasoby GPU (compute units, pamięć, scheduler). To **normalne i oczekiwane**. Nie ma sensu patrzeć na czas pojedynczego pliku przy `concurrency > 1` jako miary „czy działa szybciej".

### 4.3. Interakcja (cała rozmowa) jest **szybsza** o ~22%

- GPU=1: `avgInteractionProcessingSeconds = 13.99 s` (legi przetwarzane sekwencyjnie)
- GPU=2: `avgInteractionProcessingSeconds = 10.87 s` (oba legi równolegle)

Tu widać prawdziwą korzyść: rozmowa `1_1.wav + 1_2.wav` przy concurrency=2 leci jednocześnie, więc czas całej rozmowy = `max(leg1, leg2)`, a nie suma. Dzięki temu `p95InteractionProcessingSeconds` spada z 17.43 s do 13.37 s.

### 4.4. Ogony (p95/p99) — bez regresji

Mimo że pliki są wolniejsze, p95/p99 **interakcji** są lepsze, a `errors = 0` w obu runach. Nie ma żadnego ryzyka regresji jakościowej / stabilności przy zwiększeniu concurrency z 1 na 2.

---

## 5. Wniosek dla decyzji produkcyjnej

```text
gpuConcurrency=2 jest jednoznacznie lepszy od gpuConcurrency=1 na tej maszynie:
  +31% throughput (RTF 15.35x -> 20.13x)
  -24% wall-clock dla tego datasetu
  -22% p95 czasu interakcji
  bez błędów, bez failed files/interactions
  zużycie pamięci GPU: ~2.85 GB / 4 GB (jest jeszcze miejsce)
```

---

## 6. Co dalej — rekomendacje sweep'u

Speedup z 1 → 2 wynosi **1.31x**, mimo że teoretycznie powinien być bliski 2x. Powód widać w `gpu-metrics.csv`: przy concurrency=2 GPU jest już w okolicach 87–100 % utilizacji. To oznacza, że krzywa wydajności zaczyna się wypłaszczać.

Sugerowane następne kroki w sweep'ie:

1. **`gpu-concurrency 3`** — sprawdzić, czy jest jeszcze marginalny zysk. Przy 100 % util to raczej nie, ale warto zweryfikować dla pewności (model `medium-q5` zajmuje ~1.4 GB GPU dla pierwszej instancji + bufory na strumień; przy concurrency=3 będziemy w okolicy 3.5–3.8 GB / 4 GB, czyli na granicy).
2. **`gpu-concurrency 4`** — prawie na pewno OOM lub silna degradacja na 4 GB karcie. Trzeba zmierzyć empirycznie.
3. **Większy dataset** — ten run trwał tylko 32–42 s. Dla solidnych liczb warto powtórzyć na min. 1–2 h audio, bo przy tak krótkich runach szum (cache warm-up, init Whisper.net, pierwsze próbkowanie `nvidia-smi`) zaburza p95/p99.

Bazując na obecnych danych, na **RTX 3050 Ti Laptop 4 GB**, sweet spotem jest najpewniej `gpuConcurrency = 2` — z perspektywy capacity 1 maszyna przerobi ok. **161 h audio na 8-godzinną zmianę** (vs 122 h dla concurrency=1).

---

## 7. TL;DR

| | GPU=1 | GPU=2 |
|---|---:|---:|
| RTF (audio h / 1 h pracy) | 15.35x | **20.13x** |
| Czas datasetu | 41.96 s | **32.00 s** |
| p95 interakcji | 17.43 s | **13.37 s** |
| Util GPU | ~78 % | **87–100 %** |
| Pamięć GPU | 1.48 GB | 2.85 GB |
| Błędy | 0 | 0 |

**Rekomendacja:** używać `--gpu-concurrency 2` jako baseline produkcyjny dla tej konfiguracji sprzętu i modelu. Następny krok eksperymentalny: zmierzyć `gpu-concurrency 3` na większym datasecie.
