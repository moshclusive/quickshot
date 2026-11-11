# Contributing to QuickShot

First off, thank you for considering contributing to **QuickShot**! 🎉

QuickShot is a fast, lightweight, and open-source screenshot, scrolling capture, GIF recorder, and OCR utility built in C# for Windows. We welcome contributions from developers, designers, translators, and users of all experience levels.

---

## 🌟 How Can You Contribute?

You can contribute to QuickShot in several ways:
- **Reporting Bugs**: Let us know if something isn't working as expected.
- **Suggesting Features**: Share ideas for new tools, shortcuts, or performance enhancements.
- **Submitting Pull Requests**: Fix issues, improve code quality, or add new capabilities.
- **Improving Documentation**: Fix typos, add tutorials, or clarify feature guides.
- **Language / OCR Support**: Help add or optimize Tesseract language training models.

---

## 🐞 Reporting Bugs

Before submitting a bug report:
1. Check the [Existing Issues](https://github.com/moshclusive/quickshot/issues) to see if the issue has already been reported.
2. If not, open a new issue with:
   - A clear and descriptive title.
   - Steps to reproduce the bug.
   - Expected behavior vs. actual behavior.
   - Your Windows version (Windows 10, Windows 11, etc.).
   - Screenshots or GIFs illustrating the issue if applicable.

---

## 💡 Suggesting Enhancements

Feature requests are very welcome! When creating an issue:
- Explain why this feature would be useful to QuickShot users.
- Describe how you envision the feature working (UI layout, shortcuts, options).
- Provide mockups or reference screenshots if available.

---

## 💻 Developing & Submitting Code (Pull Requests)

### Development Setup
1. **Fork the repository** on GitHub: `https://github.com/moshclusive/quickshot`.
2. **Clone your fork** locally:
   ```bash
   git clone https://github.com/YOUR-USERNAME/quickshot.git
   cd quickshot
   ```
3. **Prerequisites**:
   - Windows 10 or 11 (x64)
   - .NET Framework 4.8 SDK or Visual Studio 2022 / JetBrains Rider
   - [Inno Setup 6](https://jrsoftware.org/isdl.php) (only required if building the installer)

4. **Build the Project**:
   ```bash
   dotnet build QuickShot/Source/QuickShot.csproj -c Release
   ```
   Or run the automated build script in PowerShell:
   ```powershell
   powershell -ExecutionPolicy Bypass -File QuickShot/Source/build.ps1
   ```

### Code Style Guidelines
- Maintain clean, readable C# code matching existing style conventions.
- Keep dependencies minimal—QuickShot's core philosophy is zero bloat, high responsiveness, and a tiny footprint (< 16 MB installer).
- Always ensure native Win32 resources (`Bitmap`, `Graphics`, `Pen`, `Brush`, `Font`, hooks) are properly disposed.
- Preserve existing comments and docstrings.

### Submitting a PR
1. Create a new branch for your feature or bugfix:
   ```bash
   git checkout -b feature/my-new-feature
   ```
2. Commit your changes with meaningful commit messages:
   ```bash
   git commit -m "feat: add custom hotkey for quick pin"
   ```
3. Push to your fork:
   ```bash
   git push origin feature/my-new-feature
   ```
4. Open a **Pull Request** against the `main` branch of `moshclusive/quickshot`.

---

## 📜 Code of Conduct

Please treat all members of the community with respect, kindness, and empathy. Harassment or discriminatory behavior will not be tolerated.

---

## 💬 Questions or Help?

Feel free to start a discussion or open an issue on the [QuickShot GitHub repository](https://github.com/moshclusive/quickshot). Thank you for helping make QuickShot better for everyone!
