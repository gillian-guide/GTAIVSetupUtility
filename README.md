# Gillian's GTA IV Setup Utility
Semi-automatically installs DXVK and launch options for your GTA IV installation (+extra). It checks your hardware and tailors the available options and defaults to it.

Built with .NET 10 and Avalonia 12. Runs on Windows and, through Wine/Proton, on Linux.

<img width="1007" height="584" alt="image" src="https://github.com/user-attachments/assets/ac01eb25-bf25-4121-ac94-a9347366452c" />

## Usage
- Launch the tool. If `GTAIV.exe` is in the same folder as the utility, that folder is selected automatically.
- Otherwise select your game folder, the one that includes `GTAIV.exe`.
- Press `Install DXVK`, after which, press `Setup launch options`.
- If experienced, play around with the toggles. Defaults should be fine, however, as they're automatically tailored to your hardware.
- If DLSS-IV is installed in the game folder, its section appears. You can turn it on/off there, pick your upscaler and preset, update DLSS/FSR and toggle HDR.
- Done!

## Features
- Automatically installs the best DXVK version supported by your hardware by checking its Vulkan capabilities.
- Automatically sets up your launch options, including monitor details and VRAM (VRAM not available for versions older than 1.0.8.0; options have to be pasted manually on 1.2).
- Accounts for multi-GPU setups, both during DXVK setup and when building the launch options.
- DLSS-IV section, which allows you to turn the mod on/off, choose DLSS or FSR, pick a DLSS/FSR preset/profile and enable HDR. When enabled, the latest DXVK is ensured.
- Detects whether some features are unsupported by your hardware or installed mods.
- Detects if ZolikaPatch is outdated (by checking if the latest option is missing from the ini) and prompts the user to update it.
- Detects if ZolikaPatch/OpenPreCEPatch and/or FusionFix are installed and fixes their configuration files for compatibility.
- Properly enables/disables Borderless Fullscreen if using ZolikaPatch, OpenPreCEPatch or FusionFix.
- Warns the user if they have IVSDK .NET and DXVK installed at the same time as RTSS is enabled.
- Prompts the user to fix GFWL achievements if using GFWL (or revert fixing them). OpenPreCEPatch keeps its Steam achievement mirror enabled next to GFWL.
- Removes `GFWLDLCs.asi` and enables `LoadDLCs` if an up-to-date ZolikaPatch or OpenPreCEPatch is present.
- Detects obsolete `commandline.txt` options and removes them accordingly.
- Warns the user if they're using `commandline.txt` on the Steam version.
- Warns the user if they're using the outdated `dsound.dll` and prompts them to download the latest Ultimate ASI Loader.
- Provides tips for what the launch options actually do.

## Requirements
- Windows 10/11 x64, or Linux with Wine/Proton.
- To build: the .NET 10 SDK (pinned by `global.json`).

## Build
```
dotnet build GTAIVSetupUtility.csproj -c Debug
dotnet publish GTAIVSetupUtility.csproj -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:PublishTrimmed=true -p:TrimMode=link \
  -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish
```
NuGet versions are centralized in `Directory.Packages.props`; code style/analyzer defaults are in
`Directory.Build.props` and `.editorconfig`. CI also verifies `dotnet format`.

## Contribution
Contribution is highly welcome. Ensure that the functionality is not compromised in any way when contributing. The DLSS-IV module under `Modules/DlssIv/` is intentionally self-contained, but see `AGENTS.md` for the few host hooks it needs.

## Attribution
Following packages were used to create this app:

- [Avalonia UI](https://docs.avaloniaui.net)
- [MessageBox.Avalonia](https://www.nuget.org/packages/MessageBox.Avalonia)
- [NLog](https://www.nuget.org/packages/NLog)
- [System.Management](https://www.nuget.org/packages/System.Management/)
- [SevenZipExtractor](https://www.nuget.org/packages/SevenZipExtractor)