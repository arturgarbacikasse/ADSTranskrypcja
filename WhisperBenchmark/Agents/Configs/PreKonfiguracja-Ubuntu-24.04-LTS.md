# PreKonfiguracja Ubuntu 24.04 LTS dla WhisperBenchmark

Instrukcja przygotowania Ubuntu 24.04 LTS pod projekt `WhisperBenchmark` / `ADSTranskrypcja`.

Docelowe środowisko:

- Ubuntu 24.04 LTS
- NVIDIA GPU z CUDA, np. NVIDIA GeForce RTX 3050 Ti Laptop GPU
- NVIDIA Driver 595.x lub nowszy kompatybilny z CUDA 13
- CUDA Toolkit 13.x
- .NET 10 SDK
- Git
- ffmpeg
- Cursor / VS Code Remote SSH

Projekt uruchamia benchmark transkrypcji Whisper.net / whisper.cpp na GPU.

---

## 1. Aktualizacja systemu

```bash
sudo apt update
sudo apt upgrade -y
```

---

## 2. Pakiety bazowe

Zainstaluj podstawowe narzędzia developerskie:

```bash
sudo apt install -y \
  git \
  curl \
  wget \
  unzip \
  ca-certificates \
  build-essential \
  pkg-config \
  ffmpeg \
  jq \
  htop \
  nvtop \
  openssh-server
```

Opis najważniejszych pakietów:

| Pakiet | Po co |
|---|---|
| `git` | pobieranie repozytorium |
| `curl`, `wget` | pobieranie plików/modeli |
| `build-essential` | podstawowe narzędzia kompilacji |
| `ffmpeg` | konwersja audio do WAV mono/16kHz/PCM-16 |
| `jq` | czytelne podglądanie JSON-ów |
| `htop` | monitoring CPU/RAM |
| `nvtop` | monitoring GPU |
| `openssh-server` | połączenie z Cursor / VS Code przez SSH |

Włącz SSH:

```bash
sudo systemctl enable --now ssh
systemctl status ssh --no-pager
```

Sprawdź IP maszyny:

```bash
hostname -I
```

---

## 3. Instalacja sterownika NVIDIA

Najpierw sprawdź, czy Ubuntu widzi kartę i jaki driver rekomenduje:

```bash
ubuntu-drivers devices
```

Przykładowy wynik dla RTX 3050 Ti Mobile:

```text
model    : GA107M [GeForce RTX 3050 Ti Mobile]
driver   : nvidia-driver-595-open - distro non-free recommended
```

Zainstaluj rekomendowany sterownik:

```bash
sudo ubuntu-drivers install
sudo reboot
```

Po restarcie sprawdź:

```bash
nvidia-smi
```

Oczekiwany efekt: `nvidia-smi` pokazuje kartę, wersję drivera i wersję CUDA.

Dodatkowa komenda kontrolna:

```bash
nvidia-smi --query-gpu=name,driver_version --format=csv
```

Przykład poprawnego wyniku:

```text
name, driver_version
NVIDIA GeForce RTX 3050 Ti Laptop GPU, 595.71.05
```

---

## 4. Ważne: `nvidia-smi` to nie wszystko

Jeżeli działa `nvidia-smi`, to znaczy, że działa sterownik NVIDIA.

Ale do Whisper.net CUDA potrzebne są też biblioteki runtime/toolkit, np.:

```text
libcudart.so
libcublas.so
```

Bez nich aplikacja może pokazać:

```text
runtime: Cpu, useGpu=True
Skonfigurowano UseGpu=true, ale Whisper.net załadował runtime CPU
```

Dlatego trzeba zainstalować CUDA Toolkit.

---

## 5. Instalacja CUDA Toolkit 13.x

Dodaj oficjalne repozytorium CUDA dla Ubuntu 24.04:

```bash
cd /tmp

wget https://developer.download.nvidia.com/compute/cuda/repos/ubuntu2404/x86_64/cuda-keyring_1.1-1_all.deb

sudo dpkg -i cuda-keyring_1.1-1_all.deb

sudo apt update
```

Sprawdź dostępne wersje CUDA Toolkit 13:

```bash
apt-cache search cuda-toolkit | grep 13
```

Zainstaluj dostępną wersję, np.:

