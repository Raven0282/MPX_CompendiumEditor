# MasterplanXP - 4e Compendium Editor

## Overview
The **Compendium Editor** is a high-performance desktop application built with **Avalonia UI** and **.NET 10**. It is designed to manage, edit, and extend the legacy data repositories used by the MasterplanXP (MPX) ecosystem—specifically for Dungeons & Dragons 4th Edition.

The primary challenge this tool solves is the modification of massive, legacy Javascript-wrapped data files (JSONP) while maintaining strict compatibility with original "unquoted-key" formatting and specific directory structures.

---

## 🚀 Quick Start
1. **Build:** Open the solution in Visual Studio 2022+ or use `dotnet build`.
2. **Launch:** Run `CompendiumEditor.exe`.
3. **Open Repository:** Use the **"Select Folder"** button to target a category folder (e.g., `monster`) or the top-level repository folder containing `catalog.js`.
4. **Edit:** Select a record in the grid, modify the RAW HTML, and click **"Save Changes"**.
5. **Append:** Click **"New Record"** to stage a new entry based on an existing template.

---

## 🏗️ Project Architecture
The project follows a modern **Clean Architecture** approach using **MVVM (CommunityToolkit.Mvvm)**:

*   **Models:** High-performance memory representations of compendium records.
*   **ViewModels:** Orchestrate UI state, commands, and data synchronization logic.
*   **Services (Data Layer):** The "Engine" of the application. Uses the **Strategy Pattern** to handle 20+ distinct data categories (Powers, Monsters, Feats, etc.).
*   **Views:** Cross-platform XAML (AXAML) interfaces with a dynamic CSS-based styling system.

---

## 📁 Technical Documentation
For detailed maintenance guides, please refer to the files in the `/docs` directory:

1. [Architecture & Patterns](./docs/architecture.md) - Deep dive into DI, MVVM, and Strategy Pattern.
2. [Data Layer & Extraction](./docs/data-layer.md) - How we "heal" legacy JSON and manage data shards.
3. [Specialized Writers](./docs/writers.md) - How to maintain or add new category handlers.
4. [Styling & UI](./docs/ui-styling.md) - Guide to the CSS-based previewer and dark mode support.

---

## 🛠️ Key Technologies
*   **Avalonia UI:** Cross-platform XAML framework.
*   **CommunityToolkit.Mvvm:** Source generators for boilerplate-free MVVM.
*   **Microsoft.Extensions.DependencyInjection:** Clean service orchestration.
*   **AvaloniaEdit:** Advanced code editor for RAW markup.
*   **HtmlRenderer.Avalonia:** High-fidelity rendering of 4e stat-blocks.
