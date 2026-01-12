# Gillian's GTA IV Setup Utility
Semi-automatically installs DXVK and launch options for your GTA IV installation (+extra). It automatically checks your hardware and what options should be available (aswell as setting defaults).

![image](https://github.com/user-attachments/assets/347f6315-570a-49fc-8a5f-2e4677373029)

## Usage
- Launch the tool.
- Select your game folder, the one that includes `GTAIV.exe`.
- Press `Install DXVK`, after which, press `Setup launch options`.
- If experienced, play around with the toggles. Defaults should be fine, however, as they're automatically tailored to your hardware.
- Done!

## Features
- Automatically installing the best version of DXVK supported by your hardware by checking its Vulkan capabilities.
- Automatically sets up your launch options, including monitor details and VRAM (VRAM not available for versions older than 1.0.8.0; have to paste options manually on 1.2)
- Accounting for multi-GPU setups, both during DXVK setup and setting up the launch options.
- Detects whether some features are unsupported by your hardware or installed mods.
- Detects if ZolikaPatch is outdated (by checking if the latest option is missing from the ini) and prompts the user to update it.
- Detects if ZolikaPatch and/or FusionFix are installed and prompts to edit their configuration files to be compatible with eachother.
- Properly enables/disables Borderless Fullscreen if using ZolikaPatch or FusionFix
- Warns the user if they have IVSDK .NET and DXVK installed at the same time as RTSS is enabled.
- Prompts the user to fix GFWL achievements if using GFWL (or revert fixing them; this will trigger if using XLivelessNess at the moment).
- Removing `GFWLDLCs.asi` and enabling `LoadDLCs` if up-to-date ZolikaPatch is present.
- Detect obsolete `commandline.txt` options and remove them accordingly.
- Warn the user if they're using `commandline.txt` on Steam version.
- Warn the user if they're using the outdated `dsound.dll` and prompt them to downoad the latest Ultimate ASI Loader.
- Providing tips for what the launch options actually do. And *not* providing useless options.

## Contribution
Contribution is highly welcome. I'm poorly experienced with C#, but this rewrite was needed for many reasons. And so, the current code is extremely clunky and works out of prayers.

## Attribution
Following NuGet packages were used to create this app:

- [NLog](https://www.nuget.org/packages/NLog) by Jarek Kowalski, Kim Chriestensen, Julian Verdurmen - used for logging.
- [FluentIcons.Avalonia](https://www.nuget.org/packages/FluentIcons.Avalonia) by davidxuang - used for icons.
- [MessageBox.Avalonia](https://www.nuget.org/packages/MessageBox.Avalonia) by Lary - used for stylized message boxes.
- And Microsoft's official packages such as [System.Management](https://www.nuget.org/packages/System.Management/) for convenience and functional code.

And these were used during development, but were dropped along the way:

- [ByteSize](https://www.nuget.org/packages/ByteSize) by Omar Khudeira - used to calculate and convert the VRAM correctly.
- [Microsoft-WindowsAPICodePack-Shell](https://www.nuget.org/packages/WindowsAPICodePack-Shell) by rpastric, contre, dahall - allows to create a Choose File dialogue box.
- [SharpZipLib](https://www.nuget.org/packages/SharpZipLib) by ICSharpCode - used for extracting a .tar.gz archive provided by DXVK.
- [PromptDialog](https://www.nuget.org/packages/PromptDialog/) by pythonizo - used to prompt the VRAM as a failsafe, as I was too lazy to create my own WPF window for that.
- [ini-parser](https://github.com/rickyah/ini-parser) by Ricardo Amores Harnandes - was used to edit ZolikaPatch and FusionFix ini files, replaced later due to issues with it.