```bash
sudo apt install -y cuda-toolkit-13-0
```

albo, jeśli dostępna jest nowsza:

```bash
sudo apt install -y cuda-toolkit-13-2
```

---

## 6. Konfiguracja PATH i bibliotek CUDA

Dodaj CUDA do środowiska użytkownika:

```bash
echo 'export PATH=/usr/local/cuda/bin:$PATH' >> ~/.bashrc
echo 'export LD_LIBRARY_PATH=/usr/local/cuda/lib64:$LD_LIBRARY_PATH' >> ~/.bashrc

source ~/.bashrc
```

Dodaj CUDA do cache bibliotek systemowych:

```bash
echo "/usr/local/cuda/lib64" | sudo tee /etc/ld.so.conf.d/cuda.conf
sudo ldconfig
```

Sprawdź instalację:

```bash
nvcc --version
```

Sprawdź, czy system widzi biblioteki CUDA:

```bash
ldconfig -p | grep -Ei "libcudart|libcublas|libcuda"
```

Poprawnie powinny pojawić się m.in.:

```text
libcudart.so
libcublas.so
libcuda.so
```

---

## 7. Instalacja .NET 10 SDK

Zainstaluj .NET 10 SDK:

```bash
sudo apt update
sudo apt install -y dotnet-sdk-10.0
```

Sprawdź instalację:

```bash
dotnet --info
dotnet --list-sdks
dotnet --list-runtimes
```

Wynik powinien zawierać SDK `10.0.x`.

---

## 8. Pobranie repozytorium

Przejdź do katalogu domowego użytkownika:

```bash
cd /home/aubuntu
```

Sklonuj repozytorium:

```bash
git clone https://github.com/arturgarbacikasse/ADSTranskrypcja.git
```

Wejdź do projektu:

```bash
cd /home/aubuntu/ADSTranskrypcja/WhisperBenchmark
```

Jeżeli repozytorium już istnieje:

```bash
cd /home/aubuntu/ADSTranskrypcja
git pull
cd WhisperBenchmark
```

---

## 9. Przywrócenie pakietów i build

```bash
cd /home/aubuntu/ADSTranskrypcja/WhisperBenchmark

dotnet restore
dotnet build -c Release
```

Sprawdź, czy aplikacja startuje:

```bash
dotnet run -c Release -- --help
```

Oczekiwane tryby:

```text
single
soak
sweep
```

---

## 10. Wymagane pakiety NuGet w projekcie

Projekt powinien zawierać pakiety Whisper.net, w tym runtime CUDA.

Sprawdź:

```bash
grep -i "Whisper.net" WhisperBenchmark.csproj
```

Oczekiwane pakiety:

```xml
<PackageReference Include="Whisper.net" Version="..." />
<PackageReference Include="Whisper.net.Runtime" Version="..." />
<PackageReference Include="Whisper.net.Runtime.Cuda" Version="..." />
<PackageReference Include="Whisper.net.Runtime.Cuda12" Version="..." />
```

Dla nowych driverów NVIDIA / CUDA 13 preferowany jest:

```text
Whisper.net.Runtime.Cuda
```

Dla starszych konfiguracji może być używany fallback:

```text
Whisper.net.Runtime.Cuda12
```

---

## 11. Katalogi danych

Projekt może używać katalogów lokalnych w repo:

```text
/home/aubuntu/ADSTranskrypcja/WhisperBenchmark/Data/Input
/home/aubuntu/ADSTranskrypcja/WhisperBenchmark/Data/Output
/home/aubuntu/ADSTranskrypcja/WhisperBenchmark/Models
```

Można też używać katalogów systemowych:

```text
/data/input
/data/output
/data/models
```

Utworzenie katalogów systemowych:

```bash
sudo mkdir -p /data/input /data/output /data/models
sudo chown -R aubuntu:aubuntu /data
```

---

## 12. Format plików wejściowych WAV

Aplikacja oczekuje plików:

```text
mono
16 kHz
PCM 16-bit
```

Nazewnictwo:

```text
{callId}_{participantId}.wav
```

Przykład:

```text
call001_1.wav
call001_2.wav
call002_1.wav
call002_2.wav
```

Przykładowa lokalizacja:

```text
./Data/Input/call001_1.wav
./Data/Input/call001_2.wav
```

