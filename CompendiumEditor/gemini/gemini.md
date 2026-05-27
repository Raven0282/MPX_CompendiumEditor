### **MasterplanXP - 4e Compendium Editor**

## Overview

* **MasterplanXP is a desktop/browser tool for Dungeon Masters of Dungeons & Dragons 4th Edition. MasterplanXP (MPX) is a succesor to Masterplan (OMP). Separate modules handle Character Creation (Character Builder), customization of character options (Character Workshop), monster customization and creation (Monster Builder), Adventure creation (Adventure Plotter), Campaign Management (Campaign Trail), and the Online Compendium (Compendium).
* **MPX: AvaloniaUI 12 cross-platform migration, .NET 10, Clean Architecture, MVVM.

**Tech Stack:** Windows 11 / C# / .NET 10.0 / Avalonia UI 12 / CommunityToolkit.Mvvm
** Development IDE:** Visual Studio 2026

### **What Has Been Accomplished - Compendium Editor**

#### **1. Core Infrastructure & Domain Mapping**

* **Strongly Typed Data Models:** Established `CompendiumRecord.cs` to map incoming unstructured JSONP structures directly into high-performance in-memory memory models for safe UI parsing.
* **Encapsulated Architecture:** Enforced clean architectural boundaries by completely divorcing database/file I/O pipelines from platform-specific UI concerns, paving a clear runway for future cross-platform scalability.

#### **2. Low-Level Ingestion Engine (`CompendiumExtractor`)**

* **Zero-Dependency Parsing:** Designed high-performance char-span processing pipelines (`ReadOnlySpan<char>`) to isolate balanced JSON boundaries (`{...}` and `[[...]]`) from legacy JSONP global function wrappers (`od.reader.jsonp_batch_data`, `jsonp_data_listing`).
* **Robust List Matching:** Implemented defensive tracking mechanics within `ExtractArrayPayload` to scan backwards and perfectly balance multi-dimensional arrays, preventing malformed string truncations.

#### **3. Multi-File Synchronization Pipeline (`CompendiumWriter`)**

* **Downward Propagation:** Built a coordinated sync engine to ensure that editing a singular record automatically cascades to update:
1. The specific isolated **Data Shard** (e.g., `feat1234.js`).
2. The master structural tracking **Listing Matrix** (`_listing.js`).
3. The text-searchable index map file (`_index.js`).

* **Defensive I/O Foundations:** Integrated automated `.backup` directories that generate timestamped history layers before modifying source records, ensuring manual or automated rollbacks are always available.

#### **4. Modernized Avalonia UI Presentation Layer**

* **Clean MVVM Bindings:** Wired up fully active, observable pipelines using the Community Toolkit (`[ObservableProperty]`, `[RelayCommand]`) within `MainWindowViewModel.cs`.
* **Advanced Editor Integration:** Successfully embedded **AvaloniaEdit** into `MainWindow.axaml` with active internal state checks (`_isUpdatingEditorDirectly`), enabling full syntax highlighting and robust, fluid, two-way bound text streaming.
* **Resilient Theme Control:** Standardized application initialization inside `App.axaml.cs` to accurately map configuration service parameters straight onto native platform behaviors (`RequestedThemeVariant`).

#### **5. Resolved Legacy JSON Parsing (The 'f' Error)**
   * Root Cause identified: The application was using strict JSON parsing on legacy .js data shards that contained unquoted
     property names (e.g., { feat123: "..." }).
   * Solution: Implemented a "JSON Healer" in the CompendiumExtractor. This uses a robust Regex to normalize unquoted keys into
     strict JSON format before passing them to the parser, allowing the application to read legacy files without the 'f' is an
     invalid start of a property name error.

#### **6. Resolved HTML Character Corruption**
   * Root Cause identified: Default System.Text.Json behavior escapes HTML characters (e.g., < becomes \u003C), which was
     breaking the compatibility of the data files with your existing JS application.
   * Solution: Reconfigured the CompendiumWriter to use JavaScriptEncoder.UnsafeRelaxedJsonEscaping. This ensures that HTML
     tags remain human-readable and functional in the source files.

#### **7. Resolved Zero-Formatting Change Mandate**
   * Challenge: Modern serializers enforce double-quotes on keys, but your legacy application requires unquoted keys in files
     like _index.js.
   * Solution: Developed a Format Post-Processor in the CompendiumWriter. It detects if the original file used unquoted keys
     and surgically strips the double-quotes from the keys after serialization, preserving the exact legacy format and 4-space
     indentation standard.

#### **8. Resolved Editor Word Wrapping**
   * Root Cause identified: The AvaloniaEdit control was failing to wrap text because the horizontal scrollbar was enabled,
     allowing for an infinite horizontal canvas.
   * Solution: Modified MainWindow.axaml to set HorizontalScrollBarVisibility="Disabled", which forces the editor engine to
     respect the viewport width and trigger word wrapping.

#### **9. Resolved 2-Way Binding & State Sync**
   * Root Cause identified: The editor control was not being correctly located during initialization, meaning edits in the UI
     never reached the ViewModel.
   * Solution: Refactored the MainWindow.axaml.cs logic to use a more robust FindControl pattern and manual event
     synchronization. This fixed the HTML Preview (it now updates as you type) and ensured the Save button actually commits
     your new edits.

#### **10. Established Persistent Logging Architecture**
   * Accomplishment: Moved from ephemeral Debug.WriteLine calls to a permanent IDiagnosticLogger service.
   * Outcome: The application now generates timestamped session logs in a /logs folder. This captures the "JSON Healing"
     actions, file splicing operations, and metadata extraction steps, providing a reliable audit trail for development.

#### **Issues**
1. The log file is NOT being created, this needs to be corrected. 
2. After the AI made wholesale changes to the MainWindowViewModel and did not follow directions to make only surgical changes the application has an issue with the ingest of the jsonp files and throws an index out of range exception.  The logs for the issue, a representative _index.js, a _listing.js and a data*.js file are located on the ScratchFiles folder.

#### **Instructions**
1. We are troubleshooting the issues.  We will tackle issues in order, only one issue at a time. Review files and make recommendations.
2. Recommendations need to carefully take the Tech-Stack into account.  Do not provide recommendations if they are not specifically applicable on the Tech-Stack.
3. All the work is Open Source. Do not make recommendations for packages or solutions that require subsciptions or restrictive commercial licenses.
4. Do not change names of variables, methods, etc. There are dependencies on multiple parts of the code and changing names can only be done if the change is propagated across the entire system, and only with prior approval. THIS IS THE MOST IMPORTANT INSTRUCTION.
5. If given approval to make changes to files, the changes must be surgical and only address the current issue. In addition, do not trigger a build.  Builds will be handled manually and output will be provided.
6. When making suggestions try to provide suggestions that improve the performance of the application.
7. You are allowed to say I don't know if you are not certain of the issue.  I'd rather have to manually search for a solution than get a wrong answer.
8. Ask questions when needed.  Always ask one question at a time.

