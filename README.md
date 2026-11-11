<div align="center">

# ⚡ QuickShot

### The Modern, Lightning-Fast Screenshot, Scrolling Capture, GIF Recorder & Any-Language OCR Studio for Windows

[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011%20(x64)-blue.svg?style=for-the-badge&logo=windows)](https://github.com/moshclusive/quickshot)
[![Framework](https://img.shields.io/badge/.NET%20Framework-4.8-512BD4.svg?style=for-the-badge&logo=dotnet)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg?style=for-the-badge)](LICENSE)
[![Release](https://img.shields.io/badge/Release-v1.0.0-success.svg?style=for-the-badge)](https://github.com/moshclusive/quickshot/releases)
[![Setup Size](https://img.shields.io/badge/Installer-%3C%2016%20MB-brightgreen.svg?style=for-the-badge)](https://github.com/moshclusive/quickshot/releases/download/v1.0.0/QuickShot-Setup.exe)

<br/>

[**📥 Download QuickShot Setup (v1.0.0)**](https://github.com/moshclusive/quickshot/releases/download/v1.0.0/QuickShot-Setup.exe)

</div>

---

## 🚀 Overview

*QuickShot* is a fast, lightweight Windows screen capture software built in C# using native Win32 APIs for zero-lag performance. It handles full scrolling captures, high-FPS GIFs, multilingual OCR, and floating on-screen pins—without the bloat of heavy capture suites.

---

## 🎯 Highlights

- ⚡ **Instant Overlay** – Launches immediately with zero UI lag using native GDI+ rendering.
- 📜 **Smart Scrolling Capture** – Stitches long pages, documents, and chats while automatically filtering out repetitive sticky headers and floating bars.
- ⚓ **Anchored Annotations** – Drawings and text stay locked to their exact coordinates on the document without shifting as you scroll.
- 🎥 **Clean GIF Recording** – Records smooth animated clips with mouse-click halos; controls stay completely outside the capture area.
- 🔤 **Multilingual OCR** – Extract text directly from your screen in 100+ languages (including English and Bengali) with quick case-conversion tools.
- 🖍️ **Legible Highlighting** – Applies proper blend modes (Multiply for light backgrounds, Screen for dark) so text underneath remains crisp and easy to read.
- 📌 **Pin to Desktop** – Keep any screenshot snippet floating always-on-top as an active reference while you work.
- 📋 **History & Quick Actions** – Search past captures, trigger one-click actions (`Copy`, `Open`, `Delete`), configure global hotkeys, and sync straight to the clipboard.

---

## 📥 Download & Installation

### Option 1: One-Click Installer (Recommended)
Download the standalone installer (`< 16 MB`):
👉 [**Download QuickShot-Setup.exe (v1.0.0)**](https://github.com/moshclusive/quickshot/releases/download/v1.0.0/QuickShot-Setup.exe)

- Self-contained (includes OCR trained models and runtime libraries).
- Optional automatic launch on Windows startup in silent tray mode.
- Clean uninstaller included.

### Option 2: Portable Usage
You can run `QuickShot.exe` directly from the release folder without running an installer.

---

## 🛠️ Feature Walkthrough

### 1. 📐 Area Selection & Precision Snip
- 🔲 **Rectangular Snip** – Drag across single or multi-monitor setups. An on-screen dimension badge displays the exact width × height in real time.
- ➰ **Freehand Lasso** – Outline irregular shapes and organic cutouts smoothly.
- 🔍 **Pixel Magnifier** – An 8× zoom loupe with live RGB color values helps you align edges down to the exact pixel.

---

### 2. 📜 Scrolling Capture
- 🚫 **Sticky Element Filtering** – Automatically detects and ignores fixed navbars, floating chat bubbles, and sticky headers so they don't repeat in the stitched image.
- 🔄 **Seamless Alignment** – Tracks vertical and horizontal movement frame-by-frame for clean stitching without seam tears.
- ⚓ **Anchored Markups** – Add notes while scrolling; drawings and arrows stay locked to their respective page sections instead of drifting over the screen.

---

### 3. 🎞️ Lightweight GIF Recorder
- 🎛️ **Out-of-Frame Controls** – The recording toolbar (Pause, Stop, Restart) sits completely outside the capture area, keeping your output clean.
- 🟡 **Cursor Highlights** – Adds a subtle yellow glow around the cursor so clicks and actions are clear in guides and demos.
- 📦 **Standalone Export** – Generates optimized GIFs instantly using native LZW encoding—no FFmpeg or third-party binaries required.

---

### 4. 🔤 OCR & Text Tools
Extract text from images, videos, protected web pages, and PDF scans in seconds:
- 🌐 **100+ Languages** – Built-in offline OCR powered by Tesseract (supports English, Bengali, and many others).
- 🔀 **One-Click Case Formatting** – Clean up extracted text before copying:
  - **Original** – Keeps the source casing and line breaks intact.
  - **`UPPERCASE`** / **`lowercase`** – Toggles all-caps or all-lowercase.
  - **`Title Case`** – Capitalizes the first letter of each word.
  - **`Single Line`** – Strips newlines for quick pasting into search bars or terminal prompts.

---

### 5. 🎨 Annotation Tools

| Tool | Key | Description |
| :--- | :---: | :--- |
| **Select / Move** | `V` | Select, reposition, resize, or delete annotations. |
| **Rectangle** | `U` | Box important UI areas or content sections. |
| **Ellipse** | `C` | Circle focal points or specific details. |
| **Arrow** | `A` | Point out key elements with a clear directional marker. |
| **Line** | `L` | Draw straight dividers and connectors. |
| **Pen** | `P` | Freehand drawing and handwritten sketches. |
| **Highlighter** | `H` | Non-destructive marker using blend modes so text stays sharp underneath. |
| **Step Counter** | `N` | Drop auto-incrementing numbered badges (`①`, `②`, `③`...) for tutorials. |
| **Text** | `T` | Type notes directly on the image with adjustable sizes and fonts. |
| **Blur / Pixelate** | Toolbar | Obscure API keys, passwords, faces, and sensitive info. |
| **Spotlight** | Toolbar | Dim the surroundings to draw focus to a specific element. |

---

### ⚡ Power Modifiers & Hidden Superpowers

QuickShot includes powerful pro modifiers inspired by industry-standard graphic software:

- 🔒 **`Ctrl` + Rectangle / Ellipse**:
  Holding `Ctrl` while drawing locks a strict **1:1 aspect ratio**, giving you mathematically perfect squares and circles every time.
- 📐 **`Shift` + Line / Arrow**:
  Holding `Shift` while drawing locks orientation to **$0^\circ$, $45^\circ$, or $90^\circ$**, ensuring arrow and line annotations are perfectly level or vertical.
- 🖐️ **`Spacebar` / `Ctrl` + `Spacebar` (Dynamic Panning)**:
  Holding `Spacebar` while dragging allows you to **reposition the active shape or selection box on the fly** before letting go!
- ↩️ **`Ctrl` + `Z` & `Ctrl` + `Y`**:
  Full multi-level Undo and Redo support.
- ⌫ **`Delete`**:
  Deletes the currently selected annotation in `Select` mode (`V`).
- ↵ **`Enter`**:
  Instant confirm and save capture.
- ⎋ **`Esc`**:
  Cancel current action, deselect, or close overlay cleanly.

---

### 6. 📌 Pin to Screen
- Press **Pin** on any capture preview to turn it into an always-on-top borderless floating desk card.
- Drag anywhere across screens, resize freely, or double-click to dismiss. Ideal for comparing specs, reading code references, or following tutorials.

---

### 7. 🗃️ Studio History & Management
- Access your recent captures from the modern dark Dashboard.
- Clean icon-only action buttons on hover:
  - 📋 **Copy**: Copies image or file to Windows clipboard.
  - 📁 **Open**: Reveals file in Windows File Explorer.
  - 🗑 **Delete**: Moves file to recycle bin.
- Configurable backup folders, automated retention cleanup, and customizable hotkeys.

---

## ⌨️ Keyboard Shortcuts

| Action | Default Shortcut | Configurable |
| :--- | :---: | :---: |
| **Capture Region** | `PrintScreen` | ✅ Yes |
| **Scrolling Capture** | Custom / Toolbar | ✅ Yes |
| **Record Screen GIF** | Custom / Toolbar | ✅ Yes |
| **Extract Text (OCR)** | Custom / Toolbar | ✅ Yes |
| **Stop Recording** | `F9` | ✅ Yes |
| **Pause / Resume Recording** | `F10` | ✅ Yes |
| **Confirm / Save** | `Enter` | Fixed |
| **Cancel / Exit** | `Esc` | Fixed |

---

## 🏗️ Building from Source

### Prerequisites
- Windows 10 or 11 (64-bit)
- [.NET Framework 4.8 Developer Pack](https://dotnet.microsoft.com/download/dotnet-framework/net48)
- .NET 8.0+ SDK (for MSBuild execution)
- [Inno Setup 6](https://jrsoftware.org/isdl.php) (optional, for compiling the setup installer)

### Quick Build
1. Clone the repository:
   ```bash
   git clone https://github.com/moshclusive/quickshot.git
   cd quickshot
   ```

2. Build Release binary:
   ```bash
   dotnet build QuickShot/Source/QuickShot.csproj -c Release
   ```
   The compiled executable will be located in `QuickShot/Source/bin/Release/net48/QuickShot.exe`.

3. Build the Single-File Setup Installer:
   ```powershell
   powershell -ExecutionPolicy Bypass -File QuickShot/Source/build.ps1
   ```
   The resulting installer will be generated in `QuickShot/Setup/QuickShot-Setup.exe` (`< 16 MB`).

---


## 📄 License

Distributed under the **MIT License**. See [`LICENSE`](LICENSE) for more information.

---

<div align="center">

Crafted with care by [**Mosh**](https://github.com/moshclusive)

⭐ **If you find QuickShot useful, consider starring the repository on [GitHub](https://github.com/moshclusive/quickshot)!**

</div>