---

## 13. Konwersja audio do poprawnego WAV

Przykład konwersji MP3 do WAV wymaganego przez benchmark:

```bash
ffmpeg -i input.mp3 -ac 1 -ar 16000 -sample_fmt s16 ./Data/Input/call001_1.wav
```

Przykład konwersji dowolnego WAV do wymaganego formatu:

```bash
ffmpeg -i input.wav -ac 1 -ar 16000 -sample_fmt s16 ./Data/Input/call001_1.wav
```

Sprawdzenie pliku:

```bash
ffprobe ./Data/Input/call001_1.wav
```

---

## 14. Pierwszy test SINGLE

Uruchom pojedynczy plik:

```bash
cd /home/aubuntu/ADSTranskrypcja/WhisperBenchmark

dotnet run -c Release -- \
  single \
  --file ./Data/Input/call001_1.wav
```

Poprawny log GPU powinien zawierać:

```text
runtime: Cuda, useGpu=True, gpuDevice=0
```

Przykład poprawnego wyniku:

```text
Załadowano model Whispera ggml-large-v3-turbo.bin ... (runtime: Cuda, useGpu=True, gpuDevice=0).
Plik: call001_1.wav, audio=125.68s, processing=6.64s, rtf=18.94x
```

---

## 15. Test SOAK

Krótki test obciążeniowy:

```bash
dotnet run -c Release -- \
  soak \
  --input ./Data/Input \
  --output ./Data/Output \
  --duration-minutes 2 \
  --gpu-concurrency 1
```

Dla kart z małą ilością VRAM, np. RTX 3050 Ti 4 GB, zaczynać od:

```text
--gpu-concurrency 1
```

Potem można testować:

```text
--gpu-concurrency 2
```

Dla dużych kart, np. L40 48 GB, można testować większe wartości:

```text
1,2,4,8,16
```

---

## 16. Test SWEEP

Sweep porównuje różne wartości równoległości GPU:

```bash
dotnet run -c Release -- \
  sweep \
  --input ./Data/Input \
  --output ./Data/Output \
  --duration-minutes 10 \
  --concurrency 1,2,4
```

Dla RTX 3050 Ti 4 GB nie zaczynać od dużych wartości. Najpierw testować `1`, potem ewentualnie `2`.

---

## 17. Wybór modelu i `--gpu-concurrency` pod dostępny VRAM

`WhisperBenchmark` w trybie `dataset` / `soak` ładuje **niezależną kopię modelu w VRAM dla każdego slotu `--gpu-concurrency`** (świadomy wybór – każdy worker ma własny `whisper_context`, inaczej CUDA crashuje przy współbieżnym dostępie). Czyli realne zużycie VRAM to:

```text
VRAM ≈ slots * (rozmiar_modelu + KV_cache_per_slot + bufory_aktywacji)
```

Dla `BeamSize=5` (domyślne `appsettings.json`) `KV_cache + bufory` per slot to zwykle 0.3–0.7 GB (im dłuższe audio, tym więcej).

### Estymata per slot (BeamSize=5)

| Model | Wagi w VRAM | + KV/aktywacje (per slot) | Razem per slot |
|---|---|---|---|
| `ggml-large-v3-turbo.bin` (FP16) | ~1.6 GB | ~0.4–0.7 GB | **~2.0–2.3 GB** |
| `ggml-large-v3.bin` (FP16) | ~3.1 GB | ~0.5–0.8 GB | **~3.6–3.9 GB** |
| `ggml-medium.bin` (FP16) | ~1.5 GB | ~0.3–0.5 GB | **~1.8–2.0 GB** |
| `ggml-medium-q5_0.bin` | ~0.5 GB | ~0.3–0.5 GB | **~0.8–1.0 GB** |
| `ggml-large-v3-turbo-q5_0.bin` | ~0.6 GB | ~0.3–0.5 GB | **~0.9–1.1 GB** |

### Rekomendowane `--gpu-concurrency` per karta

