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

## 17. Monitoring GPU

W drugim terminalu:

```bash
watch -n 1 nvidia-smi
```

Albo:

```bash
nvtop
```

Można też użyć:

```bash
nvidia-smi dmon
```

---

## 18. Diagnostyka: czy CUDA runtime jest widoczny

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

## 19. Diagnostyka: aplikacja działa na CPU mimo `UseGpu=true`

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

## 20. Diagnostyka: `nvidia-smi` nie działa

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

## 21. Diagnostyka: brak pliku wejściowego

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

## 22. Cursor / Remote SSH

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

## 23. Komenda kontrolna po pełnej konfiguracji

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

## 24. Minimalny skrót instalacyjny

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