# Getting Started

This guide will get your development environment set up from scratch.

## What you'll install

- **Git** — version control (how we share code)
- **.NET 8 SDK** — the language and tools the simulation engine is written in
- **VS Code** — code editor with good C# support

---

## 1. Install Git

### Windows
Download and run the installer from https://git-scm.com/download/win. Accept all the defaults.

### Mac
Open Terminal and run:
```
xcode-select --install
```

### Linux / WSL
```
sudo apt update && sudo apt install git
```

Verify it worked:
```
git --version
```

---

## 2. Set up your GitHub account

If you don't have one, create a free account at https://github.com.

Then tell git who you are (replace with your details):
```
git config --global user.name "Your Name"
git config --global user.email "your@email.com"
```

### Set up SSH access to GitHub

This lets you push/pull code without typing a password every time.

1. Generate a key:
   ```
   ssh-keygen -t ed25519 -C "your@email.com"
   ```
   Press Enter three times to accept all defaults.

2. Copy your public key to the clipboard:
   - **Windows/WSL:** `cat ~/.ssh/id_ed25519.pub` then copy the output
   - **Mac:** `pbcopy < ~/.ssh/id_ed25519.pub`
   - **Linux:** `cat ~/.ssh/id_ed25519.pub` then copy the output

3. Go to https://github.com/settings/ssh/new, paste the key, give it a name like "my laptop", and click **Add SSH key**.

4. Test it:
   ```
   ssh -T git@github.com
   ```
   You should see: `Hi <username>! You've successfully authenticated.`

---

## 3. Install .NET 8 SDK

### Windows
Download the .NET 8 SDK installer from https://dotnet.microsoft.com/download/dotnet/8.0 and run it.

### Mac
```
brew install dotnet@8
```
If you don't have Homebrew: https://brew.sh

### Linux / WSL
```
sudo apt update && sudo apt install dotnet-sdk-8.0
```

Verify it worked:
```
dotnet --version
```
You should see something like `8.0.x`.

---

## 4. Install VS Code

Download from https://code.visualstudio.com and install it.

Then install the C# extension:
1. Open VS Code
2. Press `Ctrl+Shift+X` (or `Cmd+Shift+X` on Mac) to open Extensions
3. Search for **C# Dev Kit** and install it (published by Microsoft)

---

## 5. Clone the repository

Pick a folder where you want to keep the project and run:
```
git clone git@github.com:dwilson2547/ecosystem_sim.git
cd ecosystem_sim
```

---

## 6. Build and run the tests

```
cd sim
dotnet test
```

You should see output ending in something like:
```
Passed! - Failed: 0, Passed: 1, Skipped: 0
```

That means everything is working.

---

## 7. Open the project in VS Code

```
code sim/EcosystemSim.sln
```

VS Code will prompt you to install recommended extensions — accept them.

The simulation engine code is in `sim/EcosystemSim/`. The tests are in `sim/EcosystemSim.Tests/`.

---

## 8. Install Godot 4.7 (game frontend only)

The simulation engine (`sim/`) runs without Godot. You only need this if you want to run or edit
the game UI in `godot/`.

Download the **Godot 4.7 Mono** build (the `.NET` variant) from https://godotengine.org/download.

### Linux / WSL

```bash
mkdir -p ~/tools/godot
cd ~/tools/godot
# download the mono build, e.g.:
wget https://github.com/godotengine/godot/releases/download/4.7-stable/Godot_v4.7-stable_mono_linux_x86_64.zip
unzip Godot_v4.7-stable_mono_linux_x86_64.zip
chmod +x Godot_v4.7-stable_mono_linux_x86_64/Godot_v4.7-stable_mono_linux.x86_64
```

**On WSL**, always launch from the terminal — the editor's embedded Game panel does not forward
input correctly on WSL2:

```bash
~/tools/godot/Godot_v4.7-stable_mono_linux_x86_64/Godot_v4.7-stable_mono_linux.x86_64 \
    --path /path/to/ecosystem_sim/godot
```

Godot will compile the C# project automatically on first open (requires .NET 8 SDK from step 3).

---

## Day-to-day workflow

Get the latest code before you start working:
```
git pull
```

After making changes:
```
git add .
git commit -m "brief description of what you changed"
git push
```

Run tests anytime with:
```
cd sim
dotnet test
```