| Karta (VRAM) | `large-v3-turbo` FP16 | `large-v3-turbo-q5_0` | `medium-q5_0` | `large-v3` FP16 |
|---|---|---|---|---|
| **RTX 3050 / 3050 Ti 4 GB (laptop)** | **1** | 1–2 | **2** (3 może padać OOM) | nie zmieści się |
| RTX 3060 / 4060 8 GB | 2 | 4 | 6 | 1–2 |
| RTX 4070 / 4080 12–16 GB | 4 | 8 | 12 | 3 |
| L40 48 GB | 8–16 | 32+ | 32+ | 8 |

> Liczby są punktem startowym – realne nasycenie znajduje się trybem `sweep` (`--concurrency 1,2,4,8`).

### Jak zmniejszyć model

W `appsettings.json`:

```jsonc
{
  "Transcription": {
    "ModelFileName": "ggml-medium-q5_0.bin",
    "AutoDownloadModel": true,
    "Whisper": {
      "SamplingStrategy": "BeamSearch",
      "BeamSize": 5
    }
  }
}
```

Albo dla jeszcze mniejszego zużycia VRAM (ale niższej jakości / niższego RTF na małych plikach):

```jsonc
{
  "Whisper": {
    "SamplingStrategy": "Greedy",
    "BeamSize": 1
  }
}
```

`Greedy` likwiduje większość KV cache beam search – pozwala podnieść `--gpu-concurrency` o 1–2 sloty na każdej karcie.

### Reguła kciuka przy planowaniu testu

1. Odczytaj wolny VRAM: `nvidia-smi --query-gpu=memory.free --format=csv,noheader,nounits` → wynik w MiB.
2. Wybierz model z tabeli wyżej i odczytaj "razem per slot".
3. `safe_slots = floor((free_MiB - 500) / per_slot_MiB)` (500 MiB zapasu dla sterownika i kontekstu Whisper.net).
4. Zacznij od `safe_slots`, jeśli przejdzie cały `dataset` bez crashu, dopiero wtedy testuj `safe_slots + 1`.

Przykład na RTX 3050 Ti 4 GB (3759 MiB free) z `medium-q5_0` (1.0 GB per slot):

```text
safe_slots = floor((3759 - 500) / 1024) = floor(3.18) = 3
```

W praktyce na tej konfiguracji `--gpu-concurrency 3` zwykle padnie przy dłuższych plikach – `2` jest stabilne. Zostaw 1 slot zapasu względem teoretycznej estymaty.

---

## 18. Monitoring GPU

### a) Wbudowany `gpu-metrics.csv`

Aplikacja sama zbiera próbki gdy `Benchmark.CollectGpuMetrics: true`. Trafia do `<OutputDirectory>/gpu-metrics.csv`. Kolumny:

```text
timestamp, index, name, utilization.gpu [%], memory.used [MiB], memory.total [MiB], power.draw [W], temperature.gpu [C]
```

Częstotliwość kontrolowana przez `Benchmark.GpuMetricsIntervalSeconds` (domyślnie 10). Na małych kartach warto skrócić do `1`, żeby wyłapać peaki:

```jsonc
"GpuMetricsIntervalSeconds": 1
```

> Uwaga: jeśli proces wywali się natywnym `abort()` (np. CUDA OOM w whisper.cpp), `GpuMetricsCollector` może nie zdążyć zapisać końcówki CSV – wtedy patrz pkt **b)**.

### b) Drugi terminal – `nvidia-smi -l 1`

Najpewniejsza metoda (działa niezależnie od naszego procesu, przeżywa SIGABRT):

```bash
nvidia-smi --query-gpu=timestamp,memory.used,memory.free,memory.total,utilization.gpu,power.draw,temperature.gpu \
           --format=csv -l 1 \
| tee gpu-watch.csv
```

`-l 1` = próbka co 1 sekundę. `tee` pisze równocześnie do pliku i konsoli – po crashu masz historię. Stop `Ctrl+C` po zakończeniu benchmarku.

### c) Dashboard `nvidia-smi dmon`

```bash
nvidia-smi dmon -s pucvmet -c 0 -d 1
```

(`pucvmet` = power, utilization, clocks, video, memory, encoder, temperature; `-c 0` = bez limitu; `-d 1` = co 1s).

### d) `nvtop`

Interaktywny widok jak `htop`:

```bash
nvtop
```

### e) Loop z większą rozdzielczością (0.5 s)

