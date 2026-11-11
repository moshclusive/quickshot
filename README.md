<div align="center">

# ⚡ QuickShot

### The Modern, Lightning-Fast Screenshot, Scrolling Capture, GIF Recorder & Any-Language OCR Studio for Windows

[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011%20(x64)-blue.svg?style=for-the-badge&logo=windows)](https://github.com/moshclusive/quickshot)
[![Framework](https://img.shields.io/badge/.NET%20Framework-4.8-512BD4.svg?style=for-the-badge&logo=dotnet)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg?style=for-the-badge)](LICENSE)
[![Release](https://img.shields.io/badge/Release-v1.0.0-success.svg?style=for-the-badge)](https://github.com/moshclusive/quickshot/releases)
[![Setup Size](https://img.shields.io/badge/Installer-%3C%2016%20MB-brightgreen.svg?style=for-the-badge)](https://github.com/moshclusive/quickshot/releases/download/v1.0.0/QuickShot-Setup.exe)

<br/>

[**📥 Download QuickShot Setup (v1.0.0)**](https://github.com/moshclusive/quickshot/releases/download/v1.0.0/QuickShot-Setup.exe) • [**📖 Documentation**](#-feature-walkthrough) • [**⌨️ Shortcuts**](#-keyboard-shortcuts) • [**🤝 Contributing**](CONTRIBUTING.md)

</div>

---

## 🚀 Overview

**QuickShot** is a next-generation desktop capture suite built from scratch in C# with native Win32 hardware acceleration. It combines the snappy lightweight feel of classic screenshot tools with the advanced power of modern capture studios: pixel-perfect scrolling capture, high-frame-rate GIF recording, universal multi-language OCR, smart optical annotations, and floating pin-to-screen reference cards.

No heavy dependencies. No command-line FFmpeg bloat. Zero lag. Instant startup.

---

## 🎯 Highlights

- **⚡ Blazing Fast**: Zero-latency overlay activation with native GDI+ hardware acceleration.
- **📜 Pixel-Perfect Scrolling Capture**: Auto-scrolls and stitches full-page websites, chats, and documents with smart floating-element sanitization (strips repetitive sticky headers and floating widgets).
- **🔒 Document-Locked Annotations**: Annotations stay 100% locked to their original position on the scrolled document—no drifting when scrolling up or down.
- **🎥 Clean GIF Screen Recorder**: Record high-quality animated GIFs with a floating control bar placed cleanly outside the capture area, plus smooth yellow mouse highlight halos.
- **🧠 Universal Any-Language OCR**: Extract text from any screen region in 100+ languages (English, Bengali, and more) with an instant text-casing transformer.
- **🖍️ True Optical Highlighter**: Employs mathematical Multiply (light mode) and Screen (dark mode) blending—text underneath is always 100% sharp and readable.
- **📌 Pin to Screen**: Pin any capture snippet as an always-on-top floating desktop widget.
- **🎨 Modern Dark UI Studio**: Fast history dashboard with icon-only action cards (`Copy`, `Open`, `Delete`), customizable hotkeys, and automatic clipboard sync.

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

### 1. 📐 Precision Region Capture
- **Rectangular Snip**: Click and drag across any screen or multi-monitor setup. Real-time dimensions pill shows exact width $\times$ height.
- **Freehand Lasso Mode**: Cut out arbitrary, organic shapes and irregular selections with fluid curves.
- **Pixel Magnifier HUD**: Shows 8x zoomed pixel grid and exact RGB color values around the cursor for pixel-level accuracy.

---

### 2. 📜 Next-Gen Scrolling Capture
Tired of screenshot tools that break when pages have sticky headers or floating buttons?
- **Intelligent Sticky Sanitizer**: Automatically detects and prevents repetitive floating widgets (such as floating chat icons or navigation bars) from multiplying in the stitched output.
- **Sub-Pixel Motion Tracking**: Tracks 1-pixel granular movements for ultra-smooth vertical and horizontal document scrolling.
- **100% Anchored Document Annotations**: Draw annotations on the document *before* or *during* scrolling—annotations remain strictly anchored to their target elements even after multiple scroll passes.

---

### 3. 🎞️ Lightweight Screen GIF Recorder
Capture crisp screen animations without heavy third-party software:
- **Clean Outside Control Bar**: The recording HUD (Timer, Stop, Pause, Restart, Cancel) floats strictly **outside** your capture box, keeping your output 100% clean.
- **Subtle Mouse Halo**: Automatically draws a soft translucent yellow halo around your cursor with zero border stroke, ensuring viewers follow your clicks effortlessly.
- **Instant Save**: Pure native color quantization and lossless LZW encoding with no FFmpeg required.

---

### 4. 🔤 Universal Multi-Language OCR & Casing Switcher
Extract text from images, scanned documents, video subtitles, or protected web pages in seconds:
- **Any-Language Engine**: Built-in multi-lingual Tesseract engine detects English, Bengali, and 100+ worldwide languages.
- **Instant Casing Switcher**: Convert recognized text with a single click:
  - **Normal / Original**: Preserves original capitalization and format.
  - **`UPPERCASE`**: Capitalizes every character.
  - **`lowercase`**: Converts entire text to lowercase.
  - **`Title Case`**: Capitalizes the first letter of each word.
  - **`Single Line`**: Strips newline breaks for quick pasting into search bars or terminal commands.

---

### 5. 🎨 Professional Annotation Suite

| Tool | Shortcut | Description |
| :--- | :---: | :--- |
| **Select / Move** | `V` | Select, drag, resize, or delete existing annotations. |
| **Rectangle** | `U` | Clean geometric framing box. |
| **Ellipse / Circle** | `C` | Smooth circular or oval callout. |
| **Arrow** | `A` | Directional indicator with subtle drop shadow for depth. |
| **Line** | `L` | Clean straight connecting rule. |
| **Pen / Freehand** | `P` | Natural vector drawing and handwritten marks. |
| **Highlighter** | `H` | **True optical marker** with Multiply/Screen blending. Text underneath remains 100% legible and crisp! |
| **Step Counter** | `N` | Automatic numbered badge (`①`, `②`, `③`...) for step-by-step documentation. |
| **Text** | `T` | Direct on-canvas typographic annotation with adjustable font sizes. |
| **Blur / Pixelate** | Toolbar | Obscure sensitive passwords, API keys, credentials, or faces. |
| **Spotlight** | Toolbar | Darken surroundings and focus brightness on your focal point. |

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

## 🤝 Contributing

Contributions are what make the open-source community an amazing place to learn, inspire, and create. Any contributions you make are **greatly appreciated**!

Please see our [**Contributing Guide (CONTRIBUTING.md)**](CONTRIBUTING.md) for detailed guidelines on opening issues, feature suggestions, code style, and submitting pull requests.

---

## 📄 License

Distributed under the **MIT License**. See [`LICENSE`](LICENSE) for more information.

---

<div align="center">

Crafted with care by [**Mosh**](https://github.com/moshclusive)

⭐ **If you find QuickShot useful, consider starring the repository on [GitHub](https://github.com/moshclusive/quickshot)!**

</div>
