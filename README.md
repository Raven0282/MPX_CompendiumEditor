<img width="300" height="193" alt="logo-mpx" src="https://github.com/user-attachments/assets/0d41a780-c18c-4089-9276-e5dad8b85725" />

# Masterplan XP - Compendium Editor
*** This is an ALPHA BUILD - Make backup copies of your original files. Use at your own risk. ***


### MasterplanXP module for modifying and adding to the Online Compendium.

## Installation Instructions - WIP
Install the Compendium Editor
  - Windows
    1. Download the .zip archive - https://tools.gamerssyndicate.net/parts/masterplan/MPX_CompendiumEditor.zip
    2. Extract the files from the .zip archive.
    3. Double click (run) CompendiumEditor.Desktop.exe.
 
  - Linux - WIP
  - MacOS - WIP

Any work with the Online Compendium assumes you have downloaded a local copy of the Online Compendium. The Online Compendium is located on Gihub: https://github.com/mbutler/iws.mx-dnd

## How to Edit with the Compendium Editor
1. Open the Compendium Editor  
3. Select a repository folder from the downloaded Online Compendium (e.g., monster)
4. Select an item from the left hand side listing.
5. Edit what you want on the Editor Pane on the center, the left hand pane interactively updates as you make changes.
6. When you are done making changes click the Save Changes button.
7. To verify your changes - Go to your local copy of the Online Compendium and open the index.html file.  A browser window with the Online Compendium will allow you to search for the changes you have made.

## How to Add Entries with the Compendium Editor
1. Open the Compendium Editor
2. Select a repository folder from the Compendium (e.g., monster)
3. Select an item from the left hand side listing.
4. Click the +New Record button.
5. A dialog box appears.  Fill out the Name of the Entry you want, add a source. Click the Start Editing button.

** Note **
When entering a source, the listing should filter based on what sources exist for the specific category. Try to use the sources that already exist in the system instead of creating your own for the same resource. (e.g., if PHB is in the list, don't add Players' Handbook.) If a particular source for the category does not exist enter a new one and try to follow the current convention for naming (i.e., PHB instead of Player's Handbook).
   
6. Modify the selected entry to match what you want to add.  Try to follow the format already created for the entry.  You will see your changes interactively on the Preview Pane.  Since you are adding a new entry don't forget to add the "Published in" information at the bottom of the entry.  Follow that format.
7. When you have finished making your addition click the Commit New button.
8. To verify your changes - Go to your local copy of the Online Compendium and open the index.html file.  A browser window with the Online Compendium will allow you to search for the changes you have made.

## Features
After selecting a repository folder you will see a count of the number of items that are part of that category.  You can use this number to confirm your addition to the records on your local Online Compendium.  The application will update the global count of items as you edit.

Both the Editor and the Preview Pane can be detached from the application and placed anywhere, including a separate monitor for easier (i.e., larger) data entry windows. When the panes are closed they return to the main window.  If you close the main window the panes will NOT close automatically.  Please save your work.

The Compendium Editor is capable of using your style sheets for displaying the Preview. Follow the format and place your file in your %AppData%\Local\CompendiumEditor\Styles folder.