```bash
while true; do
  nvidia-smi --query-gpu=timestamp,memory.used,memory.free,utilization.gpu \
             --format=csv,noheader,nounits
  sleep 0.5
done | tee gpu-watch-0.5s.csv
```

### Playbook: zmierzyć peak VRAM podczas runu

1. **Terminal A** (uruchom **pierwszy**):
   ```bash
   nvidia-smi --query-gpu=timestamp,memory.used,memory.free,utilization.gpu --format=csv -l 1 | tee gpu-watch.csv
   ```
2. **Terminal B** (benchmark):
   ```bash
   cd /home/aubuntu/ADSTranskrypcja/WhisperBenchmark
   dotnet run -c Release --no-launch-profile -- dataset \
     --input ./Data/Input --output ./Data/Output --gpu-concurrency 2 \
     > run.stdout.log 2> run.stderr.log
   echo "exit=$?"
   ```
3. Po zakończeniu w terminalu A `Ctrl+C`. W `gpu-watch.csv` odczytaj **peak `memory.used`** – masz realne zużycie do podstawienia w estymacie z sekcji 17.

---

## 19. Diagnostyka: "Kończy szybko bez wyników" / brak raportu

Objaw: aplikacja loguje `Załadowano instancję modelu N/N` oraz `Dataset: wszystkie X jobów wrzucone do kolejki.`, **brak** `[00:00:10] mode=dataset ...`, **brak** `DATASET BENCHMARK FINISHED`, brak `benchmark-summary.json` (albo stary, z poprzedniego runa). Powłoka wraca do prompta po kilku sekundach.

To prawie zawsze **silent SIGABRT z natywki** (whisper.cpp / ggml / sterownik CUDA): wątek natywny robi `abort()` zanim .NET zdąży złapać wyjątek i odpalić nasz `try/finally` z zapisem raportu.

### Krok 1: odczytaj exit code

```bash
dotnet run -c Release --no-launch-profile -- dataset \
  --input ./Data/Input --output ./Data/Output --gpu-concurrency 3
echo "exit=$?"
```

| `exit` | Co znaczy | Działanie |
|---|---|---|
| `0` | OK | Powinno być `DATASET BENCHMARK FINISHED` – jeśli nie ma, sprawdź `./Data/Output`. |
| `134` | SIGABRT z natywki (`GGML_ASSERT`, CUDA `cudaMalloc` == null) | Najczęściej VRAM OOM – patrz krok 2. |
| `139` | SIGSEGV | Crash natywki – sprawdź `dmesg` i ldd na bibliotekach CUDA. |
| `137` | SIGKILL | Najczęściej kernelowy OOM Killer – `dmesg \| tail` to potwierdzi. |
| `130` | SIGINT (Ctrl+C) | Anulowane – aplikacja powinna zapisać raport częściowy. |
| `2` | Wyjątek .NET | Stack w logu – zwykle błąd konfiguracji. |

### Krok 2: rozdziel stdout / stderr i zajrzyj do stderr

Natywne `GGML_ASSERT(...) failed` i `CUDA error: out of memory` lecą na **stderr**, nasze logi `info:` na **stdout**. Splatają się w jednym oknie i łatwo przegapić ślad. Rozdziel:

```bash
dotnet run -c Release --no-launch-profile -- dataset \
  --input ./Data/Input --output ./Data/Output --gpu-concurrency 3 \
  > run.stdout.log 2> run.stderr.log
echo "exit=$?"
tail -40 run.stderr.log
```

Charakterystyczne wpisy w `run.stderr.log`:

```text
/path/whisper.cpp/ggml/src/ggml-backend.cpp:194: GGML_ASSERT(buffer) failed
```

→ klasyczny CUDA OOM przy alokacji bufora (najczęściej przy 3. instancji modelu na karcie, która nie ma na to miejsca).

```text
CUDA error: out of memory
```

→ to samo, ale podczas alokacji KV cache w trakcie pierwszej transkrypcji (nie podczas ładowania).

### Krok 3: zmierz peak VRAM z drugiego terminala

Patrz sekcja 18.e) – playbook z dwoma terminalami. Jeżeli `memory.used` w `gpu-watch.csv` skoczy do `~memory.total` i zaraz potem `exit ≠ 0`, masz potwierdzony VRAM OOM.

### Krok 4: sprawdź kernel log

```bash
sudo dmesg -T | tail -100 | grep -iE "oom|killed|dotnet|whisper"
journalctl -k --since "5 minutes ago" | tail -100
```

### Krok 5: zwolnij GPU po crashu

Po SIGABRT pamięć GPU bywa nieoddana przez ~10–30 s, czasem dłużej. Zanim odpalisz kolejny run:

```bash
pkill -f WhisperBenchmark
nvidia-smi --query-gpu=memory.used,memory.free --format=csv
```

Jeśli `memory.used` nie spada poniżej ~300 MiB w ciągu minuty, restart sesji X / restart usługi NVIDIA (`sudo systemctl restart nvidia-persistenced`) albo `sudo reboot`.

### Krok 6: napraw

- **Najszybsza naprawa:** obniż `--gpu-concurrency` o 1.
- **Średnia naprawa:** zmień model na mniejszy / skwantyzowany (sekcja 17 – tabela rekomendacji).
- **Drobny zysk:** w `appsettings.json` `Whisper.BeamSize: 1` lub `Whisper.SamplingStrategy: "Greedy"` – mniej KV cache per slot.

### Szybka ściągawka

| Symptom | Najprawdopodobniejsza przyczyna | Pierwszy strzał |
|---|---|---|
| `Załadowano instancję modelu 2/3` i koniec | OOM przy ładowaniu N-tej kopii modelu | `--gpu-concurrency 2`, albo mniejszy model |
| Logi `Załadowano N/N` i `wszystkie jobów wrzucone do kolejki`, ale brak `[00:00:10]` | OOM przy pierwszej alokacji KV cache | `BeamSize: 1` lub `--gpu-concurrency` -1 |
| `[00:00:10]` jest, ale potem `exit=134` / `139` w trakcie | OOM przy dłuższym pliku | mniejszy model lub `BeamSize: 1` |
| `runtime: Cpu, useGpu=True` w logu | Brak CUDA Toolkit / brak `libcudart` | patrz sekcja 21 |

---

## 20. Diagnostyka: czy CUDA runtime jest widoczny

Sprawdź, czy paczki Whisper.net CUDA trafiły do builda:

```bash
cd /home/aubuntu/ADSTranskrypcja/WhisperBenchmark

find bin/Release/net10.0 -type f | grep -Ei "cuda|cublas|whisper|ggml"
```

Oczekiwane pliki:

```text
bin/Release/net10.0/runtimes/cuda/linux-x64/libwhisper.so
bin/Release/net10.0/runtimes/cuda/linux-x64/libggml-cuda-whisper.so
bin/Release/net10.0/runtimes/cuda12/linux-x64/libggml-cuda-whisper.so
```

Sprawdź zależności runtime CUDA:

```bash
ldd bin/Release/net10.0/runtimes/cuda/linux-x64/libggml-cuda-whisper.so | grep -i "not found\|cuda\|cublas\|cudart\|stdc++"
```

Jeśli pojawi się:

```text
libcudart.so.13 => not found
libcublas.so.13 => not found
```

to brakuje CUDA Toolkit albo system nie widzi bibliotek CUDA.

Naprawa:

```bash
echo "/usr/local/cuda/lib64" | sudo tee /etc/ld.so.conf.d/cuda.conf
sudo ldconfig
```

Następnie sprawdź:

```bash
ldconfig -p | grep -Ei "libcudart|libcublas|libcuda"
```

---

## 21. Diagnostyka: aplikacja działa na CPU mimo `UseGpu=true`

Objaw:

```text
runtime: Cpu, useGpu=True
Skonfigurowano UseGpu=true, ale Whisper.net załadował runtime CPU
```

Sprawdź po kolei:

```bash
nvidia-smi
nvcc --version
ldconfig -p | grep -Ei "libcudart|libcublas|libcuda"
```

Sprawdź zależności:

```bash
ldd bin/Release/net10.0/runtimes/cuda/linux-x64/libggml-cuda-whisper.so | grep -i "not found"
```

Jeżeli brakuje `libcudart` albo `libcublas`, zainstaluj CUDA Toolkit 13.x.

Po naprawie uruchom ponownie:

```bash
dotnet run -c Release -- \
  single \
  --file ./Data/Input/call001_1.wav
```

Oczekiwane:

```text
runtime: Cuda
```

---

## 22. Diagnostyka: `nvidia-smi` nie działa

Sprawdź sterowniki:

```bash
ubuntu-drivers devices
```

Zainstaluj rekomendowany driver:

```bash
sudo ubuntu-drivers install
sudo reboot
```

Jeśli dalej nie działa, sprawdź Secure Boot:

```bash
mokutil --sb-state
```

Jeżeli Secure Boot jest włączony i moduł NVIDIA się nie ładuje, najprościej na prywatnej maszynie wyłączyć Secure Boot w BIOS/UEFI.

---

## 23. Diagnostyka: brak pliku wejściowego

Objaw:

```text
Plik wejściowy nie istnieje: /data/input/call001_1.wav
```

Sprawdź, gdzie naprawdę są pliki:

```bash
ls -la ./Data/Input
ls -la /data/input
```

Uruchom z poprawną ścieżką:

```bash
dotnet run -c Release -- \
  single \
  --file ./Data/Input/call001_1.wav
```

Albo skopiuj pliki do `/data/input`:

```bash
cp ./Data/Input/*.wav /data/input/
```

---

## 24. Cursor / Remote SSH

Na Ubuntu:

```bash
sudo systemctl enable --now ssh
hostname -I
```

Na Windows w pliku:

```text
C:\Users\<WINDOWS_USER>\.ssh\config
```

dodaj:

```sshconfig
Host ubuntu-whisper
    HostName 192.168.66.201
    User aubuntu
    Port 22
    ServerAliveInterval 30
    ServerAliveCountMax 3
```

Test z PowerShell:

```powershell
ssh ubuntu-whisper
```

W Cursor:

```text
Ctrl + Shift + P
Remote-SSH: Connect to Host
ubuntu-whisper
```

Otwórz katalog:

```text
/home/aubuntu/ADSTranskrypcja
```

---

## 25. Komenda kontrolna po pełnej konfiguracji

Po pełnej konfiguracji uruchom:

```bash
cd /home/aubuntu/ADSTranskrypcja/WhisperBenchmark

nvidia-smi
nvcc --version
dotnet --info

dotnet build -c Release

dotnet run -c Release -- \
  single \
  --file ./Data/Input/call001_1.wav
```

Poprawny efekt:

```text
runtime: Cuda
rtf > 1.0x
```

Na RTX 3050 Ti Mobile testowo uzyskano wynik około:

```text
audio=125.68s
processing=6.64s
rtf=18.94x
runtime: Cuda
```

---

## 26. Minimalny skrót instalacyjny

Dla świeżej maszyny Ubuntu 24.04 LTS:

```bash
sudo apt update
sudo apt upgrade -y

sudo apt install -y \
  git curl wget unzip ca-certificates \
  build-essential pkg-config \
  ffmpeg jq htop nvtop openssh-server

sudo systemctl enable --now ssh

sudo ubuntu-drivers install
sudo reboot
```

Po restarcie:

```bash
nvidia-smi

cd /tmp
wget https://developer.download.nvidia.com/compute/cuda/repos/ubuntu2404/x86_64/cuda-keyring_1.1-1_all.deb
sudo dpkg -i cuda-keyring_1.1-1_all.deb
sudo apt update
apt-cache search cuda-toolkit | grep 13

sudo apt install -y cuda-toolkit-13-0

echo 'export PATH=/usr/local/cuda/bin:$PATH' >> ~/.bashrc
echo 'export LD_LIBRARY_PATH=/usr/local/cuda/lib64:$LD_LIBRARY_PATH' >> ~/.bashrc
source ~/.bashrc

echo "/usr/local/cuda/lib64" | sudo tee /etc/ld.so.conf.d/cuda.conf
sudo ldconfig

sudo apt install -y dotnet-sdk-10.0

cd /home/aubuntu
git clone https://github.com/arturgarbacikasse/ADSTranskrypcja.git

cd /home/aubuntu/ADSTranskrypcja/WhisperBenchmark
dotnet restore
dotnet build -c Release
dotnet run -c Release -- --help
```

Test GPU:

```bash
dotnet run -c Release -- \
  single \
  --file ./Data/Input/call001_1.wav
```